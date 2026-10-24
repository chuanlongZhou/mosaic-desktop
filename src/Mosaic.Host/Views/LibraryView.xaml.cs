using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Mosaic.Core.Models;
using Mosaic.Host.Services;
using Mosaic.Host.ViewModels;

namespace Mosaic.Host.Views;

public partial class LibraryView : UserControl, IDisposable
{
    private readonly AppServices _services;
    private readonly SearchRuntime _runtime;
    public LibraryViewModel ViewModel { get; }
    private bool? _details;
    private string? _pendingWorkspace;
    public LibraryView(AppServices services, SearchRuntime runtime, string? selectedId = null)
    {
        _services = services; _runtime = runtime; ViewModel = new(services, runtime);
        InitializeComponent(); DataContext = ViewModel;
        AllowDrop = true;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true; };
        Drop += async (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) { e.Handled = true; await ViewModel.AddPathsAsync(paths.Take(256)); } };
        ResourcesList.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await ViewModel.OpenAsync(); }
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { OnCopyClick(this, e); e.Handled = true; }
        };
        var initialized = false;
        Loaded += async (_, _) =>
        {
            if (!initialized) { initialized = true; await ViewModel.InitializeAsync(selectedId); }
            if (_pendingWorkspace is { } id) { _pendingWorkspace = null; SelectWorkspace(id); }
        };
    }
    public void SelectWorkspace(string id)
    {
        if (!IsLoaded) { _pendingWorkspace = id; return; }
        ViewModel.FocusWorkspace(id);
    }
    public Task FlushAsync() => ViewModel.FlushAsync();
    private async void OnSearchClick(object sender, RoutedEventArgs e) => await _services.Commands.ExecuteAsync(new("palette.toggle"));
    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = sender as Button ?? AddResourceButton };
        var files = new MenuItem { Header = "添加文件…" };
        files.Click += async (_, _) => { var picker = new OpenFileDialog { Multiselect = true, CheckFileExists = true, Title = "登记资源（保留原文件）" }; if (picker.ShowDialog(Window.GetWindow(this)) == true) await ViewModel.AddPathsAsync(picker.FileNames); };
        menu.Items.Add(files);
        foreach (var project in new[] { false, true })
        {
            var folder = new MenuItem { Header = project ? "添加项目目录…" : "添加文件夹…" };
            folder.Click += async (_, _) => { var picker = new OpenFolderDialog { Title = project ? "添加项目入口" : "添加文件夹入口" }; if (picker.ShowDialog(Window.GetWindow(this)) == true) await ViewModel.AddPathsAsync([picker.FolderName], project); };
            menu.Items.Add(folder);
        }
        menu.IsOpen = true;
    }
    private void EditCollection(CollectionDocument? collection)
    {
        var editor = new CollectionEditorWindow(ViewModel.Collections, _runtime.Library.Resources, collection) { Owner = Window.GetWindow(this) };
        editor.ShowDialog();
    }
    private void OnNewCollectionClick(object sender, RoutedEventArgs e) => EditCollection(null);
    private void OnEditCollectionClick(object sender, RoutedEventArgs e) { if (ViewModel.CurrentCollection is { } collection) EditCollection(collection); }
    private async void OnCardsClick(object sender, RoutedEventArgs e) => await ViewModel.SetViewModeAsync("cards");
    private async void OnListClick(object sender, RoutedEventArgs e) => await ViewModel.SetViewModeAsync("list");
    private void OnPreviousClick(object sender, RoutedEventArgs e) => ViewModel.MovePage(-1);
    private void OnNextClick(object sender, RoutedEventArgs e) => ViewModel.MovePage(1);
    private async void OnOpenClick(object sender, RoutedEventArgs e) => await ViewModel.OpenAsync();
    private async void OnRevealClick(object sender, RoutedEventArgs e) => await ViewModel.OpenAsync(true);
    private async void OnResourceDoubleClick(object sender, MouseButtonEventArgs e) => await ViewModel.OpenAsync();
    private void OnResourceSelectionChanged(object sender, SelectionChangedEventArgs e) { ViewModel.SetSelection(ResourcesList.SelectedItems.OfType<Mosaic.Core.Search.SearchItem>()); UpdateDetails(); }
    private async void OnFavoriteClick(object sender, RoutedEventArgs e) => await ViewModel.SaveMetadataAsync(true);
    private async void OnSaveTagsClick(object sender, RoutedEventArgs e) => await ViewModel.SaveMetadataAsync(false);
    private async void OnPinClick(object sender, RoutedEventArgs e) => await ViewModel.PinToDockAsync();
    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanReveal || ViewModel.Selected is null) return;
        try { Clipboard.SetText(ViewModel.Selected.Target); ViewModel.SetStatus("路径已复制"); }
        catch (System.Runtime.InteropServices.COMException) { ViewModel.SetStatus("剪贴板忙，请重试。", true); }
    }
    private async void OnAddToCollectionClick(object sender, RoutedEventArgs e)
    {
        if (CollectionTarget.SelectedItem is CollectionDocument collection) await ViewModel.SetCollectionMemberAsync(collection.Id, true);
        else ViewModel.SetStatus("请先选择或新建一个普通 / 项目集合。");
    }
    private async void OnRemoveFromCollectionClick(object sender, RoutedEventArgs e)
    { if (ViewModel.CurrentCollection is { } collection) await ViewModel.SetCollectionMemberAsync(collection.Id, false); }
    private async void OnAddToWorkspaceClick(object sender, RoutedEventArgs e)
    {
        if (WorkspaceTarget.SelectedItem is WorkspaceSummary workspace) await ViewModel.AddToWorkspaceAsync(workspace.Id);
        else ViewModel.SetStatus("请先选择一个工作空间。");
    }
    private async void OnRemoveFromWorkspaceClick(object sender, RoutedEventArgs e)
    {
        var id = ViewModel.SelectedNavigation?.Id;
        if (id == "current" || id?.StartsWith("workspace:", StringComparison.Ordinal) == true) await ViewModel.RemoveFromWorkspaceAsync();
        else ViewModel.SetStatus("请先在左侧选择当前工作空间或具体工作空间。");
    }
    private void OnDetailsClick(object sender, RoutedEventArgs e) { _details = !(_details ?? ActualWidth >= 1000); UpdateDetails(); }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateDetails();
    private void UpdateDetails()
    {
        if (DetailsColumn is null) return;
        var visible = (_details ?? ActualWidth >= 1000) && ViewModel.HasSelection;
        DetailsColumn.Width = new GridLength(visible ? Math.Min(266, ActualWidth * .33) : 0);
        DetailsPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }
    public void Dispose() => ViewModel.Dispose();
}
