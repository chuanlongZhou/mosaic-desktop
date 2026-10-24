using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Mosaic.Platform.Windows;

public static class ShellLauncher
{
    public static void OpenResource(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
            throw new FileNotFoundException("入口已不存在或当前无法访问。请重新选择文件或文件夹。", path);
        Process.Start(new ProcessStartInfo(Path.GetFullPath(path)) { UseShellExecute = true });
    }

    public static void OpenBuiltIn(string? name)
    {
        var executable = name switch
        {
            "notepad" => Path.Combine(Environment.SystemDirectory, "notepad.exe"),
            "calculator" => Path.Combine(Environment.SystemDirectory, "calc.exe"),
            _ => throw new InvalidOperationException("未知的内置应用。")
        };
        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
    }

    public static void Reveal(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path))) throw new FileNotFoundException("资源已不存在或当前无法访问。", path);
        var item = ILCreateFromPath(Path.GetFullPath(path));
        if (item == 0) throw new InvalidOperationException("无法定位这个资源。");
        try { Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(item, 0, 0, 0)); }
        finally { ILFree(item); }
    }

    public static void OpenPackaged(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId) || appId.Length > 256 || !appId.Contains('!') || appId.Any(c => !(char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or '!')))
            throw new InvalidOperationException("应用标识符无效。");
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false };
        info.ArgumentList.Add("shell:AppsFolder\\" + appId);
        Process.Start(info);
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint ILCreateFromPath(string path);
    [DllImport("shell32.dll")] private static extern void ILFree(nint item);
    [DllImport("shell32.dll")] private static extern int SHOpenFolderAndSelectItems(nint item, uint count, nint children, uint flags);
}
