using Mosaic.Core.Models;
using Mosaic.Core.Search;

namespace Mosaic.Platform.Windows;

public static class FileDiscovery
{
    private static readonly HashSet<string> ExcludedNames = new(StringComparer.OrdinalIgnoreCase)
    { ".git", ".tools", ".artifacts", "node_modules", "bin", "obj", "$RECYCLE.BIN", "System Volume Information" };
    private const FileAttributes SkipAttributes = FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Offline | (FileAttributes)0x00440000;
    public static bool IgnoreChange(string root, string path) => Path.GetRelativePath(root, path).Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Any(ExcludedNames.Contains);

    public static string ValidateRoot(string root, string dataRoot)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Path.IsPathFullyQualified(full) || full.StartsWith("\\\\", StringComparison.Ordinal) || full.Equals(Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full)!), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请选择本机的具体文件夹，首版不扫描整盘或网络共享。");
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException("目录不存在或已离线：" + full);
        if ((File.GetAttributes(full) & SkipAttributes) != 0) throw new InvalidDataException("不扫描系统、链接或云占位目录。");
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (SearchIdentity.IsWithin(full, windows) || SearchIdentity.IsWithin(full, dataRoot) || SearchIdentity.IsWithin(dataRoot, full))
            throw new InvalidDataException("请选择项目或资料目录；不扫描 Windows、Mosaic 数据目录或其父目录。");
        return full;
    }

    public static IEnumerable<IndexEntry> Enumerate(string root, CancellationToken token)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("目录离线或不可用：" + root);
        if ((File.GetAttributes(root) & SkipAttributes) != 0) throw new IOException("索引根目录变为链接或占位目录，已停止扫描。");
        var stack = new Stack<(IEnumerator<FileSystemInfo> Iterator, int Depth)>();
        IEnumerator<FileSystemInfo> Open(string path) => new DirectoryInfo(path).EnumerateFileSystemInfos("*", new EnumerationOptions
        { IgnoreInaccessible = true, RecurseSubdirectories = false, AttributesToSkip = SkipAttributes, ReturnSpecialDirectories = false }).GetEnumerator();
        stack.Push((Open(root), 0));
        var count = 0;
        try
        {
            while (stack.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var (iterator, depth) = stack.Peek();
                FileSystemInfo? entry = null;
                try { if (iterator.MoveNext()) entry = iterator.Current; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                if (entry is null) { iterator.Dispose(); stack.Pop(); continue; }
                bool directory;
                IndexEntry? record = null;
                try
                {
                    directory = (entry.Attributes & FileAttributes.Directory) != 0;
                    if ((entry.Attributes & SkipAttributes) != 0 || (directory && ExcludedNames.Contains(entry.Name)) || entry.FullName.Length > 4096) continue;
                    record = new() { Name = entry.Name, Path = entry.FullName, IsDirectory = directory, Length = entry is FileInfo file ? file.Length : 0, ModifiedUtc = entry.LastWriteTimeUtc };
                    if (directory && depth < 64) stack.Push((Open(entry.FullName), depth + 1));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                if (record is not null) yield return record;
                if (++count >= 1_000_000) throw new IOException("单个目录已超过首版扫描范围，保留旧索引，请缩小范围。");
            }
        }
        finally { while (stack.TryPop(out var entry)) entry.Iterator.Dispose(); }
    }
}
