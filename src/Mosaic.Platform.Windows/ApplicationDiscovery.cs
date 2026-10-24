using System.Runtime.InteropServices;
using Mosaic.Core.Search;

namespace Mosaic.Platform.Windows;

public static class ApplicationDiscovery
{
    public static Task<IReadOnlyList<SearchItem>> DiscoverAsync(CancellationToken token)
    {
        var completion = new TaskCompletionSource<IReadOnlyList<SearchItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var found = new Dictionary<string, SearchItem>(StringComparer.OrdinalIgnoreCase);
                foreach (var (name, target, aliases) in new[] { ("记事本", "notepad", "notepad text editor"), ("计算器", "calculator", "calculator calc") })
                {
                    var item = new SearchItem(name, SearchKind.App, "app.builtin", target, "Windows 应用", aliases);
                    found[item.Key] = item;
                }
                foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms) })
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (var file in FileDiscovery.Enumerate(root, token))
                    {
                        token.ThrowIfCancellationRequested();
                        if (found.Count >= 4096) break;
                        if (file.IsDirectory || Path.GetExtension(file.Path).ToLowerInvariant() is not (".lnk" or ".exe" or ".appref-ms")) continue;
                        var item = new SearchItem(Path.GetFileNameWithoutExtension(file.Name), SearchKind.App, "resource.open", file.Path, "开始菜单");
                        found[item.Key] = item;
                    }
                }
                ReadPackagedApps(found, token);
                completion.TrySetResult(found.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray());
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception ex) { completion.TrySetException(ex); }
        }) { IsBackground = true, Name = "Mosaic application discovery" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static void ReadPackagedApps(Dictionary<string, SearchItem> found, CancellationToken token)
    {
        object? shell = null, folder = null, items = null;
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null) return;
            shell = Activator.CreateInstance(type);
            if (shell is null) return;
            folder = ((dynamic)shell).NameSpace("shell:AppsFolder");
            if (folder is null) return;
            items = ((dynamic)folder).Items();
            var count = Math.Min((int)((dynamic)items).Count, 4096);
            for (var i = 0; i < count && found.Count < 4096; i++)
            {
                token.ThrowIfCancellationRequested();
                object? entry = null;
                try
                {
                    entry = ((dynamic)items).Item(i);
                    var name = (string)((dynamic)entry).Name;
                    var appId = (string?)((dynamic)entry).ExtendedProperty("System.AppUserModel.ID");
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(appId) || !appId.Contains('!') || appId.Length > 256) continue;
                    var item = new SearchItem(name, SearchKind.App, "app.packaged", appId, "已安装应用", appId);
                    found[item.Key] = item;
                }
                catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
                finally { Release(entry); }
            }
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { /* Start menu and built-ins remain available. */ }
        finally { Release(items); Release(folder); Release(shell); }
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
}
