using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Mosaic.Host.Services;

internal static class WorkAreaNotificationChecks
{
    public static async Task RunAsync()
    {
        var original = ShellWorkArea.Capture();
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--work-area-notification-probe");
        using var probe = Process.Start(start)!;
        try
        {
            await probe.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12));
            var output = await probe.StandardOutput.ReadToEndAsync();
            var error = await probe.StandardError.ReadToEndAsync();
            if (probe.ExitCode != 0) throw new Exception($"Work-area notification probe failed ({probe.ExitCode}): {error}");
            Console.Write(output);
        }
        catch (TimeoutException)
        {
            throw new Exception("Work-area updates blocked the WPF dispatcher while SystemEvents waited for that dispatcher.");
        }
        finally
        {
            if (!probe.HasExited) { probe.Kill(); await probe.WaitForExitAsync(); }
            ShellWorkArea.Restore(original);
        }
    }

    public static int RunProbe()
    {
        var result = 1;
        var thread = new Thread(() =>
        {
            var original = ShellWorkArea.Capture();
            var notifications = 0;
            UserPreferenceChangedEventHandler listener = (_, _) => notifications++;
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                // Capture this UI dispatcher exactly as the real WinForms tray
                // icon and WPF system-preference handlers do in Mosaic.
                using var tray = new System.Windows.Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Information, Text = "Mosaic work-area regression fixture", Visible = true };
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                SystemEvents.UserPreferenceChanged += listener;
                var window = new Window { Title = "Mosaic work-area notification fixture", Width = 260, Height = 120, ShowActivated = false };
                var screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
                var updates = 0;
                var longestUpdate = TimeSpan.Zero;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
                timer.Tick += (_, _) =>
                {
                    if (updates < 12)
                    {
                        var elapsed = Stopwatch.StartNew();
                        ShellWorkArea.Apply(0, false, new Dictionary<System.Drawing.Rectangle, double> { [screen] = updates % 2 == 0 ? 8 : 16 });
                        if (elapsed.Elapsed > longestUpdate) longestUpdate = elapsed.Elapsed;
                        updates++;
                    }
                    else
                    {
                        timer.Stop();
                        if (notifications == 0) { app.Shutdown(2); return; }
                        if (longestUpdate.TotalMilliseconds > 250)
                        {
                            Console.Error.WriteLine($"Work-area notification blocked the UI for {longestUpdate.TotalMilliseconds:F0} ms.");
                            app.Shutdown(3); return;
                        }
                        Console.WriteLine($"  work-area notifications: {updates} UI-thread changes, {notifications} marshalled callbacks, longest change {longestUpdate.TotalMilliseconds:F1} ms.");
                        result = 0; app.Shutdown();
                    }
                };
                window.Loaded += (_, _) => timer.Start();
                app.Run(window);
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { SystemEvents.UserPreferenceChanged -= listener; ShellWorkArea.Restore(original); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        return result;
    }
}
