using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace Mosaic.Platform.Windows;

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    public bool IsPrimary { get; }

    public SingleInstance(string dataRoot)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + "|" + Path.GetFullPath(dataRoot).ToUpperInvariant())))[..24];
        _pipeName = "Mosaic-" + key;
        _mutex = new Mutex(true, "Local\\" + _pipeName, out var created);
        IsPrimary = created;
    }

    public async Task NotifyPrimaryAsync(string action = "dashboard")
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await pipe.ConnectAsync(timeout.Token);
            if (action is not ("dashboard" or "palette" or "shutdown" or "library" or "workspaces")) return;
            await pipe.WriteAsync(Encoding.UTF8.GetBytes(action), timeout.Token);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException) { }
    }

    public async Task ListenAsync(Action<string> activated)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                var buffer = new byte[32];
                var count = 0;
                while (count < buffer.Length)
                {
                    var read = await pipe.ReadAsync(buffer.AsMemory(count), requestTimeout.Token);
                    if (read == 0) break;
                    count += read;
                }
                var action = Encoding.UTF8.GetString(buffer, 0, count);
                if (action is "dashboard" or "palette" or "shutdown" or "library" or "workspaces") activated(action);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { await Task.Delay(250, _stop.Token).ConfigureAwait(false); }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
        _stop.Dispose();
    }
}
