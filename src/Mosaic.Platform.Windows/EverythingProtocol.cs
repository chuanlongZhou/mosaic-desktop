using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using Mosaic.Core.Search;

namespace Mosaic.Platform.Windows;

/// <summary>Unicode IPC v1 wire format from voidtools' public everything_ipc.h. No SDK DLL is distributed.</summary>
public static class EverythingProtocol
{
    public const int MaximumResults = 200;
    public static byte[] BuildQuery(uint window, uint replyId, SearchQuery query, SearchSettings settings)
    {
        if (!settings.EverythingGlobalScope && settings.Roots.Count == 0) throw new InvalidOperationException("未授权全盘范围，且没有选定目录。");
        var lookahead = string.Concat(query.Normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(8).Select(x => "(?=.*" + Regex.Escape(x) + ")"));
        var scope = settings.EverythingGlobalScope ? "" : "(?:" + string.Join("|", settings.Roots.Select(x => Regex.Escape(Path.GetFullPath(x).TrimEnd('\\')) + "(?:\\\\|$)")) + ")";
        var pattern = "(?i)^" + lookahead + scope + ".*$";
        var text = Encoding.Unicode.GetBytes(pattern + "\0");
        if (text.Length > 65536) throw new InvalidOperationException("Everything 查询范围过长。");
        var bytes = new byte[20 + text.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0), window);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), replyId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 0xC); // REGEX | MATCHPATH: user text is escaped, not Everything syntax.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), MaximumResults);
        text.CopyTo(bytes, 20);
        return bytes;
    }

    public static (IReadOnlyList<SearchItem> Items, bool Partial) ParseReply(byte[] data)
    {
        if (data.Length is < 28 or > 1024 * 1024) throw new InvalidDataException("Everything 返回数据大小无效。");
        var total = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(20));
        if (count > MaximumResults || 28L + count * 12L > data.Length) throw new InvalidDataException("Everything 返回项目数无效。");
        var items = new List<SearchItem>();
        for (var i = 0; i < count; i++)
        {
            var offset = 28 + i * 12;
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)offset));
            var name = ReadString(data, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)offset + 4)));
            var directory = ReadString(data, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)offset + 8)));
            if (name.Length == 0 || name.IndexOfAny(['\\', '/']) >= 0 || !Path.IsPathFullyQualified(directory)) continue;
            var full = Path.Combine(directory, name);
            if (full.Length > 4096) continue;
            items.Add(new(name, SearchIdentity.FileKind(full, (flags & 1) != 0), "resource.open", full, "Everything"));
        }
        return (items, total > count);
    }

    private static string ReadString(byte[] data, uint offset)
    {
        if (offset >= data.Length || offset % 2 != 0) throw new InvalidDataException("Everything 字符串偏移无效。");
        var end = (int)offset;
        while (end + 1 < data.Length && end - offset <= 8192)
        {
            if (data[end] == 0 && data[end + 1] == 0) return Encoding.Unicode.GetString(data, (int)offset, end - (int)offset);
            end += 2;
        }
        throw new InvalidDataException("Everything 字符串缺少终止符或过长。");
    }
}
