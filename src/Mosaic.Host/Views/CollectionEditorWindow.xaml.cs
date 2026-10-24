using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Mosaic.Core.Models;
using Mosaic.Core.Search;
using Mosaic.Host.Services;
using Mosaic.Host.ViewModels;
using Mosaic.Storage.Json;

namespace Mosaic.Host.Views;

public partial class CollectionEditorWindow : Window
{
    private readonly CollectionRepository _repository;
    private readonly IReadOnlyList<SearchItem> _resources;
    private readonly CollectionDocument _original;
    public bool Saved { get; private set; }
    public CollectionEditorWindow(CollectionRepository repository, IReadOnlyList<SearchItem> resources, CollectionDocument? original = null)
    {
        _repository = repository; _resources = resources; _original = original is null ? new() : repository.Copy(original);
        InitializeComponent();
        Heading.Text = original is null ? "新建资源集合" : "编辑集合";
        KindInput.ItemsSource = new[] { new KindOption(CollectionKind.Static, "普通集合 · 手动成员"), new KindOption(CollectionKind.Smart, "智能集合 · 条件匹配"), new KindOption(CollectionKind.Project, "项目集合 · 目录与关联资源") };
        KindInput.SelectedIndex = (int)_original.Kind;
        TypeInput.ItemsSource = new[] { new ResourceTypeOption(null, "不限类型") }.Concat(Enum.GetValues<SearchKind>().Select(x => new ResourceTypeOption(x, new SearchItem("", x, "", "", "").TypeLabel))).ToArray();
        TypeInput.SelectedItem = ((IEnumerable<ResourceTypeOption>)TypeInput.ItemsSource).First(x => x.Kind == _original.Rule.Type);
        NameInput.Text = original?.Name ?? ""; TagInput.Text = _original.Rule.Tag; RootInput.Text = _original.Rule.Root; RuleQueryInput.Text = _original.Rule.Text;
        AnyRuleCheck.IsChecked = _original.Rule.MatchAny;
        SourceInitialized += (_, _) => NativeShell.StyleWindow(new WindowInteropHelper(this).Handle, false, false);
        Height = Math.Min(610, SystemParameters.WorkArea.Height - 48);
    }
    private CollectionDocument Candidate()
    {
        var candidate = _repository.Copy(_original);
        candidate.Name = NameInput.Text.Trim(); candidate.Kind = ((KindOption)KindInput.SelectedItem).Kind;
        if (candidate.Kind == CollectionKind.Smart) candidate.ResourceIds.Clear();
        candidate.Rule = new() { Type = (TypeInput.SelectedItem as ResourceTypeOption)?.Kind, Tag = TagInput.Text.Trim(), Root = RootInput.Text.Trim(), Text = RuleQueryInput.Text.Trim(), MatchAny = AnyRuleCheck.IsChecked == true };
        candidate.Validate(); return candidate;
    }
    private void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RulePanel is null || KindInput.SelectedItem is not KindOption option) return;
        RulePanel.Visibility = option.Kind == CollectionKind.Static ? Visibility.Collapsed : Visibility.Visible;
        RuleHelp.Text = option.Kind switch { CollectionKind.Smart => "条件同时满足才匹配。规则仅查询已登记资源；保存为智能集合会以规则替代手动成员。", CollectionKind.Project => "自动包含目录下的已登记资源，也可手动关联应用与模型。项目集合只使用目录条件。", _ => "通过资源详情把所选资源加入集合；同一资源可属于多个集合。" };
        TypeInput.IsEnabled = TagInput.IsEnabled = RuleQueryInput.IsEnabled = option.Kind == CollectionKind.Smart;
        AnyRuleCheck.IsEnabled = option.Kind == CollectionKind.Smart;
    }
    private void OnPreviewClick(object sender, RoutedEventArgs e)
    {
        try { var candidate = Candidate(); CountLabel.Text = $"当前已登记资源中匹配 {_resources.Count(candidate.Includes):N0} 项"; ErrorLabel.Text = ""; }
        catch (Exception ex) { ErrorLabel.Text = ex.Message; }
    }
    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = EditorBody.IsEnabled = false;
        try { await _repository.SaveAsync(Candidate()); Saved = true; Close(); }
        catch (Exception ex) { ErrorLabel.Text = "未保存：" + ex.Message; }
        finally { SaveButton.IsEnabled = EditorBody.IsEnabled = true; }
    }
    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
    private sealed record KindOption(CollectionKind Kind, string Label);
}
