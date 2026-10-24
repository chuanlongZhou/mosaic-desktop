using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Mosaic.Core.Models;

namespace Mosaic.Storage.Json;

public sealed class JsonConflictException(string message) : IOException(message);
public sealed record RecoveryNotice(string RelativePath, string Message);

/// <summary>One bounded, serialized writer; same-directory replacement; no database or third-party serializer.</summary>
public sealed class JsonDocumentStore(string root)
{
    private const int MaxBytes = 1024 * 1024;
    private readonly string _root = Path.GetFullPath(root);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, string?> _fingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _revisions = new(StringComparer.OrdinalIgnoreCase);
    private int _pending;
    public string Root => _root;
    public List<RecoveryNotice> Notices { get; } = [];

    public async Task<T> LoadAsync<T>(string relativePath, JsonTypeInfo<T> type, Func<T> create, CancellationToken token = default)
        where T : VersionedDocument
    {
        var path = Resolve(relativePath);
        await _gate.WaitAsync(token);
        try
        {
            var primary = File.Exists(path);
            if (!primary && !File.Exists(path + ".bak"))
            {
                _fingerprints[path] = null;
                var fresh = create();
                fresh.Validate();
                _revisions[path] = fresh.Revision;
                return fresh;
            }
            try
            {
                if (!primary) throw new InvalidDataException("主文件缺失。");
                var (value, hash) = await ReadAsync(path, type, token);
                _fingerprints[path] = hash;
                _revisions[path] = value.Revision;
                return value;
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
            {
                T recovered;
                try { (recovered, _) = await ReadAsync(path + ".bak", type, token); }
                catch (Exception backupEx) when (backupEx is JsonException or InvalidDataException or IOException)
                {
                    throw new InvalidDataException($"无法读取 {relativePath}，备份也不可用。原文件已保留。", new AggregateException(ex, backupEx));
                }
                if (primary)
                    File.Copy(path, path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"), false);
                // Restore from a validated backup without replacing that good backup with corrupt data.
                await AtomicWriteAsync(path, await File.ReadAllBytesAsync(path + ".bak", token), false, token);
                _fingerprints[path] = await HashFileAsync(path, token);
                _revisions[path] = recovered.Revision;
                Notices.Add(new(relativePath, "主配置异常，已从上一版备份恢复；异常副本已保留。"));
                return recovered;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync<T>(string relativePath, T value, JsonTypeInfo<T> type, CancellationToken token = default)
        where T : VersionedDocument
    {
        value.Validate();
        var expectedRevision = value.Revision;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, type); // Snapshot before joining the writer queue.
        if (bytes.Length > MaxBytes) throw new InvalidDataException("单个 JSON 文档超过 1 MiB，未保存。");
        if (Interlocked.Increment(ref _pending) > 32)
        {
            Interlocked.Decrement(ref _pending);
            throw new IOException("保存队列已满，请稍后重试。");
        }
        var entered = false;
        try
        {
            await _gate.WaitAsync(token);
            entered = true;
            var path = Resolve(relativePath);
            if (!_fingerprints.TryGetValue(path, out var expected))
                throw new InvalidOperationException("保存前必须先读取配置。");
            if (_revisions[path] != expectedRevision)
                throw new JsonConflictException("配置保存版本已变化，请重新读取最新配置后重试。");
            var actual = File.Exists(path) ? await HashFileAsync(path, token) : null;
            if (!StringComparer.Ordinal.Equals(actual, expected))
                throw new JsonConflictException("配置已被外部修改。请保留当前内容并重启应用后再编辑，避免覆盖新文件。");
            var snapshot = JsonSerializer.Deserialize(bytes, type) ?? throw new InvalidDataException("JSON 快照为空。");
            snapshot.Revision = value.Revision + 1;
            bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, type);
            await AtomicWriteAsync(path, bytes, true, token);
            value.Revision = snapshot.Revision;
            _fingerprints[path] = Convert.ToHexString(SHA256.HashData(bytes));
            _revisions[path] = snapshot.Revision;
        }
        finally
        {
            if (entered) _gate.Release();
            Interlocked.Decrement(ref _pending);
        }
    }

    private string Resolve(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relative));
        if (!full.StartsWith(_root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("配置路径超出数据目录。");
        return full;
    }

    private static async Task<(T Value, string Hash)> ReadAsync<T>(string path, JsonTypeInfo<T> type, CancellationToken token)
        where T : VersionedDocument
    {
        if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException("JSON 文件超过 1 MiB。");
        var bytes = await File.ReadAllBytesAsync(path, token);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("JSON 文件超过 1 MiB。");
        var value = JsonSerializer.Deserialize(bytes, type) ?? throw new InvalidDataException("JSON 文档为空。");
        value.Validate();
        return (value, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
    }

    private static async Task AtomicWriteAsync(string path, byte[] bytes, bool backup, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes, token);
                await stream.FlushAsync(token);
                stream.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            if (File.Exists(path)) File.Replace(temp, path, backup ? path + ".bak" : null);
            else File.Move(temp, path, overwrite: false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
