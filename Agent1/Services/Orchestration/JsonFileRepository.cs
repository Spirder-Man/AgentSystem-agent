using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Agent1.Services.Orchestration
{
    /// <summary>
    /// JSON文件仓储基类 — P0 持久化层。
    /// 
    /// 提供原子化的读写操作，保证数据在进程重启后不丢失。
    /// 文件锁保护并发读写安全（适配 CLI 单进程多线程场景）。
    /// 生产环境可替换为 PostgreSQL 仓储实现，接口不变。
    /// </summary>
    public abstract class JsonFileRepository<T> where T : class, new()
    {
        private readonly string _filePath;
        private readonly ReaderWriterLockSlim _rwLock = new();
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        protected JsonFileRepository(string fileName)
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "Data");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, fileName);
        }

        /// <summary>从文件加载数据，文件不存在则返回默认值</summary>
        protected T Load()
        {
            _rwLock.EnterReadLock();
            try
            {
                if (!File.Exists(_filePath))
                    return new T();

                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning("[JsonFileRepo] 加载失败 {Path}: {Error}", _filePath, ex.Message);
                return new T();
            }
            finally { _rwLock.ExitReadLock(); }
        }

        /// <summary>保存数据到文件（原子写入：先写临时文件，再替换）</summary>
        public void SaveData(T data)
        {
            _rwLock.EnterWriteLock();
            try
            {
                var json = JsonSerializer.Serialize(data, JsonOptions);
                var tempPath = _filePath + ".tmp";

                // 原子写入：先写临时文件，成功后再替换目标文件
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _filePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error("[JsonFileRepo] 保存失败 {Path}: {Error}", _filePath, ex.Message);
            }
            finally { _rwLock.ExitWriteLock(); }
        }

        /// <summary>检查数据文件是否存在（首次启动判断）</summary>
        protected bool Exists()
        {
            _rwLock.EnterReadLock();
            try { return File.Exists(_filePath); }
            finally { _rwLock.ExitReadLock(); }
        }
    }
}
