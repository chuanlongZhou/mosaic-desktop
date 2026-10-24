using System.Runtime.InteropServices;
using Mosaic.Core.Search;
using Mosaic.Platform.Windows;

namespace Mosaic.Host.Services;

public sealed class EverythingClient(nint replyWindow) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private TaskCompletionSource<byte[]>? _pending;
    private uint _replyId;
    private int _sequence = Random.Shared.Next(10000, 1000000);
    private bool _disposed;

    public async Task<SearchSourceResult> SearchAsync(SearchQuery query, SearchSettings settings, CancellationToken token)
    {
        if (!settings.UseEverything) return new("Everything", [], "Everything 未启用");
        if (query.Normalized.Length == 0 || (!settings.EverythingGlobalScope && settings.Roots.Count == 0)) return new("Everything", [], "Everything 限定为已选目录");
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_disposed) throw new OperationCanceledException();
            var window = await Task.Run(() => FindWindow("EVERYTHING_TASKBAR_NOTIFICATION", null), token).ConfigureAwait(false);
            if (window == 0) return new("Everything", [], "Everything 未运行，使用本地搜索");
            var loaded = await Task.Run(() => SendMessageTimeout(window, 0x400, 401, 0, 3, 80, out var result) != 0 && result != 0, token).ConfigureAwait(false);
            if (!loaded) return new("Everything", [], "Everything 未就绪，使用本地搜索");
            var id = unchecked((uint)Interlocked.Increment(ref _sequence));
            var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_sync) { _replyId = id; _pending = completion; }
            var payload = EverythingProtocol.BuildQuery(unchecked((uint)replyWindow.ToInt64()), id, query, settings);
            var sent = await Task.Run(() => SendQuery(window, payload), token).ConfigureAwait(false);
            if (!sent) return new("Everything", [], "Everything IPC 不可用，使用本地搜索");
            byte[] bytes;
            try { bytes = await completion.Task.WaitAsync(TimeSpan.FromMilliseconds(1200), token).ConfigureAwait(false); }
            catch (TimeoutException) { return new("Everything", [], "Everything 响应超时，使用本地搜索"); }
            var (items, partial) = EverythingProtocol.ParseReply(bytes);
            var top = new TopResults();
            foreach (var item in items)
                if (settings.EverythingGlobalScope || settings.Roots.Any(root => SearchIdentity.IsWithin(item.Target, root))) top.Add(SearchRanking.Match(item, query));
            return new("Everything", top.Sorted(), partial ? "Everything 前 200 个候选（非全局完整排序）" : "Everything 已连接", partial);
        }
        finally { lock (_sync) _pending = null; _gate.Release(); }
    }

    public bool Receive(nint pointer)
    {
        var data = Marshal.PtrToStructure<CopyData>(pointer);
        lock (_sync)
        {
            if (_pending is null || data.Id.ToInt64() != _replyId || data.Length is < 28 or > 1024 * 1024 || data.Data == 0) return false;
            var bytes = new byte[data.Length];
            Marshal.Copy(data.Data, bytes, 0, bytes.Length);
            _pending.TrySetResult(bytes);
            return true;
        }
    }

    private bool SendQuery(nint window, byte[] payload)
    {
        var buffer = Marshal.AllocHGlobal(payload.Length);
        var structure = Marshal.AllocHGlobal(Marshal.SizeOf<CopyData>());
        try
        {
            Marshal.Copy(payload, 0, buffer, payload.Length);
            Marshal.StructureToPtr(new CopyData { Id = 2, Length = payload.Length, Data = buffer }, structure, false);
            return SendMessageTimeout(window, 0x4A, replyWindow, structure, 3, 100, out var result) != 0 && result != 0;
        }
        finally { Marshal.FreeHGlobal(structure); Marshal.FreeHGlobal(buffer); }
    }
    public void Dispose() { _disposed = true; lock (_sync) _pending?.TrySetCanceled(); }
    [StructLayout(LayoutKind.Sequential)] private struct CopyData { public nint Id; public int Length; public nint Data; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string className, string? title);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SendMessageTimeout(nint window, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
}
