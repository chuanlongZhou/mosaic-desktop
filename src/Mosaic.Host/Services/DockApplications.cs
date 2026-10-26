using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Mosaic.Core.Models;
using Mosaic.Platform.Windows;

namespace Mosaic.Host.Services;

public sealed record DockApplication(DockItem Item, string Identity, ImageSource? Icon, nint[] Windows, bool Imported, string[] WindowTitles)
{
    public string Title => Item.Title;
    public string DisplayTitle => Windows.Length > 1 ? $"{Title} · {Windows.Length} 个窗口" : Title;
    public string Glyph => Item.Glyph;
    public bool IsRunning => Windows.Length > 0;
    public bool HasSingleWindow => Windows.Length == 1;
    public bool HasMultipleWindows => Windows.Length > 1;
    public string WindowCountLabel => Windows.Length > 99 ? "99+" : Windows.Length.ToString();
    public bool HasIcon => Icon is not null;
}

public sealed class DockApplications
{
    private readonly Dictionary<string, ImageSource?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Path, int Index)> _iconLocations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _identities = new(StringComparer.OrdinalIgnoreCase);
    private List<(DockItem Item, string Identity)> _pins = [];
    private DateTime _pinRead;

    public async Task<IReadOnlyList<DockApplication>> ReadAsync(IReadOnlyList<DockItem> manual, bool sync)
    {
        if (sync && DateTime.UtcNow - _pinRead > TimeSpan.FromSeconds(20))
        {
            _pins = await ReadPinsAsync(); _pinRead = DateTime.UtcNow;
        }
        var running = await Task.Run(ReadWindows);
        var result = new List<DockApplication>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(DockItem item, string identity, bool imported)
        {
            if (!seen.Add(identity)) return;
            var matches = running.Where(w => string.Equals(w.Identity, identity, StringComparison.OrdinalIgnoreCase)).ToArray();
            var iconPath = item.Target;
            result.Add(new(item, identity, Icon(iconPath), matches.Select(w => w.Window).ToArray(), imported,
                matches.Select(w => w.Title).ToArray()));
        }
        // Keep home and search available even for existing profiles with older default entries.
        Add(new() { Id = "mosaic-home", Title = "Mosaic 工作台", Glyph = "\uE80F", Command = "dashboard.open" }, "mosaic-home", false);
        if (sync) foreach (var (item, identity) in _pins) Add(item, identity, true);
        foreach (var item in manual)
        {
            if (sync && item.Command == "app.builtin") continue;
            if (item.Command is "dashboard.toggle" or "dashboard.open") continue;
            Add(item, Identity(item.Target, item.Command), false);
        }
        foreach (var window in running)
            Add(new() { Id = "running-" + window.Window, Title = window.Title, Target = window.Path, Command = "resource.open" }, window.Identity, true);
        return result;
    }

    private async Task<List<(DockItem Item, string Identity)>> ReadPinsAsync()
    {
        var result = new List<(DockItem, string)>();
        byte[] favorites = [];
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband"))
            favorites = key?.GetValue("Favorites") as byte[] ?? [];
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
        var candidates = new List<(DockItem Item, int Order)>();
        if (Directory.Exists(folder))
            foreach (var path in Directory.EnumerateFiles(folder, "*.lnk").Take(128))
                candidates.Add((new() { Title = Path.GetFileNameWithoutExtension(path), Target = path }, Position(favorites, Path.GetFileName(path))));
        IReadOnlyList<Mosaic.Core.Search.SearchItem> apps;
        try { apps = await ApplicationDiscovery.DiscoverAsync(CancellationToken.None); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { apps = []; }
        foreach (var app in apps)
        {
            var order = Position(favorites, app.Command == "app.packaged" ? app.Target : app.Name + ".lnk");
            if (order == int.MaxValue || candidates.Any(c => string.Equals(c.Item.Title, app.Name, StringComparison.OrdinalIgnoreCase))) continue;
            candidates.Add((new() { Title = app.Name, Target = app.Target, Command = app.Command }, order));
        }
        var identities = await OnSta(() => candidates.OrderBy(c => c.Order).Select(c => (c.Item, Identity(c.Item.Target, c.Item.Command))).ToList());
        result.AddRange(identities);
        return result;
    }
    private static int Position(byte[] data, string text)
    {
        if (string.IsNullOrEmpty(text)) return int.MaxValue;
        var bytes = Encoding.Unicode.GetBytes(text);
        var index = data.AsSpan().IndexOf(bytes);
        return index < 0 ? int.MaxValue : index;
    }
    private static Task<T> OnSta<T>(Func<T> run)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { completion.SetResult(run()); } catch (Exception ex) { completion.SetException(ex); } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    private string Identity(string path, string command)
    {
        if (command == "app.packaged") return path;
        if (!Path.IsPathRooted(path)) return command + ":" + path;
        if (_identities.TryGetValue(path, out var cached)) return cached;
        var identity = path;
        if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            object? shell = null, folder = null, entry = null, link = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
                folder = ((dynamic)shell!).NameSpace(Path.GetDirectoryName(path));
                entry = ((dynamic)folder!).ParseName(Path.GetFileName(path));
                var appId = (string?)((dynamic)entry!).ExtendedProperty("System.AppUserModel.ID");
                link = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!);
                ((System.Runtime.InteropServices.ComTypes.IPersistFile)link!).Load(path, 0);
                var targetBuffer = new StringBuilder(4096);
                ((IShellLink)link!).GetPath(targetBuffer, targetBuffer.Capacity, 0, 4);
                var target = targetBuffer.ToString();
                var iconBuffer = new StringBuilder(4096);
                ((IShellLink)link!).GetIconLocation(iconBuffer, iconBuffer.Capacity, out var iconIndex);
                var iconPath = Environment.ExpandEnvironmentVariables(iconBuffer.ToString());
                _iconLocations[path] = (string.IsNullOrWhiteSpace(iconPath) ? target : iconPath, iconIndex);
                // Executable identity lets classic windows with no explicit AppID merge with their pin.
                identity = !string.IsNullOrWhiteSpace(appId) && appId.Contains('!') ? appId : string.IsNullOrWhiteSpace(target) ? path : target;
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
            finally { foreach (var obj in new[] { link, entry, folder, shell }) if (obj is not null && Marshal.IsComObject(obj)) Marshal.FinalReleaseComObject(obj); }
        }
        if (_identities.Count > 512) _identities.Clear();
        return _identities[path] = identity;
    }

    private record Running(nint Window, string Identity, string Path, string Title);
    private static List<Running> ReadWindows()
    {
        var result = new List<Running>();
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || GetWindow(window, 4) != 0 || (GetWindowLongPtr(window, -20).ToInt64() & 0x80) != 0) return true;
            GetWindowThreadProcessId(window, out var pid);
            if (pid == Environment.ProcessId) return true;
            DwmGetWindowAttribute(window, 14, out var cloaked, sizeof(int));
            if (cloaked != 0) return true;
            var title = new StringBuilder(512); GetWindowText(window, title, 512);
            if (title.Length == 0) return true;
            var className = new StringBuilder(128); GetClassName(window, className, 128);
            if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            try
            {
                using var process = Process.GetProcessById((int)pid);
                var path = process.MainModule?.FileName ?? "";
                if (path.Length == 0) return true;
                var id = ProcessAppId(process.Handle);
                result.Add(new(window, id.Contains('!') ? id : path, path, title.ToString()));
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException) { }
            return true;
        }, 0);
        return result;
    }
    private static string ProcessAppId(nint process)
    {
        uint length = 512; var buffer = new StringBuilder(512);
        return GetApplicationUserModelId(process, ref length, buffer) == 0 ? buffer.ToString() : "";
    }
    private ImageSource? Icon(string path)
    {
        if (_icons.TryGetValue(path, out var image)) return image;
        if (string.IsNullOrEmpty(path)) return null;
        if (path.Contains('!') && !Path.IsPathRooted(path))
        {
            object? factory = null; nint bitmap = 0;
            try
            {
                var iid = typeof(IShellItemImageFactory).GUID;
                if (SHCreateItemFromParsingName("shell:AppsFolder\\" + path, 0, ref iid, out factory) == 0)
                {
                    ((IShellItemImageFactory)factory!).GetImage(new NativeSize { Width = 48, Height = 48 }, 4, out bitmap);
                    image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); image.Freeze();
                }
            }
            catch (COMException) { }
            finally { if (bitmap != 0) DeleteObject(bitmap); if (factory is not null && Marshal.IsComObject(factory)) Marshal.FinalReleaseComObject(factory); }
            return _icons[path] = image;
        }
        if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            // Extract the shortcut's declared icon resource directly, without the Shell link overlay.
            if (_iconLocations.TryGetValue(path, out var location) && !string.IsNullOrWhiteSpace(location.Path))
            {
                ExtractIconEx(location.Path, location.Index, out var large, out var small, 1);
                try
                {
                    if (large != 0) { image = Imaging.CreateBitmapSourceFromHIcon(large, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); image.Freeze(); }
                }
                finally { if (large != 0) DestroyIcon(large); if (small != 0) DestroyIcon(small); }
            }
            return _icons[path] = image;
        }
        var info = new ShellFileInfo();
        if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), 0x100) != 0 && info.Icon != 0)
        {
            try { image = Imaging.CreateBitmapSourceFromHIcon(info.Icon, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); image.Freeze(); }
            finally { DestroyIcon(info.Icon); }
        }
        if (_icons.Count > 512) _icons.Clear();
        return _icons[path] = image;
    }
    public static void Activate(nint window)
    {
        if (!IsWindow(window)) return;
        if (IsIconic(window)) ShowWindowAsync(window, 9);
        SetForegroundWindow(window);
    }
    private delegate bool EnumCallback(nint window, nint data);
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length, nint findData, uint flags);
        void GetIDList(out nint list);
        void SetIDList(nint list);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int length);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int length);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int length);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetHotkey(out short key);
        void SetHotkey(short key);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length, out int index);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int Width, Height; }
    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory { void GetImage(NativeSize size, uint flags, out nint bitmap); }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string name, nint context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object? item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint bitmap);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ShellFileInfo
    {
        public nint Icon; public int IconIndex; public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string path, int index, out nint large, out nint small, uint count);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint SHGetFileInfo(string path, uint attributes, ref ShellFileInfo info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, nint data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int maximum);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder text, int maximum);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetApplicationUserModelId(nint process, ref uint length, StringBuilder id);
}
