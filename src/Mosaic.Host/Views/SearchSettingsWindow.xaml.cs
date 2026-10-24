using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Win32;
using Mosaic.Core.Models;
using Mosaic.Host.Services;
using Mosaic.Platform.Windows;
using Mosaic.Core.Search;

namespace Mosaic.Host.Views;

public partial class SearchSettingsWindow : Window
{
    private readonly AppServices _services;
    private readonly SearchRuntime _runtime;
    private readonly Func<AppSettings, Task> _save;
    private readonly ObservableCollection<string> _roots;
    public bool Saved { get; private set; }
    public SearchSettingsWindow(AppServices services, SearchRuntime runtime, Func<AppSettings, Task> save, string? hotkeyError = null)
    {
        _services = services; _runtime = runtime; _save = save;
        InitializeComponent();
        _roots = new(services.Settings.Search.Roots); RootsList.ItemsSource = _roots;
        var options = services.Settings.Search;
        EnabledCheck.IsChecked = options.Enabled; IndexEnabledCheck.IsChecked = options.IndexEnabled;
        EverythingCheck.IsChecked = options.UseEverything; GlobalScopeCheck.IsChecked = options.EverythingGlobalScope; IgnoreAccentsCheck.IsChecked = options.IgnoreAccents;
        var shortcuts = new List<ShortcutOption> { new("Ctrl + Alt + Space", 3, 0x20), new("Ctrl + Shift + Space", 6, 0x20), new("Alt + Shift + Space", 5, 0x20) };
        var selected = shortcuts.FirstOrDefault(x => x.Modifiers == options.HotkeyModifiers && x.Key == options.HotkeyVirtualKey);
        if (selected is null) { selected = new(NativeShell.DescribeHotkey(options), options.HotkeyModifiers, options.HotkeyVirtualKey); shortcuts.Add(selected); }
        HotkeyCombo.ItemsSource = shortcuts; HotkeyCombo.SelectedItem = selected;
        SourceInitialized += (_, _) => NativeShell.StyleWindow(new WindowInteropHelper(this).Handle, false, false);
        _runtime.Changed += OnStatusChanged;
        Closed += (_, _) => _runtime.Changed -= OnStatusChanged;
        Height = Math.Min(710, SystemParameters.WorkArea.Height - 40);
        IndexStatusLabel.Text = _runtime.IndexStatus;
        ErrorLabel.Text = hotkeyError ?? "";
    }
    private void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "选择 Mosaic 文件搜索范围" };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            if (_roots.Count >= 8) throw new InvalidOperationException("最多选择 8 个搜索目录。");
            var root = FileDiscovery.ValidateRoot(picker.FolderName, _services.Store.Root);
            if (_roots.Any(x => SearchIdentity.IsWithin(root, x) || SearchIdentity.IsWithin(x, root))) throw new InvalidOperationException("该目录与已有范围重叠，请保留一个合适的上级目录。");
            _roots.Add(root); ErrorLabel.Text = "";
        }
        catch (Exception ex) { ErrorLabel.Text = ex.Message; }
    }
    private void OnRemoveFolderClick(object sender, RoutedEventArgs e) { if ((sender as Button)?.Tag is string root) _roots.Remove(root); }
    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false; SettingsBody.IsEnabled = false;
        try
        {
            var candidate = _services.CopySettings();
            candidate.Search.Enabled = EnabledCheck.IsChecked == true; candidate.Search.IndexEnabled = IndexEnabledCheck.IsChecked == true;
            candidate.Search.UseEverything = EverythingCheck.IsChecked == true; candidate.Search.EverythingGlobalScope = GlobalScopeCheck.IsChecked == true;
            candidate.Search.IgnoreAccents = IgnoreAccentsCheck.IsChecked == true;
            candidate.Search.Roots = _roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (HotkeyCombo.SelectedItem is ShortcutOption shortcut) { candidate.Search.HotkeyModifiers = shortcut.Modifiers; candidate.Search.HotkeyVirtualKey = shortcut.Key; }
            if (candidate.Search.IndexEnabled) foreach (var root in candidate.Search.Roots) FileDiscovery.ValidateRoot(root, _services.Store.Root);
            candidate.Validate();
            await _save(candidate);
            Saved = true; Close();
        }
        catch (Exception ex) { ErrorLabel.Text = "未保存：" + ex.Message; }
        finally { SaveButton.IsEnabled = true; SettingsBody.IsEnabled = true; }
    }
    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
    private void OnRebuildClick(object sender, RoutedEventArgs e) => _runtime.RequestRebuild();
    private void OnCancelScanClick(object sender, RoutedEventArgs e) => _runtime.CancelRebuild();
    private void OnStatusChanged() => Dispatcher.BeginInvoke(() => IndexStatusLabel.Text = _runtime.IndexStatus);
    private sealed record ShortcutOption(string Label, uint Modifiers, uint Key);
}
