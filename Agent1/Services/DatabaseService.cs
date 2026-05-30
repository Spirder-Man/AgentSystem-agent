using Agent1.Config;
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
            catch
            {
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

                // 第二步：尝试创建中文全文搜索索引，如果失败则使用simple配置
                try
                {
                    var chineseIndexSql = @"
                        CREATE INDEX IF NOT EXISTS idx_chemical_documents_content_gin 
                        ON chemical_documents USING gin (to_tsvector('chinese', content));
                    ";
                    using (var chineseIndexCmd = new NpgsqlCommand(chineseIndexSql, connection))
                    {
                        await chineseIndexCmd.ExecuteNonQueryAsync();
                    }
                    Console.WriteLine("   ✅ 中文全文搜索索引创建成功");
                }
                catch
                {
                    Console.WriteLine("   ⚠️ 中文配置不可用，使用simple配置");
                    var simpleIndexSql = @"
                        CREATE INDEX IF NOT EXISTS idx_chemical_documents_content_gin 
                        ON chemical_documents USING gin (to_tsvector('simple', content));
                    ";
                    using (var simpleIndexCmd = new NpgsqlCommand(simpleIndexSql, connection))
                    {
                        await simpleIndexCmd.ExecuteNonQueryAsync();
                    }
                    Console.WriteLine("   ✅ simple全文搜索索引创建成功");
                }

                // 第三步：创建向量索引
                var vectorIndexSql = $@"
                    CREATE INDEX IF NOT EXISTS idx_chemical_documents_embedding_hnsw 
                    ON chemical_documents USING hnsw (embedding vector_cosine_ops)
                    WITH (m = {_vectorConfig.HnswM}, ef_construction = {_vectorConfig.HnswEfConstruction});
                ";
                using (var vectorIndexCmd = new NpgsqlCommand(vectorIndexSql, connection))
                {
                    await vectorIndexCmd.ExecuteNonQueryAsync();
                }

                // 第四步：创建业务字段索引
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

        private string BuildConnectionString()
        {
            return new NpgsqlConnectionStringBuilder
            {
                Host = _dbConfig.Host,
                Port = _dbConfig.Port,
                Database = _dbConfig.DatabaseName,
                Username = _dbConfig.Username,
                Password = _dbConfig.Password,
                Timeout = _dbConfig.ConnectionTimeout,
                MaxPoolSize = _dbConfig.MaxPoolSize
            }.ToString();
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
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );
                
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
            }
        }

        // 向量检索
        public async Task<List<RetrievedChunk>> VectorSearchAsync(string query, float[] queryEmbedding, int topK = 5)
        {
            var results = new List<RetrievedChunk>();
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();

                var sql = @"
                    SELECT 
                        id,
                        content,
                        regulation_type,
                        priority,
                        source_file,
                        chemical_type,
                        1 - (embedding <=> @queryEmbedding::vector) as similarity_score
                    FROM chemical_documents
                    ORDER BY embedding <=> @queryEmbedding::vector
                    LIMIT @topK;
                ";

                using var command = new NpgsqlCommand(sql, connection);
                var queryVectorString = "[" + string.Join(",", queryEmbedding.Select(x => x.ToString("G", System.Globalization.CultureInfo.InvariantCulture))) + "]";
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

                    if (!string.IsNullOrEmpty(reader["source_file"].ToString()))
                    {
                        metadata["SourceFile"] = reader["source_file"].ToString() ?? "";
                    }
                    if (!string.IsNullOrEmpty(reader["chemical_type"].ToString()))
                    {
                        metadata["ChemicalType"] = reader["chemical_type"].ToString() ?? "";
                    }

                    results.Add(new RetrievedChunk
                    {
                        Id = reader["id"].ToString(),
                        Content = reader["content"].ToString() ?? "",
                        Score = Convert.ToDouble(reader["similarity_score"]),
                        Rank = rank++,
                        Metadata = metadata,
                        RetrievalMethod = "Vector"
                    });
                }

                Console.WriteLine($"   ✅ 向量检索完成 (找到 {results.Count} 条结果)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ 向量检索失败: {ex.Message}");
            }

            return results;
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
    }
}