using Agent1.Config;
using Agent1.Models;
using Npgsql;
using Pgvector;
using Pgvector.Npgsql;
using System.Data;

namespace Agent1.Services
{
    public class DatabaseService : IDatabaseService
    {
        private readonly DatabaseConfig _dbConfig;
        private readonly VectorSearchConfig _vectorConfig;

        public DatabaseService(Config.AppConfig config)
        {
            _dbConfig = config.Database;
            _vectorConfig = config.VectorSearch;
        }

        public async Task<IDbConnection> GetConnectionAsync()
        {
            var connection = CreateConnection();
            await connection.OpenAsync();
            return connection;
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();
                return connection.State == ConnectionState.Open;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TestConnectionAsync] 连接失败: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    Console.Error.WriteLine($"[TestConnectionAsync] 内部异常: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                return false;
            }
        }

        public async Task<string> GetDatabaseInfoAsync()
        {
            var connectionString = BuildConnectionString();
            using var connection = CreateConnection();
            await connection.OpenAsync();

            using var command = new NpgsqlCommand(
                "SELECT current_database(), version(), current_user;",
                connection);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return $"📊 数据库连接信息:\n" +
                       $"  数据库名: {reader[0]}\n" +
                       $"  数据库版本: {reader[1]}\n" +
                       $"  当前用户: {reader[2]}\n" +
                       $"  连接字符串: {connectionString}";
            }
            return "无法获取数据库信息";
        }

        public async Task<List<string>> GetTableNamesAsync()
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();

            using var command = new NpgsqlCommand(
                "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public';",
                connection);

            using var reader = await command.ExecuteReaderAsync();
            var tables = new List<string>();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
            return tables;
        }

        public async Task InitializeDatabaseAsync()
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            
            // 先创建vector扩展（如果已安装）
            await CreateVectorExtensionAsync(connection);
            
            await CreateSessionTableAsync(connection);
            await CreateAuditLogTableAsync(connection);
            await CreateSearchLogTableAsync(connection);
            await CreateChemicalDocumentTableAsync(connection);
            await CreateKnowledgeDocumentTableAsync(connection);
            await CreateKnowledgeChunkTableAsync(connection);
            await CreateLongTermMemoryTableAsync(connection);
            await CreateRefreshTokenTableAsync(connection);
        }

        private async Task CreateVectorExtensionAsync(NpgsqlConnection connection)
        {
            try
            {
                // 检查是否已启用vector扩展
                var checkSql = "SELECT extname FROM pg_extension WHERE extname = 'vector';";
                using var checkCmd = new NpgsqlCommand(checkSql, connection);
                var hasExtension = await checkCmd.ExecuteScalarAsync() != null;

                if (!hasExtension)
                {
                    // 尝试创建扩展
                    var createSql = "CREATE EXTENSION IF NOT EXISTS vector;";
                    using var createCmd = new NpgsqlCommand(createSql, connection);
                    await createCmd.ExecuteNonQueryAsync();
                    Console.WriteLine("   ✅ pgvector扩展创建成功");
                }
                else
                {
                    Console.WriteLine("   ✅ pgvector扩展已存在");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ pgvector扩展检查/创建失败: {ex.Message}");
                Console.WriteLine("   💡 请确保PostgreSQL已安装pgvector扩展");
            }
        }

        private async Task CreateChemicalDocumentTableAsync(NpgsqlConnection connection)
        {
            try
            {
                // 第一步：创建表
                var createTableSql = $@"
                    CREATE TABLE IF NOT EXISTS chemical_documents (
                        id SERIAL PRIMARY KEY,
                        content TEXT NOT NULL,
                        embedding vector({_vectorConfig.EmbeddingDimension}),
                        regulation_type VARCHAR(50) NOT NULL,
                        priority VARCHAR(20) NOT NULL,
                        source_file VARCHAR(200),
                        chemical_type VARCHAR(100),
                        created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                    );
                ";

                using (var createTableCmd = new NpgsqlCommand(createTableSql, connection))
                {
                    await createTableCmd.ExecuteNonQueryAsync();
                }

                // 第二步：创建向量索引
                var vectorIndexSql = $@"
                    CREATE INDEX IF NOT EXISTS idx_chemical_documents_embedding_hnsw 
                    ON chemical_documents USING hnsw (embedding vector_cosine_ops)
                    WITH (m = {_vectorConfig.HnswM}, ef_construction = {_vectorConfig.HnswEfConstruction});
                ";
                using (var vectorIndexCmd = new NpgsqlCommand(vectorIndexSql, connection))
                {
                    await vectorIndexCmd.ExecuteNonQueryAsync();
                }

                // 第三步：创建业务字段索引
                var businessIndexSql = @"
                    CREATE INDEX IF NOT EXISTS idx_chemical_documents_regulation_type 
                    ON chemical_documents (regulation_type);

                    CREATE INDEX IF NOT EXISTS idx_chemical_documents_chemical_type 
                    ON chemical_documents (chemical_type);
                ";
                using (var businessIndexCmd = new NpgsqlCommand(businessIndexSql, connection))
                {
                    await businessIndexCmd.ExecuteNonQueryAsync();
                }

                Console.WriteLine("   ✅ 化工文档表创建成功");

                // K7: 扩展元数据字段（兼容已有表，逐列 ADD IF NOT EXISTS）
                var extensionColumns = new[]
                {
                    "ALTER TABLE chemical_documents ADD COLUMN IF NOT EXISTS regulation_number VARCHAR(100);",
                    "ALTER TABLE chemical_documents ADD COLUMN IF NOT EXISTS chapter_title VARCHAR(200);",
                    "ALTER TABLE chemical_documents ADD COLUMN IF NOT EXISTS clause_number VARCHAR(50);",
                    "ALTER TABLE chemical_documents ADD COLUMN IF NOT EXISTS page_number INT;",
                    "ALTER TABLE chemical_documents ADD COLUMN IF NOT EXISTS chunk_index INT;",
                    "ALTER TABLE chemical_documents ADD COLUMN IF NOT EXISTS extraction_quality VARCHAR(20);",
                };

                foreach (var alterSql in extensionColumns)
                {
                    try
                    {
                        using var alterCmd = new NpgsqlCommand(alterSql, connection);
                        await alterCmd.ExecuteNonQueryAsync();
                    }
                    catch (NpgsqlException) { /* 列可能已存在，忽略 */ }
                }
                Console.WriteLine("   ✅ 扩展元数据字段就绪");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 化工文档表创建失败: {ex.Message}");
            }
        }

        private async Task CreateKnowledgeDocumentTableAsync(NpgsqlConnection connection)
        {
            try
            {
                var createTableSql = @"
                    CREATE TABLE IF NOT EXISTS knowledge_documents (
                        id              SERIAL PRIMARY KEY,
                        source_path     VARCHAR(500) NOT NULL UNIQUE,
                        file_name       VARCHAR(300) NOT NULL,
                        file_format     VARCHAR(10)  NOT NULL,
                        file_size_bytes BIGINT,
                        regulation_type VARCHAR(50)  NOT NULL,
                        regulation_number VARCHAR(100),
                        regulation_title  VARCHAR(500),
                        priority        VARCHAR(10)  NOT NULL DEFAULT '中',
                        parent_category VARCHAR(200),
                        extraction_quality VARCHAR(20) DEFAULT 'good',
                        page_count      INT,
                        is_full_text    BOOLEAN DEFAULT TRUE,
                        total_chunks    INT DEFAULT 0,
                        content_hash    VARCHAR(64),
                        last_modified   TIMESTAMP WITH TIME ZONE,
                        created_at      TIMESTAMP WITH TIME ZONE DEFAULT NOW()
                    );
                ";

                using (var cmd = new NpgsqlCommand(createTableSql, connection))
                {
                    await cmd.ExecuteNonQueryAsync();
                }

                var indexSql = @"
                    CREATE INDEX IF NOT EXISTS idx_docs_regulation_number ON knowledge_documents(regulation_number);
                    CREATE INDEX IF NOT EXISTS idx_docs_type_priority ON knowledge_documents(regulation_type, priority);
                ";
                using (var idxCmd = new NpgsqlCommand(indexSql, connection))
                {
                    await idxCmd.ExecuteNonQueryAsync();
                }

                Console.WriteLine("   ✅ 知识库文档表创建成功");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 知识库文档表创建失败: {ex.Message}");
            }
        }

        private async Task CreateKnowledgeChunkTableAsync(NpgsqlConnection connection)
        {
            try
            {
                var createTableSql = $@"
                    CREATE TABLE IF NOT EXISTS knowledge_chunks (
                        id              SERIAL PRIMARY KEY,
                        document_id     INT NOT NULL REFERENCES knowledge_documents(id) ON DELETE CASCADE,
                        content         TEXT NOT NULL,
                        embedding       vector({_vectorConfig.EmbeddingDimension}),
                        chunk_index     INT NOT NULL DEFAULT 0,
                        chapter_number  VARCHAR(50),
                        chapter_title   VARCHAR(500),
                        clause_number   VARCHAR(50),
                        article_number  VARCHAR(50),
                        page_number     INT,
                        sub_chunk_index INT,
                        regulation_type VARCHAR(50)  NOT NULL,
                        priority        VARCHAR(10)  NOT NULL,
                        created_at      TIMESTAMP WITH TIME ZONE DEFAULT NOW()
                    );
                ";

                using (var cmd = new NpgsqlCommand(createTableSql, connection))
                {
                    await cmd.ExecuteNonQueryAsync();
                }

                var vectorIndexSql = $@"
                    CREATE INDEX IF NOT EXISTS idx_chunks_embedding ON knowledge_chunks
                        USING hnsw (embedding vector_cosine_ops)
                        WITH (m = {_vectorConfig.HnswM}, ef_construction = {_vectorConfig.HnswEfConstruction});
                ";
                using (var viCmd = new NpgsqlCommand(vectorIndexSql, connection))
                {
                    await viCmd.ExecuteNonQueryAsync();
                }

                var indexSql = @"
                    CREATE INDEX IF NOT EXISTS idx_chunks_document_id ON knowledge_chunks(document_id);
                    CREATE INDEX IF NOT EXISTS idx_chunks_regulation_type ON knowledge_chunks(regulation_type);
                    CREATE INDEX IF NOT EXISTS idx_chunks_priority ON knowledge_chunks(priority);
                ";
                using (var idxCmd = new NpgsqlCommand(indexSql, connection))
                {
                    await idxCmd.ExecuteNonQueryAsync();
                }

                Console.WriteLine("   ✅ 知识库分块表创建成功");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 知识库分块表创建失败: {ex.Message}");
            }
        }

        private string BuildConnectionString()
        {
            // Task 6: 密码加固 — 空密码不写入连接字符串，防止误连
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = _dbConfig.Host,
                Port = _dbConfig.Port,
                Database = _dbConfig.DatabaseName,
                Username = _dbConfig.Username,
                Timeout = _dbConfig.ConnectionTimeout,
                MaxPoolSize = _dbConfig.MaxPoolSize
            };
            if (!string.IsNullOrEmpty(_dbConfig.Password))
            {
                builder.Password = _dbConfig.Password;
            }
            return builder.ToString();
        }

        private NpgsqlConnection CreateConnection()
        {
            var connection = new NpgsqlConnection(BuildConnectionString());
            return connection;
        }

        private async Task CreateSessionTableAsync(NpgsqlConnection connection)
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS sessions (
                    id UUID PRIMARY KEY,
                    user_id VARCHAR(100) NOT NULL,
                    user_name VARCHAR(200),
                    session_data TEXT,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                    expires_at TIMESTAMP WITH TIME ZONE
                );
                
                CREATE INDEX IF NOT EXISTS idx_sessions_user_id ON sessions(user_id);
                CREATE INDEX IF NOT EXISTS idx_sessions_expires_at ON sessions(expires_at);
            ";
            
            using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        private async Task CreateAuditLogTableAsync(NpgsqlConnection connection)
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS audit_logs (
                    id SERIAL PRIMARY KEY,
                    user_id VARCHAR(100),
                    action VARCHAR(100) NOT NULL,
                    module VARCHAR(100),
                    detail TEXT,
                    ip_address VARCHAR(50),
                    chain_hash TEXT,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );

                -- [P3 哈希链] 为已有表补加链哈希列 (IF NOT EXISTS 安全)
                ALTER TABLE audit_logs ADD COLUMN IF NOT EXISTS chain_hash TEXT;
                
                CREATE INDEX IF NOT EXISTS idx_audit_logs_user_id ON audit_logs(user_id);
                CREATE INDEX IF NOT EXISTS idx_audit_logs_created_at ON audit_logs(created_at);
            ";
            
            using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        private async Task CreateSearchLogTableAsync(NpgsqlConnection connection)
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS search_logs (
                    id SERIAL PRIMARY KEY,
                    query TEXT NOT NULL,
                    results_count INT,
                    execution_time_ms INT,
                    source_priority VARCHAR(50),
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );
                
                CREATE INDEX IF NOT EXISTS idx_search_logs_created_at ON search_logs(created_at);
            ";
            
            using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        // 添加化工文档
        public async Task AddChemicalDocumentAsync(string content, string regulationType, string priority, string? sourceFile = null, string? chemicalType = null, float[]? embedding = null)
        {
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();

                var sql = @"
                    INSERT INTO chemical_documents (content, embedding, regulation_type, priority, source_file, chemical_type)
                    VALUES (@content, @embedding::vector, @regulationType, @priority, @sourceFile, @chemicalType);
                ";

                using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("@content", content);
                if (embedding != null)
                {
                    var vectorString = "[" + string.Join(",", embedding.Select(x => x.ToString("G", System.Globalization.CultureInfo.InvariantCulture))) + "]";
                    command.Parameters.AddWithValue("@embedding", vectorString);
                }
                else
                {
                    command.Parameters.AddWithValue("@embedding", DBNull.Value);
                }
                command.Parameters.AddWithValue("@regulationType", regulationType);
                command.Parameters.AddWithValue("@priority", priority);
                command.Parameters.AddWithValue("@sourceFile", sourceFile ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@chemicalType", chemicalType ?? (object)DBNull.Value);

                await command.ExecuteNonQueryAsync();
                Console.WriteLine($"   ✅ 化工文档添加成功 (类型: {regulationType})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ 化工文档添加失败: {ex.Message}");
            }
        }

        // P0修复：完整元数据入库方法 — 补全全部字段 + 脏数据熔断 + 向量维度校验
        public async Task AddChemicalDocumentAsync(ChemicalDocumentRecord record)
        {
            try
            {
                // ── 脏数据熔断 ──
                if (record.IsDirty)
                {
                    Console.WriteLine($"   🚫 脏数据拦截: 来源={record.SourceFile ?? "未知"}, 质量={record.ExtractionQuality ?? "未知"}, 内容长度={record.Content?.Length ?? 0}");
                    return;
                }

                // ── 向量维度校验 ──
                if (record.Embedding != null && record.Embedding.Length != _vectorConfig.EmbeddingDimension)
                {
                    Console.WriteLine($"   🚫 向量维度异常拦截: 期望{_vectorConfig.EmbeddingDimension}维, 实际{record.Embedding.Length}维, 来源={record.SourceFile ?? "未知"}");
                    record.Embedding = null; // 降级：向量设为 null，仍写入文本
                }

                using var connection = CreateConnection();
                await connection.OpenAsync();

                // P0修复：INSERT 补全全部元数据字段
                var sql = @"
                    INSERT INTO chemical_documents (
                        content, embedding, regulation_type, priority,
                        source_file, chemical_type,
                        regulation_number, chapter_title, clause_number,
                        page_number, chunk_index, extraction_quality
                    )
                    VALUES (
                        @content, @embedding::vector, @regulationType, @priority,
                        @sourceFile, @chemicalType,
                        @regulationNumber, @chapterTitle, @clauseNumber,
                        @pageNumber, @chunkIndex, @extractionQuality
                    );
                ";

                using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("@content", record.Content);
                if (record.Embedding != null)
                {
                    var vectorString = "[" + string.Join(",", record.Embedding.Select(x => x.ToString("G", System.Globalization.CultureInfo.InvariantCulture))) + "]";
                    command.Parameters.AddWithValue("@embedding", vectorString);
                }
                else
                {
                    command.Parameters.AddWithValue("@embedding", DBNull.Value);
                }
                command.Parameters.AddWithValue("@regulationType", record.RegulationType);
                command.Parameters.AddWithValue("@priority", record.Priority);
                command.Parameters.AddWithValue("@sourceFile", record.SourceFile ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@chemicalType", record.ChemicalType ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@regulationNumber", record.RegulationNumber ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@chapterTitle", record.ChapterTitle ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@clauseNumber", record.ClauseNumber ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@pageNumber", record.PageNumber ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@chunkIndex", record.ChunkIndex ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@extractionQuality", record.ExtractionQuality ?? (object)DBNull.Value);

                await command.ExecuteNonQueryAsync();
                Console.WriteLine($"   ✅ 化工文档添加成功 (类型: {record.RegulationType}, 法规号: {record.RegulationNumber ?? "无"}, 质量: {record.ExtractionQuality ?? "无"})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ 化工文档添加失败: {ex.Message}");
                throw; // P0修复：单条插入异常必须传播，否则调用方以为成功
            }
        }

        // Sprint 1: 批量入库，单连接写入多条（减少连接开销）
        public async Task AddChemicalDocumentsBatchAsync(List<ChemicalDocumentRecord> records)
        {
            if (records.Count == 0) return;

            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();

                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    // P0修复：批量 SQL 对齐单条 INSERT，补全全部 12 个元数据字段
                    var sql = @"
                        INSERT INTO chemical_documents (
                            content, embedding, regulation_type, priority,
                            source_file, chemical_type,
                            regulation_number, chapter_title, clause_number,
                            page_number, chunk_index, extraction_quality
                        )
                        VALUES (
                            @content, @embedding::vector, @regulationType, @priority,
                            @sourceFile, @chemicalType,
                            @regulationNumber, @chapterTitle, @clauseNumber,
                            @pageNumber, @chunkIndex, @extractionQuality
                        );
                    ";

                    int successCount = 0;
                    foreach (var record in records)
                    {
                        if (record.IsDirty)
                        {
                            Console.WriteLine($"   🚫 脏数据拦截(批量): 长度={record.Content?.Length ?? 0}, 质量={record.ExtractionQuality ?? "无"}");
                            continue;
                        }

                        using var command = new NpgsqlCommand(sql, connection, transaction);
                        command.Parameters.AddWithValue("@content", record.Content);
                        if (record.Embedding != null)
                        {
                            var vectorString = "[" + string.Join(",", record.Embedding.Select(x => x.ToString("G", System.Globalization.CultureInfo.InvariantCulture))) + "]";
                            command.Parameters.AddWithValue("@embedding", vectorString);
                        }
                        else
                        {
                            command.Parameters.AddWithValue("@embedding", DBNull.Value);
                        }
                        command.Parameters.AddWithValue("@regulationType", record.RegulationType);
                        command.Parameters.AddWithValue("@priority", record.Priority);
                        command.Parameters.AddWithValue("@sourceFile", record.SourceFile ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@chemicalType", record.ChemicalType ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@regulationNumber", record.RegulationNumber ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@chapterTitle", record.ChapterTitle ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@clauseNumber", record.ClauseNumber ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@pageNumber", record.PageNumber ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@chunkIndex", record.ChunkIndex ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@extractionQuality", record.ExtractionQuality ?? (object)DBNull.Value);

                        await command.ExecuteNonQueryAsync();
                        successCount++;
                    }

                    await transaction.CommitAsync();
                    Console.WriteLine($"   ✅ 批量入库完成: {successCount}/{records.Count} 条");
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw; // P0修复：异常向外传播，不静默吞掉
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ 批量入库失败: {ex.Message}");
                throw; // P0修复：批量入库异常必须传播，否则上层以为成功
            }
        }

        // ═══════════════════════════════════════════
        // 知识库双层表架构 — 文档级写入
        // ═══════════════════════════════════════════

        public async Task<int> InsertDocumentAsync(KnowledgeDocumentRecord doc)
        {
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();

                var sql = @"
                    INSERT INTO knowledge_documents (
                        source_path, file_name, file_format, file_size_bytes,
                        regulation_type, regulation_number, regulation_title, priority,
                        parent_category, extraction_quality, page_count,
                        is_full_text, total_chunks, content_hash, last_modified
                    )
                    VALUES (
                        @sourcePath, @fileName, @fileFormat, @fileSizeBytes,
                        @regulationType, @regulationNumber, @regulationTitle, @priority,
                        @parentCategory, @extractionQuality, @pageCount,
                        @isFullText, @totalChunks, @contentHash, @lastModified
                    )
                    RETURNING id;
                ";

                using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("@sourcePath", doc.SourcePath);
                command.Parameters.AddWithValue("@fileName", doc.FileName);
                command.Parameters.AddWithValue("@fileFormat", doc.FileFormat);
                command.Parameters.AddWithValue("@fileSizeBytes", doc.FileSizeBytes ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@regulationType", doc.RegulationType);
                command.Parameters.AddWithValue("@regulationNumber", doc.RegulationNumber ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@regulationTitle", doc.RegulationTitle ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@priority", doc.Priority);
                command.Parameters.AddWithValue("@parentCategory", doc.ParentCategory ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@extractionQuality", doc.ExtractionQuality ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@pageCount", doc.PageCount ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@isFullText", doc.IsFullText);
                command.Parameters.AddWithValue("@totalChunks", doc.TotalChunks);
                command.Parameters.AddWithValue("@contentHash", doc.ContentHash ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@lastModified", doc.LastModified ?? (object)DBNull.Value);

                var result = await command.ExecuteScalarAsync();
                var documentId = Convert.ToInt32(result);
                Console.WriteLine($"   ✅ 文档入库成功: {doc.FileName} (id={documentId}, 类型={doc.RegulationType})");
                return documentId;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ 文档入库失败: {doc.FileName} — {ex.Message}");
                throw;
            }
        }

        public async Task InsertChunkAsync(ChemicalDocumentRecord chunk, int documentId)
        {
            try
            {
                if (chunk.IsDirty)
                {
                    Console.WriteLine($"   🚫 脏数据拦截(分块): 来源={chunk.SourceFile ?? "未知"}, 质量={chunk.ExtractionQuality ?? "未知"}");
                    return;
                }

                if (chunk.Embedding != null && chunk.Embedding.Length != _vectorConfig.EmbeddingDimension)
                {
                    Console.WriteLine($"   🚫 向量维度异常拦截: 期望{_vectorConfig.EmbeddingDimension}维, 实际{chunk.Embedding.Length}维");
                    chunk.Embedding = null;
                }

                using var connection = CreateConnection();
                await connection.OpenAsync();

                var sql = @"
                    INSERT INTO knowledge_chunks (
                        document_id, content, embedding, chunk_index,
                        chapter_number, chapter_title, clause_number, article_number,
                        page_number, sub_chunk_index, regulation_type, priority
                    )
                    VALUES (
                        @documentId, @content, @embedding::vector, @chunkIndex,
                        @chapterNumber, @chapterTitle, @clauseNumber, @articleNumber,
                        @pageNumber, @subChunkIndex, @regulationType, @priority
                    );
                ";

                using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("@documentId", documentId);
                command.Parameters.AddWithValue("@content", chunk.Content);
                if (chunk.Embedding != null)
                {
                    var vecStr = "[" + string.Join(",", chunk.Embedding.Select(x => x.ToString("G", System.Globalization.CultureInfo.InvariantCulture))) + "]";
                    command.Parameters.AddWithValue("@embedding", vecStr);
                }
                else
                {
                    command.Parameters.AddWithValue("@embedding", DBNull.Value);
                }
                command.Parameters.AddWithValue("@chunkIndex", chunk.ChunkIndex ?? 0);
                command.Parameters.AddWithValue("@chapterNumber", chunk.ClauseNumber ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@chapterTitle", chunk.ChapterTitle ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@clauseNumber", chunk.ClauseNumber ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@articleNumber", (object)DBNull.Value);
                command.Parameters.AddWithValue("@pageNumber", chunk.PageNumber ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@subChunkIndex", (object)DBNull.Value);
                command.Parameters.AddWithValue("@regulationType", chunk.RegulationType);
                command.Parameters.AddWithValue("@priority", chunk.Priority);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ 分块入库失败: {ex.Message}");
                throw;
            }
        }

        public async Task InsertChunksBatchAsync(List<ChemicalDocumentRecord> chunks, int documentId)
        {
            if (chunks.Count == 0) return;

            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();
                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    var sql = @"
                        INSERT INTO knowledge_chunks (
                            document_id, content, embedding, chunk_index,
                            chapter_number, chapter_title, clause_number, article_number,
                            page_number, sub_chunk_index, regulation_type, priority
                        )
                        VALUES (
                            @documentId, @content, @embedding::vector, @chunkIndex,
                            @chapterNumber, @chapterTitle, @clauseNumber, @articleNumber,
                            @pageNumber, @subChunkIndex, @regulationType, @priority
                        );
                    ";

                    int successCount = 0;
                    foreach (var chunk in chunks)
                    {
                        if (chunk.IsDirty)
                        {
                            Console.WriteLine($"   🚫 脏数据拦截(批量分块): 长度={chunk.Content?.Length ?? 0}, 质量={chunk.ExtractionQuality ?? "无"}");
                            continue;
                        }

                        using var command = new NpgsqlCommand(sql, connection, transaction);
                        command.Parameters.AddWithValue("@documentId", documentId);
                        command.Parameters.AddWithValue("@content", chunk.Content);
                        if (chunk.Embedding != null)
                        {
                            var vecStr = "[" + string.Join(",", chunk.Embedding.Select(x => x.ToString("G", System.Globalization.CultureInfo.InvariantCulture))) + "]";
                            command.Parameters.AddWithValue("@embedding", vecStr);
                        }
                        else
                        {
                            command.Parameters.AddWithValue("@embedding", DBNull.Value);
                        }
                        command.Parameters.AddWithValue("@chunkIndex", chunk.ChunkIndex ?? 0);
                        command.Parameters.AddWithValue("@chapterNumber", chunk.ClauseNumber ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@chapterTitle", chunk.ChapterTitle ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@clauseNumber", chunk.ClauseNumber ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@articleNumber", (object)DBNull.Value);
                        command.Parameters.AddWithValue("@pageNumber", chunk.PageNumber ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@subChunkIndex", (object)DBNull.Value);
                        command.Parameters.AddWithValue("@regulationType", chunk.RegulationType);
                        command.Parameters.AddWithValue("@priority", chunk.Priority);

                        await command.ExecuteNonQueryAsync();
                        successCount++;
                    }

                    await transaction.CommitAsync();
                    Console.WriteLine($"   ✅ 批量分块入库完成: {successCount}/{chunks.Count} 条 (document_id={documentId})");
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ 批量分块入库失败: {ex.Message}");
                throw;
            }
        }

        public async Task UpdateDocumentChunkCountAsync(int documentId, int totalChunks)
        {
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();
                using var command = new NpgsqlCommand(
                    "UPDATE knowledge_documents SET total_chunks = @totalChunks WHERE id = @id;",
                    connection);
                command.Parameters.AddWithValue("@totalChunks", totalChunks);
                command.Parameters.AddWithValue("@id", documentId);
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 更新文档块计数失败: {ex.Message}");
            }
        }

        public async Task<int> GetKnowledgeDocumentCountAsync()
        {
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();
                using var command = new NpgsqlCommand(
                    "SELECT COUNT(*) FROM knowledge_documents;", connection);
                return Convert.ToInt32(await command.ExecuteScalarAsync());
            }
            catch { return 0; }
        }

        public async Task<int> GetKnowledgeChunkCountAsync()
        {
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();
                using var command = new NpgsqlCommand(
                    "SELECT COUNT(*) FROM knowledge_chunks;", connection);
                return Convert.ToInt32(await command.ExecuteScalarAsync());
            }
            catch { return 0; }
        }

        // 向量检索
        public async Task<List<RetrievedChunk>> VectorSearchAsync(string query, float[] queryEmbedding, int topK = 5)
        {
            var results = new List<RetrievedChunk>();
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();

                var queryVectorString = "[" + string.Join(",", queryEmbedding.Select(x => x.ToString("G", System.Globalization.CultureInfo.InvariantCulture))) + "]";

                // 双层表：优先从 knowledge_chunks 检索
                var sql = @"
                    SELECT 
                        kc.id,
                        kc.content,
                        kc.regulation_type,
                        kc.priority,
                        kd.source_path,
                        kd.file_name,
                        kc.chapter_title,
                        kc.clause_number,
                        kc.page_number,
                        kc.chunk_index,
                        kd.regulation_number,
                        kd.extraction_quality,
                        1 - (kc.embedding <=> @queryEmbedding::vector) as similarity_score
                    FROM knowledge_chunks kc
                    JOIN knowledge_documents kd ON kc.document_id = kd.id
                    ORDER BY kc.embedding <=> @queryEmbedding::vector
                    LIMIT @topK;
                ";

                using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("@queryEmbedding", queryVectorString);
                command.Parameters.AddWithValue("@topK", topK);

                using var reader = await command.ExecuteReaderAsync();
                int rank = 0;
                while (await reader.ReadAsync())
                {
                    var metadata = new Dictionary<string, object>
                    {
                        { "RegulationType", reader["regulation_type"].ToString() ?? "" },
                        { "Priority", reader["priority"].ToString() ?? "" }
                    };

                    AddMetaIfNotNull(metadata, "SourceFile", reader["file_name"]);
                    AddMetaIfNotNull(metadata, "SourcePath", reader["source_path"]);
                    AddMetaIfNotNull(metadata, "ChapterTitle", reader["chapter_title"]);
                    AddMetaIfNotNull(metadata, "ClauseNumber", reader["clause_number"]);
                    AddMetaIfNotNull(metadata, "RegulationNumber", reader["regulation_number"]);
                    AddMetaIfNotNull(metadata, "ExtractionQuality", reader["extraction_quality"]);
                    if (!(reader["page_number"] is DBNull))
                        metadata["PageNumber"] = reader.GetInt32(reader.GetOrdinal("page_number"));
                    if (!(reader["chunk_index"] is DBNull))
                        metadata["ChunkIndex"] = reader.GetInt32(reader.GetOrdinal("chunk_index"));

                    results.Add(new RetrievedChunk
                    {
                        Id = reader["id"].ToString() ?? "",
                        Content = reader["content"].ToString() ?? "",
                        Score = Convert.ToDouble(reader["similarity_score"]),
                        Rank = rank++,
                        Metadata = metadata,
                        RetrievalMethod = "Vector"
                    });
                }

                // 兜底：新表无结果则从旧表 chemical_documents 检索
                if (results.Count == 0)
                {
                    var fallbackSql = @"
                        SELECT 
                            id,
                            content,
                            regulation_type,
                            priority,
                            source_file,
                            chemical_type,
                            1 - (embedding <=> @fbEmbedding::vector) as similarity_score
                        FROM chemical_documents
                        ORDER BY embedding <=> @fbEmbedding::vector
                        LIMIT @fbTopK;
                    ";

                    using var fbCmd = new NpgsqlCommand(fallbackSql, connection);
                    fbCmd.Parameters.AddWithValue("@fbEmbedding", queryVectorString);
                    fbCmd.Parameters.AddWithValue("@fbTopK", topK);

                    using var fbReader = await fbCmd.ExecuteReaderAsync();
                    rank = 0;
                    while (await fbReader.ReadAsync())
                    {
                        var metadata = new Dictionary<string, object>
                        {
                            { "RegulationType", fbReader["regulation_type"].ToString() ?? "" },
                            { "Priority", fbReader["priority"].ToString() ?? "" }
                        };
                        AddMetaIfNotNull(metadata, "SourceFile", fbReader["source_file"]);
                        AddMetaIfNotNull(metadata, "ChemicalType", fbReader["chemical_type"]);

                        results.Add(new RetrievedChunk
                        {
                            Id = fbReader["id"].ToString() ?? "",
                            Content = fbReader["content"].ToString() ?? "",
                            Score = Convert.ToDouble(fbReader["similarity_score"]),
                            Rank = rank++,
                            Metadata = metadata,
                            RetrievalMethod = "Vector"
                        });
                    }
                }

                if (!EvalMode.IsActive)
                    Console.WriteLine($"   ✅ 向量检索完成 (找到 {results.Count} 条结果)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ 向量检索失败: {ex.Message}");
            }

            return results;
        }

        private static void AddMetaIfNotNull(Dictionary<string, object> meta, string key, object value)
        {
            if (value is not DBNull && value != null && !string.IsNullOrEmpty(value.ToString()))
                meta[key] = value.ToString()!;
        }

        // 清空化工文档表（与 BM25 Clear 同步，避免双通道数据不一致）
        public async Task ClearChemicalDocumentsAsync()
        {
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();

                using var command = new NpgsqlCommand(
                    "DELETE FROM chemical_documents;",
                    connection);

                int deleted = await command.ExecuteNonQueryAsync();
                Console.WriteLine($"   🧹 向量库已清空 (删除 {deleted} 条)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 清空向量库失败: {ex.Message}");
            }
        }

        // ═══ 启动加速：跳过重复嵌入生成 ═══

        public async Task<int> GetChemicalDocumentCountAsync()
        {
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();
                using var command = new NpgsqlCommand(
                    "SELECT COUNT(*) FROM chemical_documents;", connection);
                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            catch
            {
                return 0;
            }
        }

        public async Task<List<ChemicalDocumentRecord>> GetAllChemicalDocumentTextsAsync()
        {
            var results = new List<ChemicalDocumentRecord>();
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();

                // 双层表 JOIN：优先从 knowledge_chunks + knowledge_documents 读取完整元数据
                var joinSql = @"
                    SELECT kc.content, kc.regulation_type, kc.priority,
                           kc.chapter_title, kc.clause_number, kc.chunk_index,
                           kc.page_number,
                           kd.regulation_number, kd.file_name,
                           kd.extraction_quality
                    FROM knowledge_chunks kc
                    JOIN knowledge_documents kd ON kc.document_id = kd.id
                    ORDER BY kc.id;
                ";

                using var command = new NpgsqlCommand(joinSql, connection);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(new ChemicalDocumentRecord
                    {
                        Content = reader.IsDBNull(0) ? "" : reader.GetString(0),
                        RegulationType = reader.IsDBNull(1) ? "通用" : reader.GetString(1),
                        Priority = reader.IsDBNull(2) ? "中" : reader.GetString(2),
                        ChapterTitle = reader.IsDBNull(3) ? null : reader.GetString(3),
                        ClauseNumber = reader.IsDBNull(4) ? null : reader.GetString(4),
                        ChunkIndex = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                        PageNumber = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                        RegulationNumber = reader.IsDBNull(7) ? null : reader.GetString(7),
                        SourceFile = reader.IsDBNull(8) ? null : reader.GetString(8),
                        ExtractionQuality = reader.IsDBNull(9) ? null : reader.GetString(9),
                    });
                }

                // 兜底：新表无数据则从旧表 chemical_documents 读取（兼容迁移前数据）
                if (results.Count == 0)
                {
                    var fallbackSql = @"
                        SELECT content, regulation_type, priority, source_file,
                               regulation_number, chapter_title, clause_number, page_number,
                               chunk_index, extraction_quality
                        FROM chemical_documents
                        ORDER BY id;
                    ";
                    using var fbCmd = new NpgsqlCommand(fallbackSql, connection);
                    using var fbReader = await fbCmd.ExecuteReaderAsync();
                    while (await fbReader.ReadAsync())
                    {
                        results.Add(new ChemicalDocumentRecord
                        {
                            Content = fbReader.IsDBNull(0) ? "" : fbReader.GetString(0),
                            RegulationType = fbReader.IsDBNull(1) ? "通用" : fbReader.GetString(1),
                            Priority = fbReader.IsDBNull(2) ? "中" : fbReader.GetString(2),
                            SourceFile = fbReader.IsDBNull(3) ? null : fbReader.GetString(3),
                            RegulationNumber = fbReader.IsDBNull(4) ? null : fbReader.GetString(4),
                            ChapterTitle = fbReader.IsDBNull(5) ? null : fbReader.GetString(5),
                            ClauseNumber = fbReader.IsDBNull(6) ? null : fbReader.GetString(6),
                            PageNumber = fbReader.IsDBNull(7) ? null : fbReader.GetInt32(7),
                            ChunkIndex = fbReader.IsDBNull(8) ? null : fbReader.GetInt32(8),
                            ExtractionQuality = fbReader.IsDBNull(9) ? null : fbReader.GetString(9),
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 从数据库加载文档失败: {ex.Message}");
            }
            return results;
        }

        // Sprint 2: 加载全量文档及向量嵌入（GPU 索引重建 / 内存检索用）
        public async Task<List<ChemicalDocumentRecord>> GetAllChemicalDocumentsWithEmbeddingsAsync()
        {
            var results = new List<ChemicalDocumentRecord>();
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();

                // 双层表：优先从 knowledge_chunks JOIN knowledge_documents 加载
                var joinSql = @"
                    SELECT kc.id, kc.content, kc.embedding::TEXT,
                           kc.regulation_type, kc.priority,
                           kd.file_name,
                           kc.chapter_title, kc.clause_number,
                           kc.page_number, kc.chunk_index,
                           kd.regulation_number, kd.extraction_quality
                    FROM knowledge_chunks kc
                    JOIN knowledge_documents kd ON kc.document_id = kd.id
                    ORDER BY kc.id;
                ";

                using var command = new NpgsqlCommand(joinSql, connection);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var record = new ChemicalDocumentRecord
                    {
                        Id = reader.GetInt32(0),
                        Content = reader.GetString(1),
                        RegulationType = reader.IsDBNull(3) ? "通用" : reader.GetString(3),
                        Priority = reader.IsDBNull(4) ? "中" : reader.GetString(4),
                        SourceFile = reader.IsDBNull(5) ? null : reader.GetString(5),
                        ChemicalType = null, // knowledge_chunks 无此字段
                        ChapterTitle = reader.IsDBNull(6) ? null : reader.GetString(6),
                        ClauseNumber = reader.IsDBNull(7) ? null : reader.GetString(7),
                        PageNumber = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                        ChunkIndex = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                        RegulationNumber = reader.IsDBNull(10) ? null : reader.GetString(10),
                        ExtractionQuality = reader.IsDBNull(11) ? null : reader.GetString(11)
                    };

                    if (!reader.IsDBNull(2))
                    {
                        var vectorStr = reader.GetValue(2).ToString() ?? "";
                        record.Embedding = ParsePgVectorString(vectorStr);
                    }

                    results.Add(record);
                }

                // 兜底：新表无数据则从旧表 chemical_documents 加载
                if (results.Count == 0)
                {
                    var fallbackSql = @"
                        SELECT id, content, embedding::TEXT, regulation_type, priority, source_file, chemical_type,
                               regulation_number, chapter_title, clause_number, page_number, chunk_index, extraction_quality
                        FROM chemical_documents
                        ORDER BY id;
                    ";
                    using var fbCmd = new NpgsqlCommand(fallbackSql, connection);
                    using var fbReader = await fbCmd.ExecuteReaderAsync();
                    while (await fbReader.ReadAsync())
                    {
                        var record = new ChemicalDocumentRecord
                        {
                            Id = fbReader.GetInt32(0),
                            Content = fbReader.GetString(1),
                            RegulationType = fbReader.IsDBNull(3) ? "通用" : fbReader.GetString(3),
                            Priority = fbReader.IsDBNull(4) ? "中" : fbReader.GetString(4),
                            SourceFile = fbReader.IsDBNull(5) ? null : fbReader.GetString(5),
                            ChemicalType = fbReader.IsDBNull(6) ? null : fbReader.GetString(6),
                            RegulationNumber = fbReader.IsDBNull(7) ? null : fbReader.GetString(7),
                            ChapterTitle = fbReader.IsDBNull(8) ? null : fbReader.GetString(8),
                            ClauseNumber = fbReader.IsDBNull(9) ? null : fbReader.GetString(9),
                            PageNumber = fbReader.IsDBNull(10) ? null : fbReader.GetInt32(10),
                            ChunkIndex = fbReader.IsDBNull(11) ? null : fbReader.GetInt32(11),
                            ExtractionQuality = fbReader.IsDBNull(12) ? null : fbReader.GetString(12)
                        };

                        if (!fbReader.IsDBNull(2))
                        {
                            var vectorStr = fbReader.GetValue(2).ToString() ?? "";
                            record.Embedding = ParsePgVectorString(vectorStr);
                        }

                        results.Add(record);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 加载全量向量文档失败: {ex.Message}");
            }
            return results;
        }

        /// <summary>[P3 增量更新] 删除指定源文件的全部文档分块</summary>
        public async Task<int> DeleteChemicalDocumentsBySourceAsync(string sourceFile)
        {
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();
                var sql = "DELETE FROM chemical_documents WHERE source_file = @sourceFile;";
                using var cmd = new NpgsqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@sourceFile", sourceFile);
                return await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 删除文档分块失败 [{sourceFile}]: {ex.Message}");
                return 0;
            }
        }

        // 解析 pgvector 字符串格式 "[0.1,0.2,0.3]" 为 float[]
        private static float[]? ParsePgVectorString(string vectorStr)
        {
            if (string.IsNullOrWhiteSpace(vectorStr))
                return null;

            var trimmed = vectorStr.Trim('[', ']', ' ');
            if (string.IsNullOrWhiteSpace(trimmed))
                return null;

            try
            {
                return trimmed.Split(',')
                    .Select(s => float.Parse(s.Trim(), System.Globalization.CultureInfo.InvariantCulture))
                    .ToArray();
            }
            catch
            {
                return null;
            }
        }

        // ═══════════════════════════════════════════
        // 审计日志持久化（生产安全加固 — 替代内存 List）
        // ═══════════════════════════════════════════

        public async Task AddAuditLogAsync(string userId, string operation, string details, string? ipAddress = null, string? chainHash = null, DateTime? createTime = null)
        {
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();

                // [P3 哈希链] 显式写入 created_at，使入库时间与参与哈希计算的时间一致（避免 DB 默认 CURRENT_TIMESTAMP 与 C# 时钟不一致）
                var createdAtUtc = (createTime ?? DateTime.UtcNow).ToUniversalTime();

                var sql = @"
                    INSERT INTO audit_logs (user_id, action, detail, ip_address, chain_hash, created_at)
                    VALUES (@userId, @action, @detail, @ipAddress, @chainHash, @createdAt);
                ";

                using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("@userId", userId ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@action", operation);
                command.Parameters.AddWithValue("@detail", details ?? "");
                command.Parameters.AddWithValue("@ipAddress", ipAddress ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@chainHash", chainHash ?? (object)DBNull.Value);
                command.Parameters.Add(new NpgsqlParameter("@createdAt", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = createdAtUtc });

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 审计日志写入失败: {ex.Message}");
            }
        }

        // [P3 哈希链] 取最后一条 chain_hash（按 id 倒序），供 AuditService 重启后恢复链头
        public async Task<string?> GetLastAuditChainHashAsync()
        {
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();

                const string sql = "SELECT chain_hash FROM audit_logs WHERE chain_hash IS NOT NULL ORDER BY id DESC LIMIT 1;";
                using var command = new NpgsqlCommand(sql, connection);
                var result = await command.ExecuteScalarAsync();
                return result is DBNull or null ? null : result.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 审计链头恢复查询失败: {ex.Message}");
                return null;
            }
        }

        public async Task<List<AuditLog>> GetAuditLogsAsync(DateTime? startTime, DateTime? endTime, string? userId = null)
        {
            var results = new List<AuditLog>();
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();

                var sql = "SELECT id, user_id, action, detail, ip_address, COALESCE(chain_hash, '') as chain_hash, created_at FROM audit_logs WHERE 1=1";

                var parameters = new List<NpgsqlParameter>();
                if (startTime.HasValue)
                {
                    sql += " AND created_at >= @startTime";
                    parameters.Add(new NpgsqlParameter("@startTime", startTime.Value));
                }
                if (endTime.HasValue)
                {
                    sql += " AND created_at <= @endTime";
                    parameters.Add(new NpgsqlParameter("@endTime", endTime.Value));
                }
                if (!string.IsNullOrEmpty(userId))
                {
                    sql += " AND user_id = @userId";
                    parameters.Add(new NpgsqlParameter("@userId", userId));
                }
                sql += " ORDER BY created_at DESC LIMIT 1000;";

                using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddRange(parameters.ToArray());

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(new AuditLog
                    {
                        Id = Convert.ToInt64(reader["id"]),
                        UserId = reader["user_id"]?.ToString() ?? "",
                        Operation = reader["action"]?.ToString() ?? "",
                        Details = reader["detail"]?.ToString() ?? "",
                        CreateTime = reader["created_at"] is DBNull ? DateTime.MinValue : Convert.ToDateTime(reader["created_at"]),
                        ChainHash = reader["chain_hash"]?.ToString()
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 审计日志查询失败: {ex.Message}");
            }
            return results;
        }

        // ═══════════════════════════════════════════
        // Phase 2.1: 长期记忆存储 (pgvector)
        // ═══════════════════════════════════════════

        private async Task CreateLongTermMemoryTableAsync(NpgsqlConnection connection)
        {
            try
            {
                var sql = $@"
                    CREATE TABLE IF NOT EXISTS long_term_memories (
                        id UUID PRIMARY KEY,
                        user_id VARCHAR(100) NOT NULL,
                        memory_type VARCHAR(50) NOT NULL,
                        content TEXT NOT NULL,
                        embedding vector({_vectorConfig.EmbeddingDimension}),
                        source_session_id UUID,
                        source_turn_index INT DEFAULT 0,
                        importance FLOAT DEFAULT 0.5,
                        hit_count INT DEFAULT 0,
                        last_hit_at TIMESTAMPTZ,
                        is_active BOOLEAN DEFAULT true,
                        created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
                        updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
                    );

                    CREATE INDEX IF NOT EXISTS idx_ltm_user_id ON long_term_memories(user_id);
                    CREATE INDEX IF NOT EXISTS idx_ltm_memory_type ON long_term_memories(memory_type);
                    CREATE INDEX IF NOT EXISTS idx_ltm_is_active ON long_term_memories(is_active);
                    CREATE INDEX IF NOT EXISTS idx_ltm_user_active ON long_term_memories(user_id, is_active);
                ";

                using var command = new NpgsqlCommand(sql, connection);
                await command.ExecuteNonQueryAsync();

                // 尝试创建向量索引（可能因权限失败，非致命）
                try
                {
                    var vectorIndexSql = $@"
                        CREATE INDEX IF NOT EXISTS idx_ltm_embedding_hnsw
                        ON long_term_memories USING hnsw (embedding vector_cosine_ops)
                        WITH (m = 16, ef_construction = 64);
                    ";
                    using var idxCmd = new NpgsqlCommand(vectorIndexSql, connection);
                    await idxCmd.ExecuteNonQueryAsync();
                }
                catch { /* HNSW 索引非必需 */ }

                Console.WriteLine("   ✅ 长期记忆表创建成功");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 长期记忆表创建失败: {ex.Message}");
            }
        }

        // ═══ Task 2.2: Refresh Token 表 ═══

        private async Task CreateRefreshTokenTableAsync(NpgsqlConnection connection)
        {
            try
            {
                var sql = @"
                    CREATE TABLE IF NOT EXISTS refresh_tokens (
                        token_hash VARCHAR(128) PRIMARY KEY,
                        username VARCHAR(100) NOT NULL,
                        expires_at TIMESTAMPTZ NOT NULL,
                        created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
                    );

                    CREATE INDEX IF NOT EXISTS idx_refresh_tokens_expires ON refresh_tokens(expires_at);
                    CREATE INDEX IF NOT EXISTS idx_refresh_tokens_username ON refresh_tokens(username);
                ";

                using var command = new NpgsqlCommand(sql, connection);
                await command.ExecuteNonQueryAsync();
                Console.WriteLine("   ✅ Refresh Token 表创建成功");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ Refresh Token 表创建失败: {ex.Message}");
            }
        }

        public async Task StoreRefreshTokenAsync(string tokenHash, string username, DateTime expiresAt)
        {
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();

                var sql = @"
                    INSERT INTO refresh_tokens (token_hash, username, expires_at)
                    VALUES (@tokenHash, @username, @expiresAt)
                    ON CONFLICT (token_hash) DO NOTHING;
                ";

                using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("@tokenHash", tokenHash);
                command.Parameters.AddWithValue("@username", username);
                command.Parameters.AddWithValue("@expiresAt", expiresAt);
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ Refresh Token 存储失败: {ex.Message}");
            }
        }

        public async Task<string?> ValidateAndRemoveRefreshTokenAsync(string tokenHash)
        {
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();

                // 原子操作：查找未过期的 token，删除并返回 username
                var sql = @"
                    DELETE FROM refresh_tokens
                    WHERE token_hash = @tokenHash AND expires_at > CURRENT_TIMESTAMP
                    RETURNING username;
                ";

                using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("@tokenHash", tokenHash);
                var result = await command.ExecuteScalarAsync();
                return result?.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ Refresh Token 验证失败: {ex.Message}");
                return null;
            }
        }

        public async Task AddLongTermMemoryAsync(LongTermMemoryRecord record)
        {
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();

                var sql = @"
                    INSERT INTO long_term_memories (id, user_id, memory_type, content, embedding,
                        source_session_id, source_turn_index, importance, hit_count, last_hit_at, is_active, created_at, updated_at)
                    VALUES (@id, @userId, @memoryType, @content, @embedding::vector,
                        @sourceSessionId, @sourceTurnIndex, @importance, @hitCount, @lastHitAt, @isActive, @createdAt, @updatedAt)
                    ON CONFLICT (id) DO UPDATE SET
                        content = EXCLUDED.content,
                        embedding = EXCLUDED.embedding,
                        importance = EXCLUDED.importance,
                        hit_count = EXCLUDED.hit_count,
                        last_hit_at = EXCLUDED.last_hit_at,
                        is_active = EXCLUDED.is_active,
                        updated_at = EXCLUDED.updated_at;
                ";

                using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("@id", record.Id);
                command.Parameters.AddWithValue("@userId", record.UserId);
                command.Parameters.AddWithValue("@memoryType", record.MemoryType);
                command.Parameters.AddWithValue("@content", record.Content);
                if (record.Embedding != null)
                {
                    var vectorString = "[" + string.Join(",", record.Embedding.Select(x => x.ToString("G", System.Globalization.CultureInfo.InvariantCulture))) + "]";
                    command.Parameters.AddWithValue("@embedding", vectorString);
                }
                else
                {
                    command.Parameters.AddWithValue("@embedding", DBNull.Value);
                }
                command.Parameters.AddWithValue("@sourceSessionId", record.SourceSessionId ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@sourceTurnIndex", record.SourceTurnIndex);
                command.Parameters.AddWithValue("@importance", record.Importance);
                command.Parameters.AddWithValue("@hitCount", record.HitCount);
                command.Parameters.AddWithValue("@lastHitAt", record.LastHitAt ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@isActive", record.IsActive);
                command.Parameters.AddWithValue("@createdAt", record.CreatedAt);
                command.Parameters.AddWithValue("@updatedAt", record.UpdatedAt);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 长期记忆写入失败: {ex.Message}");
            }
        }

        public async Task<List<LongTermMemoryRecord>> SearchLongTermMemoriesAsync(string userId, float[] queryEmbedding, int topK = 5, string? memoryTypeFilter = null)
        {
            var results = new List<LongTermMemoryRecord>();
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();

                var sql = @"
                    SELECT id, user_id, memory_type, content,
                           source_session_id, source_turn_index,
                           importance, hit_count, last_hit_at, is_active, created_at, updated_at,
                           1 - (embedding <=> @queryEmbedding::vector) as similarity_score
                    FROM long_term_memories
                    WHERE user_id = @userId AND is_active = true
                ";

                if (!string.IsNullOrWhiteSpace(memoryTypeFilter))
                {
                    sql += " AND memory_type = @memoryType";
                }

                sql += " ORDER BY embedding <=> @queryEmbedding::vector LIMIT @topK;";

                using var command = new NpgsqlCommand(sql, connection);
                var queryVectorString = "[" + string.Join(",", queryEmbedding.Select(x => x.ToString("G", System.Globalization.CultureInfo.InvariantCulture))) + "]";
                command.Parameters.AddWithValue("@queryEmbedding", queryVectorString);
                command.Parameters.AddWithValue("@userId", userId);
                command.Parameters.AddWithValue("@topK", topK);
                if (!string.IsNullOrWhiteSpace(memoryTypeFilter))
                    command.Parameters.AddWithValue("@memoryType", memoryTypeFilter);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(MapToMemoryRecord(reader));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 长期记忆检索失败: {ex.Message}");
            }
            return results;
        }

        public async Task<List<LongTermMemoryRecord>> SearchLongTermMemoriesByKeywordAsync(string userId, string keyword, int topK = 10)
        {
            var results = new List<LongTermMemoryRecord>();
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();

                var sql = @"
                    SELECT id, user_id, memory_type, content,
                           source_session_id, source_turn_index,
                           importance, hit_count, last_hit_at, is_active, created_at, updated_at
                    FROM long_term_memories
                    WHERE user_id = @userId AND is_active = true AND content ILIKE @keyword
                    ORDER BY importance DESC, hit_count DESC
                    LIMIT @topK;
                ";

                using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("@userId", userId);
                command.Parameters.AddWithValue("@keyword", $"%{keyword}%");
                command.Parameters.AddWithValue("@topK", topK);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(MapToMemoryRecord(reader));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 长期记忆关键词搜索失败: {ex.Message}");
            }
            return results;
        }

        public async Task UpdateMemoryHitAsync(Guid memoryId)
        {
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();

                var sql = @"
                    UPDATE long_term_memories
                    SET hit_count = hit_count + 1,
                        last_hit_at = CURRENT_TIMESTAMP,
                        importance = LEAST(1.0, importance + 0.02),
                        updated_at = CURRENT_TIMESTAMP
                    WHERE id = @id;
                ";
                using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("@id", memoryId);
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 记忆命中更新失败: {ex.Message}");
            }
        }

        public async Task DeactivateMemoryAsync(Guid memoryId)
        {
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();

                var sql = @"
                    UPDATE long_term_memories
                    SET is_active = false, updated_at = CURRENT_TIMESTAMP
                    WHERE id = @id;
                ";
                using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("@id", memoryId);
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 记忆停用失败: {ex.Message}");
            }
        }

        public async Task DeactivateConflictingMemoriesAsync(string userId, string memoryType, string content)
        {
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();

                // 简单冲突检测：同一用户+同一类型+内容相似（超过50%重叠）即为冲突
                var sql = @"
                    UPDATE long_term_memories
                    SET is_active = false, updated_at = CURRENT_TIMESTAMP
                    WHERE user_id = @userId
                      AND memory_type = @memoryType
                      AND is_active = true
                      AND id != (
                          SELECT id FROM long_term_memories
                          WHERE user_id = @userId AND memory_type = @memoryType AND content = @content AND is_active = true
                          ORDER BY created_at DESC LIMIT 1
                      );
                ";
                using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("@userId", userId);
                command.Parameters.AddWithValue("@memoryType", memoryType);
                command.Parameters.AddWithValue("@content", content);
                var affected = await command.ExecuteNonQueryAsync();
                if (affected > 0)
                    Console.WriteLine($"   🧹 冲突解决: 停用 {affected} 条旧记忆");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 记忆冲突解决失败: {ex.Message}");
            }
        }

        public async Task<int> CleanupMemoriesAsync(int retentionDays)
        {
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();

                // 物理删除：is_active=false 且超过 retentionDays
                var sql = @"
                    DELETE FROM long_term_memories
                    WHERE is_active = false
                      AND updated_at < CURRENT_TIMESTAMP - INTERVAL '1 day' * @retentionDays;
                ";
                using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("@retentionDays", retentionDays);
                var deleted = await command.ExecuteNonQueryAsync();
                if (deleted > 0)
                    Console.WriteLine($"   🧹 记忆清理: 删除 {deleted} 条过期记录");
                return deleted;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 记忆清理失败: {ex.Message}");
                return 0;
            }
        }

        public async Task<LongTermMemoryStats> GetLongTermMemoryStatsAsync(string userId)
        {
            var stats = new LongTermMemoryStats();
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();

                var sql = @"
                    SELECT
                        COUNT(*) as total,
                        COUNT(*) FILTER (WHERE is_active = true) as active,
                        COUNT(*) FILTER (WHERE memory_type = 'regulation_ref') as regulation,
                        COUNT(*) FILTER (WHERE memory_type = 'chemical_fact') as chemical,
                        COUNT(*) FILTER (WHERE memory_type = 'compliance_experience') as compliance,
                        COUNT(*) FILTER (WHERE memory_type = 'user_preference') as preference,
                        COALESCE(SUM(hit_count), 0) as total_hits
                    FROM long_term_memories
                    WHERE user_id = @userId;
                ";
                using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("@userId", userId);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    stats.TotalCount = Convert.ToInt32(reader["total"]);
                    stats.ActiveCount = Convert.ToInt32(reader["active"]);
                    stats.RegulationRefCount = Convert.ToInt32(reader["regulation"]);
                    stats.ChemicalFactCount = Convert.ToInt32(reader["chemical"]);
                    stats.ComplianceExperienceCount = Convert.ToInt32(reader["compliance"]);
                    stats.UserPreferenceCount = Convert.ToInt32(reader["preference"]);
                    stats.TotalHitCount = Convert.ToInt32(reader["total_hits"]);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 记忆统计获取失败: {ex.Message}");
            }
            return stats;
        }

        public async Task<long> GetLongTermMemoryCountAsync(string userId)
        {
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();
                var sql = "SELECT COUNT(*) FROM long_term_memories WHERE user_id = @userId AND is_active = true;";
                using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("@userId", userId);
                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt64(result);
            }
            catch
            {
                return 0;
            }
        }

        private static LongTermMemoryRecord MapToMemoryRecord(NpgsqlDataReader reader)
        {
            return new LongTermMemoryRecord
            {
                Id = reader["id"] is Guid g ? g : Guid.Parse(reader["id"].ToString()!),
                UserId = reader["user_id"]?.ToString() ?? "",
                MemoryType = reader["memory_type"]?.ToString() ?? "",
                Content = reader["content"]?.ToString() ?? "",
                SourceSessionId = reader["source_session_id"] is DBNull ? null : (Guid)reader["source_session_id"],
                SourceTurnIndex = Convert.ToInt32(reader["source_turn_index"]),
                Importance = Convert.ToSingle(reader["importance"]),
                HitCount = Convert.ToInt32(reader["hit_count"]),
                LastHitAt = reader["last_hit_at"] is DBNull ? null : Convert.ToDateTime(reader["last_hit_at"]),
                IsActive = Convert.ToBoolean(reader["is_active"]),
                CreatedAt = Convert.ToDateTime(reader["created_at"]),
                UpdatedAt = Convert.ToDateTime(reader["updated_at"])
            };
        }
    }
}