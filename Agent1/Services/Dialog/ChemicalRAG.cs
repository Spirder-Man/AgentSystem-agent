
using Agent1.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Agent1.Services
{
    /// <summary>
    /// 化工RAG类，用于加载和使用化工知识库。
    /// 支持 PDF/DOC/DOCX/TXT 多格式文档的提取、清洗、语义分块和双存储。
    /// </summary>
    public class ChemicalRAG
    {
        /// <summary>
        /// 知识库服务实例，用于加载和查询化工知识库。
        /// </summary>
        private readonly IKnowledgeBaseService _knowledgeBase;
        /// <summary>
        /// 知识库路径，包含国标、园区规则和历史案例目录。
        /// </summary>
        private readonly string _knowledgeBasePath;
        /// <summary>
        /// 数据库服务（可选），用于启动加速：检查 DB 是否已有数据，跳过重复嵌入生成。
        /// </summary>
        private readonly IDatabaseService? _databaseService;

        // K2-K5: 文档处理管道组件
        private readonly PdfExtractor _pdfExtractor = new();
        private readonly DocExtractor _docExtractor = new();
        private readonly TextCleaner _textCleaner = new();
        private readonly SemanticChunker _semanticChunker = new();
        // [OCR] 扫描件 PDF 视觉 OCR 回退服务（可选，null 表示未启用）
        private readonly PdfOcrService? _pdfOcr;

        // K8: 加载统计
        private int _totalFiles;
        private int _successFiles;
        private int _partialFiles;
        private int _failedFiles;
        private int _skippedFiles;
        private int _totalChunks;
        private int _garbledChunks;   // [#5 FIX] 被乱码过滤器拒收的块数
        private readonly List<string> _failedFileList = new();
        /// <summary>
        /// 构造函数，初始化化工RAG实例。
        /// </summary>
        /// <param name="knowledgeBasePath">知识库路径，包含国标、园区规则和历史案例目录。</param>
        /// <param name="knowledgeBase">知识库服务实例，用于加载和查询化工知识库。</param>
        /// <param name="databaseService">数据库服务（可选），传入后启用启动加速：DB 有数据则跳过文件扫描和嵌入生成。</param>
        /// <param name="pdfOcrService">视觉 OCR 服务（可选），传入后扫描件 PDF（文本层过薄）自动走视觉模型逐页 OCR。</param>
        public ChemicalRAG(string knowledgeBasePath, IKnowledgeBaseService knowledgeBase, IDatabaseService? databaseService = null, PdfOcrService? pdfOcrService = null)
        {
            // P0修复：知识库路径健壮解析。
            // `dotnet run --project Agent1.Api` 启动的子进程 CWD 是项目目录而非仓库根，
            // 直接用相对路径 "knowledgebase" 会解析到 Agent1.Api/knowledgebase（不存在真实文档）。
            // 这里将配置路径针对多个稳定锚点（CWD、程序集目录及其各级父目录）解析，
            // 挑选真正包含知识库子目录的那个，保持配置驱动、跨平台、不硬编码绝对路径。
            _knowledgeBasePath = ResolveKnowledgeBasePath(knowledgeBasePath);
            _knowledgeBase = knowledgeBase;
            _databaseService = databaseService;
            _pdfOcr = pdfOcrService;
        }

        // 知识库特征子目录：只要命中其一即认定该目录是真实知识库根。
        private static readonly string[] KnowledgeBaseMarkers =
            { "国标", "园区规则", "历史案例", "化工专业条例" };

        /// <summary>
        /// 将配置的知识库路径（可能是相对路径）解析为真实存在的绝对路径。
        /// 不依赖进程工作目录：依次尝试 CWD、程序集目录及其各级父目录作为锚点，
        /// 选出真正包含知识库特征子目录的路径；均未命中时回退为原路径的绝对形式（保持原行为）。
        /// </summary>
        private static string ResolveKnowledgeBasePath(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
                return configuredPath;

            // 绝对路径且已合格 → 直接采用
            if (Path.IsPathRooted(configuredPath) && QualifiesAsKnowledgeBase(configuredPath))
                return configuredPath;

            var anchors = new List<string>();
            void AddWithAncestors(string? start)
            {
                var dir = start;
                int guard = 0;
                while (!string.IsNullOrEmpty(dir) && guard++ < 12)
                {
                    if (!anchors.Contains(dir)) anchors.Add(dir);
                    dir = Path.GetDirectoryName(
                        dir!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                }
            }
            AddWithAncestors(Directory.GetCurrentDirectory());
            AddWithAncestors(AppContext.BaseDirectory);

            var leaf = Path.GetFileName(
                configuredPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            foreach (var anchor in anchors)
            {
                // 锚点 + 配置路径
                var candidate = Path.GetFullPath(Path.Combine(anchor, configuredPath));
                if (QualifiesAsKnowledgeBase(candidate)) return candidate;

                // 锚点 + 叶子目录名（配置为多级相对路径时的兜底）
                if (!string.IsNullOrEmpty(leaf))
                {
                    var leafCandidate = Path.GetFullPath(Path.Combine(anchor, leaf));
                    if (QualifiesAsKnowledgeBase(leafCandidate)) return leafCandidate;
                }

                // 锚点自身即知识库根
                if (QualifiesAsKnowledgeBase(anchor)) return anchor;
            }

            // 兜底：保持原行为（相对 CWD 解析）
            return Path.IsPathRooted(configuredPath) ? configuredPath : Path.GetFullPath(configuredPath);
        }

        private static bool QualifiesAsKnowledgeBase(string dir)
        {
            if (!Directory.Exists(dir)) return false;
            foreach (var marker in KnowledgeBaseMarkers)
                if (Directory.Exists(Path.Combine(dir, marker))) return true;
            return false;
        }
        /// <summary>
        /// 异步加载化工知识库，包括国标、园区规则和历史案例。
        /// </summary>
        public async Task LoadKnowledgeBaseAsync()
        {
            // ═══ 启动加速：新表已有数据 → 跳过文件扫描和嵌入生成 ═══
            if (_databaseService != null)
            {
                var existingChunkCount = await _databaseService.GetKnowledgeChunkCountAsync();
                if (existingChunkCount > 0 && _knowledgeBase is HybridKnowledgeBaseService hybrid)
                {
                    Console.WriteLine($"\n📦 数据库已有 {existingChunkCount} 条分块，使用快速模式...");
                    await hybrid.RebuildBm25FromDatabaseAsync();
                    Console.WriteLine($"   知识库文档数: {_knowledgeBase.GetDocumentCount()}");
                    return;
                }
            }

            Console.WriteLine("\n========== 加载化工知识库（多格式管道） ==========");
            Console.WriteLine("知识库路径: " + _knowledgeBasePath);
            Console.WriteLine("支持格式: PDF / DOC / DOCX / TXT");
            Console.WriteLine("管道: PdfExtractor → TextCleaner → SemanticChunker → 双存储");
            Console.WriteLine("==================================================");

            if (!Directory.Exists(_knowledgeBasePath))
            {
                Console.WriteLine("知识库目录不存在！");
                return;
            }

            _totalFiles = 0; _successFiles = 0; _partialFiles = 0;
            _failedFiles = 0; _skippedFiles = 0; _totalChunks = 0;
            _garbledChunks = 0;
            _failedFileList.Clear();

            // [增量全格式] 全量加载同样维护 file_tracker，避免“全量后点增量”整库重复入库
            LoadFileTracker();

            var gbDir = Path.Combine(_knowledgeBasePath, "国标");
            if (Directory.Exists(gbDir))
                await LoadDirectoryAsync(gbDir, "国标", "高");

            var specDir = Path.Combine(_knowledgeBasePath, "化工专业条例", "化工专业条例");
            if (Directory.Exists(specDir))
                await LoadDirectoryAsync(specDir, "化工专业条例", "高");

            var parkDir = Path.Combine(_knowledgeBasePath, "园区规则");
            if (Directory.Exists(parkDir))
                await LoadDirectoryAsync(parkDir, "园区规则", "中");

            var caseDir = Path.Combine(_knowledgeBasePath, "历史案例");
            if (Directory.Exists(caseDir))
                await LoadDirectoryAsync(caseDir, "历史案例", "低");

            var h166Dir = Path.Combine(_knowledgeBasePath, "H166—危险化学品化工企业安全生产三级标准化管理制度消防台账资料档案");
            if (Directory.Exists(h166Dir))
            {
                Console.WriteLine("\n   扫描 H166 制度模板目录...");
                await LoadH166DirectoryAsync(h166Dir);
            }

            SaveFileTracker();
            PrintQualityReport();
        }
        // [P1] 知识库文件追踪器：记录每个文件的上次处理时间
        private Dictionary<string, DateTime>? _fileTracker;
        private string FileTrackerPath => Path.Combine(_knowledgeBasePath, "file_tracker.json");

        /// <summary>
        /// [P1 增量加载 → 全格式重构] 仅处理自上次加载以来修改过的文件。
        /// 基于文件最后写入时间 (LastWriteTimeUtc) 判断变更；
        /// 覆盖 PDF/DOC/DOCX/TXT 全部支持格式，与全量加载共用统一单文件管线
        /// （提取 → 清洗 → 语义分块 → 双层表 + BM25），并同步覆盖 H166 制度模板目录。
        /// 更新场景先按相对路径删除旧文档记录（source_path UNIQUE + CASCADE 级联清分块）再重新入库；
        /// 处理失败的文件不写入追踪器，下次增量自动重试。
        /// </summary>
        // [Bug-039 FIX ③] 进程内互斥门：增量更新串行化，并发会造成 DELETE/INSERT 交错撞 source_path UNIQUE（见 Bug-038/039）。
        // WaitAsync(0) 立即失败而非排队 → 调用方据此返回 409。
        private static readonly SemaphoreSlim _incrementalGate = new SemaphoreSlim(1, 1);

        public async Task LoadKnowledgeBaseIncrementalAsync(CancellationToken cancellationToken = default)
        {
            if (!await _incrementalGate.WaitAsync(0, cancellationToken))
                throw new IncrementalAlreadyRunningException();
            try
            {
                await LoadKnowledgeBaseIncrementalCoreAsync(cancellationToken);
            }
            finally
            {
                _incrementalGate.Release();
            }
        }

        // [Bug-039 FIX ②] 停机取消：cancellationToken 绑定调用方的 ApplicationStopping，
        // SIGTERM 后在文件边界收手，杠绝“临终遗写僵尸行”（见 Bug-039 根因①）。
        private async Task LoadKnowledgeBaseIncrementalCoreAsync(CancellationToken cancellationToken)
        {
            LoadFileTracker();

            Console.WriteLine("\n========== 化工知识库增量更新（全格式） ==========");
            Console.WriteLine($"已追踪文件: {_fileTracker?.Count ?? 0} 个");
            Console.WriteLine("支持格式: PDF / DOC / DOCX / TXT");
            Console.WriteLine("==================================================");

            int newFiles = 0, updatedFiles = 0, skippedFiles = 0, failedIncrFiles = 0;

            // 与全量加载共用统一单文件管线，保证增量入库的表结构和元数据完全一致
            async Task ProcessChangedFilesAsync(IEnumerable<string> files, Func<string, Task<bool>> processor)
            {
                foreach (var file in files)
                {
                    // [Bug-039 FIX ②] 停机信号到达时在文件边界安全收手，不把手头文件跑完
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Console.WriteLine("   ⏹️ 收到停机信号(ApplicationStopping)，增量在文件边界安全收手");
                        break;
                    }
                    var lastWrite = File.GetLastWriteTimeUtc(file);
                    bool isTracked = _fileTracker!.TryGetValue(file, out var tracked);
                    if (isTracked && lastWrite <= tracked)
                    {
                        skippedFiles++;
                        continue; // 未修改
                    }

                    // [Bug-039 FIX ①] 无条件防御性删除：DB 是否有残留不能由内存 tracker 推断
                    //（tracker “没追踪” ≠ DB 无行，如临终遗写的僵尸行）。source_path UNIQUE，先删再插保证幂等。
                    await RemoveFileFromKnowledgeBaseAsync(file);

                    Console.WriteLine($"   {(isTracked ? "[更新]" : "[新增]")} {Path.GetFileName(file)}");
                    if (await processor(file))
                    {
                        _fileTracker![file] = lastWrite;
                        if (isTracked) updatedFiles++; else newFiles++;
                    }
                    else
                    {
                        failedIncrFiles++;
                        _fileTracker!.Remove(file); // 失败不入追踪器，下次增量重试
                    }
                }
            }

            var directories = new[]
            {
                (Path.Combine(_knowledgeBasePath, "国标"), "国标", "高"),
                (Path.Combine(_knowledgeBasePath, "化工专业条例", "化工专业条例"), "化工专业条例", "高"),
                (Path.Combine(_knowledgeBasePath, "园区规则"), "园区规则", "中"),
                (Path.Combine(_knowledgeBasePath, "历史案例"), "历史案例", "低"),
            };

            foreach (var (dir, type, priority) in directories)
            {
                await ProcessChangedFilesAsync(
                    EnumerateSupportedFiles(dir, SearchOption.AllDirectories),
                    f => ProcessSingleFileAsync(f, type, priority));
            }

            // [病灶⑤修复] H166 制度模板目录（DOC/DOCX）增量同样覆盖
            var h166Dir = Path.Combine(_knowledgeBasePath, "H166—危险化学品化工企业安全生产三级标准化管理制度消防台账资料档案");
            await ProcessChangedFilesAsync(
                EnumerateSupportedFiles(h166Dir, SearchOption.AllDirectories)
                    .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".doc" or ".docx"),
                ProcessH166FileAsync);

            // [P3 增量更新] 检测已删除的文件并清理对应分块（新表按相对路径 CASCADE，旧表/BM25 按文件名）
            int deletedFiles = 0;
            var deletedKeys = new List<string>();
            foreach (var trackedFile in _fileTracker!.Keys)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (!File.Exists(trackedFile))
                {
                    Console.WriteLine($"   [删除] {Path.GetFileName(trackedFile)}");
                    await RemoveFileFromKnowledgeBaseAsync(trackedFile);
                    deletedKeys.Add(trackedFile);
                    deletedFiles++;
                }
            }
            foreach (var key in deletedKeys)
                _fileTracker.Remove(key);

            SaveFileTracker();
            Console.WriteLine($"\n   增量结果: +{newFiles} 新增, ~{updatedFiles} 更新, ≡{skippedFiles} 跳过, ✗{failedIncrFiles} 失败, -{deletedFiles} 删除");
            Console.WriteLine($"   知识库文档总数: {_knowledgeBase.GetDocumentCount()}\n");
        }

        private void LoadFileTracker()
        {
            try
            {
                if (File.Exists(FileTrackerPath))
                {
                    var json = File.ReadAllText(FileTrackerPath);
                    _fileTracker = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json) ?? new();
                }
                else
                {
                    _fileTracker = new();
                }
            }
            catch
            {
                _fileTracker = new();
            }
        }

        private void SaveFileTracker()
        {
            try
            {
                var json = JsonSerializer.Serialize(_fileTracker ?? new());
                var dir = Path.GetDirectoryName(FileTrackerPath);
                if (dir != null && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(FileTrackerPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 保存文件追踪器失败: {ex.Message}");
            }
        }
        /// <summary>
        /// 将文本内容按段落分块，每个分块最大500个字符。
        /// </summary>
        /// <param name="text">要分块的文本内容。</param>
        /// <param name="maxChunkSize">每个分块的最大字符数。</param>
        /// <returns>包含所有分块的列表。</returns>
        private List<string> SplitTextIntoChunks(string text, int maxChunkSize)
        {
            var chunks = new List<string>();
            var paragraphs = text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            var currentChunk = new StringBuilder();
            int currentSize = 0;

            foreach (var paragraph in paragraphs)
            {
                if (currentSize + paragraph.Length > maxChunkSize && currentSize > 0)
                {
                    chunks.Add(currentChunk.ToString());
                    currentChunk.Clear();
                    currentSize = 0;
                }
                
                if (currentChunk.Length > 0)
                    currentChunk.AppendLine();
                
                currentChunk.Append(paragraph);
                currentSize += paragraph.Length;
            }

            if (currentSize > 0)
            {
                chunks.Add(currentChunk.ToString());
            }

            return chunks;
        }
        // ==================== K6: 多格式文档处理管道 ====================

        /// <summary>
        /// 为文件创建文档记录并插入 knowledge_documents，返回自增 ID
        /// </summary>
        private async Task<int> InsertDocumentForFileAsync(string filePath, string regulationType, string priority,
            string? parentCategory = null, string? regulationNumber = null, string? regulationTitle = null,
            int? pageCount = null, string? extractionQuality = null, bool isFullText = true)
        {
            if (_databaseService == null) return 0; // 无数据库则降级，返回 0

            var fileName = Path.GetFileName(filePath);
            var fileInfo = new FileInfo(filePath);
            var ext = fileInfo.Extension.TrimStart('.').ToLowerInvariant();

            // 计算相对路径（与删除路径共用同一函数，保证删除键=插入键）
            var relativePath = GetRelativeSourcePath(filePath);

            var doc = new KnowledgeDocumentRecord
            {
                SourcePath = relativePath,
                FileName = Path.GetFileNameWithoutExtension(filePath),
                FileFormat = ext,
                FileSizeBytes = fileInfo.Exists ? fileInfo.Length : null,
                RegulationType = regulationType,
                RegulationNumber = regulationNumber,
                RegulationTitle = regulationTitle,
                Priority = priority,
                ParentCategory = parentCategory,
                ExtractionQuality = extractionQuality ?? "good",
                PageCount = pageCount,
                IsFullText = isFullText,
                LastModified = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : null
            };

            return await _databaseService.InsertDocumentAsync(doc);
        }

        // [增量全格式] 支持的文档扩展名（全量与增量共用）
        private static readonly string[] SupportedExtensions = { ".txt", ".pdf", ".doc", ".docx" };

        /// <summary>
        /// 枚举目录下所有支持格式的文档。
        /// 用 "*.*" 通配后按实际扩展名过滤，规避 Windows 下 GetFiles("*.doc") 连带匹配 .docx 的怪癖；
        /// 同时排除 Office 临时文件（~$、~WRL 前缀）。目录不存在时返回空序列。
        /// </summary>
        private static IEnumerable<string> EnumerateSupportedFiles(string dirPath, SearchOption option)
        {
            if (!Directory.Exists(dirPath)) return Enumerable.Empty<string>();
            return Directory.GetFiles(dirPath, "*.*", option)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Where(f =>
                {
                    var name = Path.GetFileName(f);
                    return !name.StartsWith("~$") && !name.StartsWith("~WRL");
                })
                .Distinct()
                .OrderBy(f => f, StringComparer.Ordinal);
        }

        /// <summary>
        /// 计算文件相对知识库根的路径，与 knowledge_documents.source_path 的写入键一致；
        /// 插入（InsertDocumentForFileAsync）与删除（RemoveFileFromKnowledgeBaseAsync）共用，保证键对齐。
        /// </summary>
        private string GetRelativeSourcePath(string filePath)
        {
            var full = Path.GetFullPath(filePath);
            var root = Path.GetFullPath(_knowledgeBasePath);
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return full.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return filePath;
        }

        // 成功处理后登记到文件追踪器（全量路径使用；增量路径在调度器中登记）
        private void TrackFile(string filePath)
        {
            if (_fileTracker == null) return;
            _fileTracker[filePath] = File.GetLastWriteTimeUtc(filePath);
        }

        /// <summary>
        /// 从知识库中彻底移除一个文件的全部数据：
        /// 新表按相对路径删除文档记录（knowledge_chunks 经 ON DELETE CASCADE 级联清理），
        /// 旧表 chemical_documents 与 BM25 内存索引经 RemoveChunksBySourceFileAsync 按文件名清理。
        /// </summary>
        private async Task RemoveFileFromKnowledgeBaseAsync(string filePath)
        {
            if (_databaseService != null)
            {
                var removed = await _databaseService.DeleteKnowledgeDocumentBySourcePathAsync(GetRelativeSourcePath(filePath));
                if (removed > 0)
                    Console.WriteLine($"   🗑️ 已删除文档记录及级联分块: {Path.GetFileName(filePath)}");
                else
                    Console.WriteLine($"   ℹ️ [DBG] DELETE 0 行(无旧记录): {Path.GetFileName(filePath)}"); // [Bug-039 教训④] 让“扑空”可见
            }
            await _knowledgeBase.RemoveChunksBySourceFileAsync(filePath);
        }

        /// <summary>
        /// [增量全格式] 统一单文件处理管线：按扩展名分派到对应处理器，全量与增量共用，
        /// 保证任何入口进来的文件都走同一条 提取 → 清洗 → 语义分块 → 双层表 + BM25 链路。
        /// </summary>
        private async Task<bool> ProcessSingleFileAsync(string filePath, string regulationType, string priority)
        {
            switch (Path.GetExtension(filePath).ToLowerInvariant())
            {
                case ".pdf":
                    return await ProcessPdfFileAsync(filePath, regulationType, priority);
                case ".doc":
                case ".docx":
                    return await ProcessDocFileAsync(filePath, regulationType, priority);
                case ".txt":
                    return await ProcessTxtFileAsync(filePath, regulationType, priority);
                default:
                    return false;
            }
        }

        /// <summary>
        /// 统一目录加载：自动识别 PDF/DOC/DOCX/TXT，成功后登记文件追踪器
        /// </summary>
        private async Task LoadDirectoryAsync(string dirPath, string regulationType, string priority)
        {
            foreach (var f in EnumerateSupportedFiles(dirPath, SearchOption.TopDirectoryOnly))
            {
                if (await ProcessSingleFileAsync(f, regulationType, priority))
                    TrackFile(f);
            }
        }

        /// <summary>
        /// 处理TXT文件并添加到知识库中（清洗 → 语义分块 → 双层表）。
        /// </summary>
        private async Task<bool> ProcessTxtFileAsync(string f, string regulationType, string priority)
        {
            _totalFiles++;
            try
            {
                var content = await File.ReadAllTextAsync(f, Encoding.UTF8);
                var cr = _textCleaner.Clean(content, regulationType);
                var chunks = _semanticChunker.Chunk(cr.CleanText, regulationType);

                // 双层表：先插入文档记录
                var quality = cr.IsGarbled ? "partial" : "good";
                var regNumber = chunks.FirstOrDefault()?.RegulationNumber;
                var documentId = await InsertDocumentForFileAsync(f, regulationType, priority,
                    extractionQuality: quality, regulationNumber: regNumber);

                foreach (var c in chunks)
                {
                    if (RejectGarbledChunk(c.Content, Path.GetFileNameWithoutExtension(f), c.ChunkIndex)) continue;
                    await _knowledgeBase.AddDocumentAsync(c.Content, new Dictionary<string, object>
                    {
                        ["RegulationType"] = regulationType, ["Priority"] = priority,
                        ["SourceFile"] = Path.GetFileNameWithoutExtension(f),
                        ["RegulationNumber"] = c.RegulationNumber ?? "",
                        ["ChapterTitle"] = c.ChapterTitle ?? "",
                        ["ClauseNumber"] = c.ClauseNumber ?? "",
                        ["ChunkIndex"] = c.ChunkIndex,
                        ["PageNumber"] = c.PageNumber ?? (object)DBNull.Value,
                        ["ExtractionQuality"] = quality,
                        ["DocumentId"] = documentId
                    });
                    _totalChunks++;
                }

                if (_databaseService != null && documentId > 0)
                    await _databaseService.UpdateDocumentChunkCountAsync(documentId, chunks.Count);

                _successFiles++;
                return true;
            }
            catch (Exception ex)
            {
                _failedFiles++; _failedFileList.Add($"{Path.GetFileNameWithoutExtension(f)} ({ex.Message})");
                return false;
            }
        }
        /// <summary>
        /// 加载并处理H166目录下的文档文件。
        /// </summary>
        /// <param name="rootDir">根目录路径。</param>
        private async Task LoadH166DirectoryAsync(string rootDir)
        {
            var docFiles = EnumerateSupportedFiles(rootDir, SearchOption.AllDirectories)
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".doc" or ".docx");

            foreach (var file in docFiles)
            {
                if (await ProcessH166FileAsync(file))
                    TrackFile(file);
            }
        }

        /// <summary>
        /// 处理单个 H166 制度模板文档（企业制度/低优先级），全量与增量共用。
        /// </summary>
        private async Task<bool> ProcessH166FileAsync(string file)
        {
            _totalFiles++;
            try
            {
                var result = _docExtractor.Extract(file);

                // 双层表：先插入文档记录
                var quality = result.ExtractionMethod == "FilenameOnly" ? "partial" : "good";
                var parentDir = result.ParentDirectory ?? "";

                if (result.ShouldFullIndex && result.FullText != null)
                {
                    var cr = _textCleaner.Clean(result.FullText, "通用");
                    var chunks = _semanticChunker.Chunk(cr.CleanText, "通用");

                    var documentId = await InsertDocumentForFileAsync(file, "企业制度", "低",
                        parentCategory: parentDir, extractionQuality: quality);

                    foreach (var c in chunks)
                    {
                        if (RejectGarbledChunk(c.Content, result.FileName, c.ChunkIndex)) continue;
                        await _knowledgeBase.AddDocumentAsync(c.Content, new Dictionary<string, object>
                        {
                            ["RegulationType"] = "企业制度", ["Priority"] = "低",
                            ["SourceFile"] = result.FileName, ["ParentDir"] = parentDir,
                            ["RegulationNumber"] = c.RegulationNumber ?? "",
                            ["ChapterTitle"] = c.ChapterTitle ?? "",
                            ["ClauseNumber"] = c.ClauseNumber ?? "",
                            ["ChunkIndex"] = c.ChunkIndex,
                            ["PageNumber"] = c.PageNumber ?? (object)DBNull.Value,
                            ["ExtractionQuality"] = quality,
                            ["DocumentId"] = documentId
                        });
                        _totalChunks++;
                    }

                    if (_databaseService != null && documentId > 0)
                        await _databaseService.UpdateDocumentChunkCountAsync(documentId, chunks.Count);

                    _successFiles++;
                }
                else
                {
                    var documentId = await InsertDocumentForFileAsync(file, "企业制度", "低",
                        parentCategory: parentDir, extractionQuality: "partial", isFullText: false);

                    await _knowledgeBase.AddDocumentAsync(result.Summary, new Dictionary<string, object>
                    {
                        ["RegulationType"] = "企业制度", ["Priority"] = "低",
                        ["SourceFile"] = result.FileName,
                        ["DocumentId"] = documentId
                    });
                    _totalChunks++;
                    _skippedFiles++;
                }
                return true;
            }
            catch (Exception ex)
            {
                _failedFiles++; _failedFileList.Add($"{Path.GetFileNameWithoutExtension(file)} ({ex.Message})");
                return false;
            }
        }
        /// <summary>
        /// 处理PDF文件并添加到知识库中。
        /// </summary>
        /// <param name="filePath">文件路径，包含文件名。</param>
        /// <param name="regulationType">文件类型，例如"国标"、"园区规则"或"历史案例"。</param>
        /// <param name="priority">文件优先级，例如"高"、"中"或"低"。</param>
        private async Task<bool> ProcessPdfFileAsync(string filePath, string regulationType, string priority)
        {
            _totalFiles++;
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            try
            {
                var pdfResult = _pdfExtractor.Extract(filePath);
                // [OCR] 扫描件回退：文本层过薄（仅封面标题/全空）时用视觉模型逐页转写替换薄文本
                if (_pdfOcr != null && pdfResult.ExtractionMethod == "OCR_NEEDED")
                {
                    Console.WriteLine($"   🔍 [{fileName}]: 文本层过薄，启动视觉 OCR（共{pdfResult.PageCount}页）...");
                    var ocr = await _pdfOcr.ExtractTextAsync(filePath);
                    if (ocr.Success)
                    {
                        pdfResult.FullText = ocr.FullText;
                        pdfResult.ExtractionMethod = "OCR";
                        pdfResult.Quality = PdfOcrService.EvaluateOcrQuality(ocr.FullText, ocr.PagesOcred);
                        Console.WriteLine($"   ✅ OCR 完成: {ocr.PagesOcred}/{ocr.PagesTotal}页成功 → {ocr.FullText.Length}字 (质量:{pdfResult.Quality})");

                        // [Bug-041 FIX] 扫描件质量门：按页成功比例分级。不达标 return false（不入库、不记 tracker，
                        // 下轮增量自动重试），避免 Bug-040 瞬时故障被固化为永久薄文本降级。
                        double pageRatio = ocr.PagesTotal > 0 ? (double)ocr.PagesOcred / ocr.PagesTotal : 0;
                        if (pdfResult.Quality == "failed" || pageRatio < 0.5)
                        {
                            Console.WriteLine($"   ✗ [{fileName}]: OCR 质量不达标(质量:{pdfResult.Quality}, 页成功率:{pageRatio:P0} = {ocr.PagesOcred}/{ocr.PagesTotal})，不入库，下轮增量重试");
                            _failedFiles++; _failedFileList.Add($"{fileName} (OCR质量不足:{pdfResult.Quality} {ocr.PagesOcred}/{ocr.PagesTotal}页)");
                            return false;
                        }
                    }
                    else
                    {
                        // [Bug-041 FIX] OCR 彻底失败（服务死亡/0 页）：不沿用触发 OCR 的薄文本入库，return false 触发下轮重试
                        Console.WriteLine($"   ✗ [{fileName}]: OCR 失败({ocr.ErrorMessage})，不入库(不沿用薄文本)，下轮增量重试");
                        _failedFiles++; _failedFileList.Add($"{fileName} (OCR失败:{ocr.ErrorMessage})");
                        return false;
                    }
                }
                //解析文件
                if (pdfResult.Quality == "failed")
                {
                    _failedFiles++; _failedFileList.Add($"{fileName} (PDF解析失败)");
                    return false;
                }
                //检索过滤
                var cleanResult = _textCleaner.Clean(pdfResult.FullText, regulationType);
                //语义分块
                var chunks = _semanticChunker.Chunk(cleanResult.CleanText, regulationType,
                    pdfResult.RegulationNumber ?? fileName, pageNumber: 1);

                // 双层表：先插入文档记录
                var regNumber = pdfResult.RegulationNumber ?? chunks.FirstOrDefault()?.RegulationNumber;
                var documentId = await InsertDocumentForFileAsync(filePath, regulationType, priority,
                    regulationNumber: regNumber, pageCount: pdfResult.PageCount,
                    extractionQuality: pdfResult.Quality);

                //构建字典存储检索后的分词
                foreach (var c in chunks)
                {
                    if (RejectGarbledChunk(c.Content, fileName, c.ChunkIndex)) continue;
                    await _knowledgeBase.AddDocumentAsync(c.Content, new Dictionary<string, object>
                    {
                        ["RegulationType"] = regulationType, ["Priority"] = priority,
                        ["SourceFile"] = fileName,
                        ["RegulationNumber"] = c.RegulationNumber ?? "",
                        ["ChapterTitle"] = c.ChapterTitle ?? "",
                        ["ClauseNumber"] = c.ClauseNumber ?? "",
                        ["ChunkIndex"] = c.ChunkIndex,
                        ["PageNumber"] = c.PageNumber ?? 1,
                        ["ExtractionQuality"] = pdfResult.Quality,
                        ["DocumentId"] = documentId
                    });
                    _totalChunks++;
                }

                if (_databaseService != null && documentId > 0)
                    await _databaseService.UpdateDocumentChunkCountAsync(documentId, chunks.Count);

                if (cleanResult.IsGarbled) _partialFiles++; else _successFiles++;
                Console.WriteLine($"   ✅ [{fileName}]: {pdfResult.PageCount}页 → {chunks.Count}块 (质量:{pdfResult.Quality})");
                return true;
            }
            catch (Exception ex)
            {
                _failedFiles++; _failedFileList.Add($"{fileName} ({ex.Message})");
                return false;
            }
        }
        /// <summary>
        /// 处理DOC/DOCX文件并添加到知识库中。
        /// </summary>
        /// <param name="filePath">文件路径，包含文件名。</param>
        /// <param name="regulationType">文件类型，例如"国标"、"园区规则"或"历史案例"。</param>
        /// <param name="priority">文件优先级，例如"高"、"中"或"低"。</param>
        private async Task<bool> ProcessDocFileAsync(string filePath, string regulationType, string priority)
        {
            _totalFiles++;
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            try
            {
                var docResult = _docExtractor.Extract(filePath);
                if (docResult.ShouldFullIndex && docResult.FullText != null)
                {
                    var cr = _textCleaner.Clean(docResult.FullText, regulationType);
                    var chunks = _semanticChunker.Chunk(cr.CleanText, regulationType);

                    // 双层表：先插入文档记录
                    var quality = docResult.ExtractionMethod == "FilenameOnly" ? "partial" : "good";
                    var documentId = await InsertDocumentForFileAsync(filePath, regulationType, priority,
                        extractionQuality: quality);

                    foreach (var c in chunks)
                    {
                        if (RejectGarbledChunk(c.Content, fileName, c.ChunkIndex)) continue;
                        await _knowledgeBase.AddDocumentAsync(c.Content, new Dictionary<string, object>
                        {
                            ["RegulationType"] = regulationType, ["Priority"] = priority, ["SourceFile"] = fileName,
                            ["RegulationNumber"] = c.RegulationNumber ?? "",
                            ["ChapterTitle"] = c.ChapterTitle ?? "",
                            ["ClauseNumber"] = c.ClauseNumber ?? "",
                            ["ChunkIndex"] = c.ChunkIndex,
                            ["PageNumber"] = c.PageNumber ?? (object)DBNull.Value,
                            ["ExtractionQuality"] = quality,
                            ["DocumentId"] = documentId
                        });
                        _totalChunks++;
                    }

                    if (_databaseService != null && documentId > 0)
                        await _databaseService.UpdateDocumentChunkCountAsync(documentId, chunks.Count);

                    _successFiles++;
                }
                else
                {
                    var documentId = await InsertDocumentForFileAsync(filePath, regulationType, priority,
                        extractionQuality: "partial", isFullText: false);

                    await _knowledgeBase.AddDocumentAsync(docResult.Summary, new Dictionary<string, object>
                    {
                        ["RegulationType"] = regulationType, ["Priority"] = priority,
                        ["SourceFile"] = fileName,
                        ["DocumentId"] = documentId
                    });
                    _totalChunks++;
                    _skippedFiles++;
                }
                return true;
            }
            catch (Exception ex)
            {
                _failedFiles++; _failedFileList.Add($"{fileName} ({ex.Message})");
                return false;
            }
        }
        /// <summary>
        /// [#5 FIX] 入库前的乱码块守门员：命中 GarbledTextDetector 任一规则即拒收，
        /// 打 WRN 日志（含文件名+块序号）并计入 _garbledChunks 统计。
        /// </summary>
        private bool RejectGarbledChunk(string content, string sourceFile, int chunkIndex)
        {
            if (!GarbledTextDetector.IsGarbled(content, out var reason)) return false;
            _garbledChunks++;
            Console.WriteLine($"   ⚠️ WRN 乱码块拒收: {sourceFile} #chunk{chunkIndex} ({reason})");
            return true;
        }

        /// <summary>
        /// 打印知识库加载质量报告。
        /// </summary>          
        private void PrintQualityReport()
        {
            Console.WriteLine("\n========================================");
            Console.WriteLine("        知识库加载质量报告");
            Console.WriteLine("========================================");
            Console.WriteLine($"  文件总数: {_totalFiles}");
            Console.WriteLine($"  ✅ 成功:   {_successFiles}");
            Console.WriteLine($"  ⚠️  部分:  {_partialFiles}");
            Console.WriteLine($"  ❌ 失败:   {_failedFiles}");
            Console.WriteLine($"  ⏭️  跳过:  {_skippedFiles} (空表单/模板)");
            Console.WriteLine("----------------------------------------");
            Console.WriteLine($"  总块数:    {_totalChunks}");
            Console.WriteLine($"  🚫 乱码块拒收: {_garbledChunks}");
            Console.WriteLine($"  知识库文档数: {_knowledgeBase.GetDocumentCount()}");
            if (_failedFileList.Count > 0)
            {
                Console.WriteLine("----------------------------------------");
                Console.WriteLine("  失败文件:");
                foreach (var f in _failedFileList.Take(10))
                    Console.WriteLine($"    - {f}");
                if (_failedFileList.Count > 10)
                    Console.WriteLine($"    ... 共 {_failedFileList.Count} 个");
            }
            Console.WriteLine("========================================\n");
        }

        /// <summary>
        /// 异步执行化工合规检索，返回与查询相关的前topK个结果。
        /// </summary>
        /// <param name="query">用户查询的合规问题。</param>
        /// <param name="topK">返回的结果数量，默认5个。</param>
        /// <returns>包含所有检索结果的列表。</returns>
        public async Task<List<RetrievedChunk>> SearchAsync(string query, int topK = 5)
        {
            Console.WriteLine("\n========== 化工合规检索 ==========");
            Console.WriteLine("查询: " + query);
            Console.WriteLine("----------------------------------");

            // 第一步：BM25检索，多拿一些结果用于重排序
            var bm25Results = await _knowledgeBase.RetrieveAsync(query, topK * 3);
            
            if (bm25Results.Count == 0)
            {
                Console.WriteLine("未找到相关法规！");
                Console.WriteLine("==================================\n");
                return bm25Results;
            }

            // 第二步：优先级重排序（核心修复！）
            var priorityLevels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "国标", 3000 },
                { "化工专业条例", 2800 },
                { "园区规则", 2000 },
                { "历史案例", 1000 },
                { "企业制度", 500 }
            };

            var rerankedResults = bm25Results
                .Select(r => 
                {
                    var priority = 0;
                    if (r.Metadata.ContainsKey("RegulationType"))
                    {
                        var p = r.Metadata["RegulationType"]?.ToString();
                        if (!string.IsNullOrEmpty(p) && priorityLevels.ContainsKey(p))
                        {
                            priority = priorityLevels[p];
                        }
                    }
                    return new { Result = r, AdjustedScore = r.Score + priority };
                })
                .OrderByDescending(x => x.AdjustedScore)
                .Take(topK)
                .Select(x => x.Result)
                .ToList();

            Console.WriteLine("找到 " + rerankedResults.Count + " 条相关结果:\n");

            for (int i = 0; i < rerankedResults.Count; i++)
            {
                var result = rerankedResults[i];
                var metadata = result.Metadata;
                
                Console.WriteLine("【" + (i + 1) + "】 得分: " + result.Score.ToString("F4"));
                
                if (metadata.ContainsKey("RegulationType"))
                    Console.WriteLine("      类型: " + metadata["RegulationType"]);
                
                if (metadata.ContainsKey("Priority"))
                    Console.WriteLine("      优先级: " + metadata["Priority"]);
                
                if (metadata.ContainsKey("SourceFile"))
                    Console.WriteLine("      来源: " + metadata["SourceFile"]);
                
                var contentPreview = (result.Content ?? "").Substring(0, Math.Min(150, (result.Content ?? "").Length));
                Console.WriteLine("      内容: " + contentPreview);
                if ((result.Content ?? "").Length > 150)
                    Console.WriteLine("             ...");
                
                Console.WriteLine();
            }

            Console.WriteLine("==================================\n");
            return rerankedResults;
        }
    }
}
