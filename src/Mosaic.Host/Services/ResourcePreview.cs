using System.Text;
using System.Windows.Media.Imaging;
using Mosaic.Core.Search;

namespace Mosaic.Host.Services;

public sealed record ResourcePreviewResult(string Information, string Text = "", BitmapSource? Image = null);

public static class ResourcePreview
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase) { ".txt", ".md", ".json", ".jsonl", ".csv", ".log", ".yaml", ".yml", ".cs", ".py", ".xml" };
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp" };
    public static ResourcePreviewResult Read(SearchItem item, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (item.Command != "resource.open") return new(item.Kind is SearchKind.App ? "通过 Windows 应用接口打开" : "Mosaic 内置命令");
        if (!Path.IsPathFullyQualified(item.Target) || item.Target.StartsWith("\\\\", StringComparison.Ordinal)) return new("网络或非本地资源 · 按需打开，不自动预览");
        try
        {
            var attributes = File.GetAttributes(item.Target);
            const FileAttributes excluded = FileAttributes.Offline | FileAttributes.ReparsePoint | (FileAttributes)0x00440000;
            for (var parent = new DirectoryInfo(Path.GetDirectoryName(item.Target)!); parent is not null; parent = parent.Parent)
                if ((parent.Attributes & excluded) != 0) return new("链接、离线或云占位目录 · 不自动预览");
            if ((attributes & excluded) != 0) return new("链接、离线或云占位资源 · 不自动预览");
            if ((attributes & FileAttributes.Directory) != 0) return new("本地文件夹 · 不读取目录内容");
            var file = new FileInfo(item.Target);
            var info = $"{file.Length / 1048576.0:0.##} MiB · 修改于 {file.LastWriteTime:g}";
            token.ThrowIfCancellationRequested();
            if (TextExtensions.Contains(file.Extension))
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
                using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, bufferSize: 4096);
                var chars = new char[2049];
                var read = reader.ReadBlock(chars, 0, chars.Length);
                token.ThrowIfCancellationRequested();
                var text = new string(chars, 0, Math.Min(2048, read)).Replace('\0', '�');
                return new(info, text + (read > 2048 ? "\n…（仅预览前 2,048 字符）" : ""));
            }
            if (ImageExtensions.Contains(file.Extension) && file.Length <= 16 * 1024 * 1024)
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                var frame = decoder.Frames[0];
                if (frame.PixelWidth > 12000 || frame.PixelHeight > 12000 || (long)frame.PixelWidth * frame.PixelHeight > 12_000_000)
                    return new(info, "图像尺寸超过预览预算，请使用关联应用打开。");
                stream.Position = 0;
                var image = new BitmapImage();
                image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 256; image.DecodePixelHeight = 180; image.StreamSource = stream;
                image.EndInit(); image.Freeze();
                token.ThrowIfCancellationRequested();
                return new(info, Image: image);
            }
            return new(info, file.Length > 16 * 1024 * 1024 && ImageExtensions.Contains(file.Extension) ? "图片超过 16 MiB，请使用关联应用打开。" : "此格式仅显示基础信息，可通过关联应用打开。");
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return new("资源不存在或磁盘离线 · 收藏、标签和集合仍保留"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException or System.Runtime.InteropServices.COMException)
        { return new("无法自动预览此资源，可尝试打开所在位置。"); }
    }
}
