using System.Windows;
using System.Windows.Controls;
using Mosaic.Host.Services;
using Mosaic.Host.ViewModels;

namespace Mosaic.Host.Views;

public partial class WorkspacesView : UserControl, IDisposable
{
    public WorkspacesViewModel ViewModel { get; }
    public WorkspacesView(AppServices services)
    {
        ViewModel = new(services); InitializeComponent(); DataContext = ViewModel;
    }
    private async void OnSwitchClick(object sender, RoutedEventArgs e) => await ViewModel.SwitchAsync();
    private async void OnRenameClick(object sender, RoutedEventArgs e) => await ViewModel.RenameAsync();
    private async void OnResourcesClick(object sender, RoutedEventArgs e) => await ViewModel.OpenResourcesAsync();
    private async void OnCloneClick(object sender, RoutedEventArgs e) => await ViewModel.CreateAsync("clone");
    private async void OnAiClick(object sender, RoutedEventArgs e) => await ViewModel.CreateAsync("ai");
    private async void OnWritingClick(object sender, RoutedEventArgs e) => await ViewModel.CreateAsync("writing");
    public Task FlushAsync() => ViewModel.FlushAsync();
    public void Dispose() => ViewModel.Dispose();
}
