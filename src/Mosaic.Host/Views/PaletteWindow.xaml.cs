using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Mosaic.Host.Services;
using Mosaic.Host.ViewModels;

namespace Mosaic.Host.Views;

public partial class PaletteWindow : Window
{
    private readonly PaletteViewModel _vm;
    private readonly AppServices _services;
    private readonly Action _settings;
    private readonly Func<string?> _hotkeyError;
    private nint _previousWindow;
    private bool _reallyClose, _detailsExplicit;
    public PaletteWindow(AppServices services, SearchRuntime runtime, Action settings, Func<string?>? hotkeyError = null)
    {
        _services = services; _settings = settings; _vm = new(services, runtime);
        _hotkeyError = hotkeyError ?? (() => null);
        InitializeComponent(); DataContext = _vm;
        SourceInitialized += (_, _) => NativeShell.StyleWindow(new WindowInteropHelper(this).Handle, false, false);
        Closed += (_, _) => _vm.Dispose();
        TextCompositionManager.AddPreviewTextInputStartHandler(QueryInput, (_, _) => { _vm.IsComposing = true; _vm.SuspendQueries(); });
        TextCompositionManager.AddPreviewTextInputHandler(QueryInput, (_, _) =>
        { _vm.IsComposing = false; Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(_vm.RequestSearch)); });
    }
    public void Open()
    {
        _previousWindow = NativeShell.CaptureForeground();
        HotkeyLabel.Text = _hotkeyError() is not null ? "快捷键被占用 · 请打开搜索设置" : NativeShell.DescribeHotkey(_services.Settings.Search);
        HotkeyLabel.ToolTip = _hotkeyError();
        Show();
        NativeShell.PlacePalette(new WindowInteropHelper(this).Handle, _previousWindow, 940, 620);
        Activate();
        UiEffects.Enter(Content as FrameworkElement);
        _vm.IsComposing = false;
        _vm.Activate();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { QueryInput.Focus(); Keyboard.Focus(QueryInput); QueryInput.SelectAll(); }));
    }
    public void Dismiss(bool restore)
    {
        if (!IsVisible) return;
        _vm.Deactivate();
        Hide();
        if (restore) NativeShell.TryRestoreForeground(_previousWindow);
    }
    public void ClosePermanently() { _reallyClose = true; _vm.Deactivate(); Close(); }
    public Task FlushAsync() => _vm.FlushAsync();
    public Task FlushMetadataAsync() => _vm.FlushMetadataAsync();
    private void OnDeactivated(object? sender, EventArgs e) { if (IsVisible) Dismiss(false); }
    private void OnDismissClick(object sender, RoutedEventArgs e) => Dismiss(true);
    private void OnClearClick(object sender, RoutedEventArgs e) { _vm.Query = ""; QueryInput.Focus(); }
    private void OnSettingsClick(object sender, RoutedEventArgs e) { Dismiss(false); _settings(); }
    private async void OnOpenClick(object sender, RoutedEventArgs e) { if (await _vm.ExecuteSelectedAsync()) Dismiss(false); }
    private async void OnRevealClick(object sender, RoutedEventArgs e) { if (await _vm.ExecuteSelectedAsync(true)) Dismiss(false); }
    private async void OnFavoriteClick(object sender, RoutedEventArgs e) => await _vm.SaveMetadataAsync(true);
    private async void OnSaveTagsClick(object sender, RoutedEventArgs e) => await _vm.SaveMetadataAsync(false);
    private async void OnPinClick(object sender, RoutedEventArgs e) => await _vm.PinToDockAsync();
    private async void OnWorkspaceClick(object sender, RoutedEventArgs e) => await _vm.AddToWorkspaceAsync();
    private async void OnLibraryClick(object sender, RoutedEventArgs e) => await _vm.OpenLibraryAsync();
    private async void OnContinueClick(object sender, RoutedEventArgs e) => await _vm.ContinueAsync();
    private async void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
    { if (_vm.Selected is not null && await _vm.ExecuteSelectedAsync()) Dismiss(false); }
    private void OnCopyClick(object sender, RoutedEventArgs e) => CopyPath();
    private void CopyPath()
    {
        if (!_vm.CanReveal || _vm.Selected is null) return;
        try { Clipboard.SetText(_vm.Selected.Item.Target); _vm.SetStatus("路径已复制"); }
        catch (System.Runtime.InteropServices.COMException) { _vm.SetStatus("剪贴板暂时被其他程序占用，请重试。", true); }
    }
    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm.IsComposing || e.Key == Key.ImeProcessed) return;
        if (e.Key == Key.Escape)
        {
            if (_detailsExplicit && ActualWidth < 780) { _detailsExplicit = false; UpdateDetails(); QueryInput.Focus(); }
            else Dismiss(true);
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && QueryInput.IsKeyboardFocusWithin && Keyboard.Modifiers != ModifierKeys.Shift && _vm.HasSelection)
        { _detailsExplicit = true; UpdateDetails(); OpenAction.Focus(); e.Handled = true; }
        else if ((QueryInput.IsKeyboardFocusWithin || ResultList.IsKeyboardFocusWithin) && e.Key is Key.Up or Key.Down)
        { _vm.MoveSelection(e.Key == Key.Up ? -1 : 1); if (_vm.Selected is not null) ResultList.ScrollIntoView(_vm.Selected); e.Handled = true; }
        else if ((QueryInput.IsKeyboardFocusWithin || ResultList.IsKeyboardFocusWithin) && e.Key == Key.Enter)
        { e.Handled = true; if (await _vm.ExecuteSelectedAsync()) Dismiss(false); }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && !QueryInput.IsKeyboardFocusWithin && Keyboard.FocusedElement is not TextBox)
        { CopyPath(); e.Handled = true; }
    }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateDetails();
    private void UpdateDetails()
    {
        if (DetailsColumn is null) return;
        var visible = ActualWidth >= 780 || _detailsExplicit;
        DetailsColumn.Width = new GridLength(visible ? 276 : 0);
        DetailsPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_reallyClose) return;
        e.Cancel = true;
        Dispatcher.BeginInvoke(new Action(() => Dismiss(true)));
    }
}
