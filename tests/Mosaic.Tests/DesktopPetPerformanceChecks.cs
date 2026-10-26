using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Mosaic.Core;
using Mosaic.Core.Models;
using Mosaic.Host.Controls;
using Mosaic.Host.Services;
using Mosaic.Storage.Json;

internal static class DesktopPetPerformanceChecks
{
    public static Task RunAsync(string report)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Mosaic;component/Themes/Dark.xaml", UriKind.Relative) });
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                Complete(RunCasesAsync(report)); app.Shutdown(); completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }

    private static async Task RunCasesAsync(string report)
    {
        var root = Path.Combine(Path.GetTempPath(), "Mosaic.Tests", Guid.NewGuid().ToString("N"));
        var services = new AppServices(new JsonDocumentStore(root)); await services.InitializeAsync();
        await services.UpdatePetAsync(p => { p.Size = 220; p.Left = 24; p.Top = 80; p.FollowCursor = false; p.ShowBubbles = false; p.SleepWhenIdle = false; p.ReactToAppClose = false; p.ReactToEnter = false; p.ReactToDesktop = false; });
        await services.UpdatePetAsync(options => options.Companions = new[] { PetCharacter.Gpt, PetCharacter.Gemini, PetCharacter.Claude, PetCharacter.Qwen }.Select(character => new DesktopPetCompanion
        {
            Character = character, Size = 160, FollowCursor = false, ShowBubbles = false, SleepWhenIdle = false,
            ReactToAppClose = false, ReactToEnter = false, ReactToDesktop = false
        }).ToList());
        using var pet = new DesktopPetService(services, () => { });
        services.SettingsChanged += (_, _) => pet.ApplySettings();
        var editor = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16) };
        var host = new Window { Title = "Mosaic synthetic input performance probe", Content = editor, Width = 400, Height = 220, Left = 320, Top = 100, ShowActivated = false, ShowInTaskbar = false };
        host.Show();
        // Exercise the actual event subscriber without injecting keys into the user's apps.
        var handler = typeof(DesktopPetService).GetMethod("RecordInput", BindingFlags.NonPublic | BindingFlags.Instance) ??
                      typeof(DesktopPetService).GetMethod("Trigger", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var record = (Action<PetTrigger>)handler.CreateDelegate(typeof(Action<PetTrigger>), pet);
        var results = new List<object>();
        try
        {
            foreach (var enabled in new[] { false, true })
            {
                editor.Clear(); await services.UpdatePetAsync(p => p.Enabled = enabled);
                if (enabled) foreach (var companion in pet.Pets) companion.Behavior.Tick(Environment.TickCount64 + 10000, 0, false, 90, false, 0, 0);
                await Task.Delay(200);
                var visualChanges = 0;
                EventHandler changed = (_, _) => visualChanges++;
                var descriptor = DependencyPropertyDescriptor.FromProperty(Image.SourceProperty, typeof(WhalePetSprite));
                var sprite = pet.Window?.Sprite;
                if (sprite is not null) { descriptor.AddValueChanged(sprite, changed); sprite.RenderTransform.Changed += changed; }
                var delays = new List<double>(); var handlerTimes = new List<double>();
                var dispatcher = Dispatcher.CurrentDispatcher;
                using var process = Process.GetCurrentProcess();
                var cpuBefore = process.TotalProcessorTime; var duration = Stopwatch.StartNew();
                try
                {
                    var producer = Task.Run(async () =>
                    {
                        // Request one event every 8ms; actual cadence depends on OS timer resolution.
                        for (var i = 0; i < 250; i++)
                        {
                            var sent = Stopwatch.GetTimestamp();
                            _ = dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                            {
                                var started = Stopwatch.GetTimestamp(); record(PetTrigger.Typing);
                                handlerTimes.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                                editor.AppendText("x");
                                delays.Add(Stopwatch.GetElapsedTime(sent).TotalMilliseconds);
                            }));
                            await Task.Delay(8);
                        }
                    });
                    await producer;
                    await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                }
                finally { if (sprite is not null) { descriptor.RemoveValueChanged(sprite, changed); sprite.RenderTransform.Changed -= changed; } }
                duration.Stop();
                if (editor.Text.Length != 250 || delays.Count != 250) throw new Exception("Synthetic editor lost input under pet load.");
                delays.Sort(); handlerTimes.Sort();
                results.Add(new
                {
                    PetEnabled = enabled, PetCount = pet.Pets.Count, InputEvents = delays.Count, VisualChanges = visualChanges,
                    ElapsedMs = Math.Round(duration.Elapsed.TotalMilliseconds, 2),
                    CpuMs = Math.Round((process.TotalProcessorTime - cpuBefore).TotalMilliseconds, 2),
                    InputP95Ms = Math.Round(delays[(int)(delays.Count * .95)], 3), InputMaxMs = Math.Round(delays[^1], 3),
                    HandlerP95Ms = Math.Round(handlerTimes[(int)(handlerTimes.Count * .95)], 4), HandlerMaxMs = Math.Round(handlerTimes[^1], 4)
                });
            }
            var json = JsonSerializer.Serialize(new { Method = "Synthetic WPF editor, 250 events requested every 8ms (actual cadence depends on OS timer resolution); not physical keyboard or IME latency", Handler = handler.Name, Cases = results }, new JsonSerializerOptions { WriteIndented = true });
            var path = Path.GetFullPath(report); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, json);
            Console.WriteLine(json);
        }
        finally { host.Close(); }
    }

    private static void Complete(Task task)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        _ = task.ContinueWith(_ => dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false)), TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }
}
