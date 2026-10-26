using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Mosaic.Core;
using Mosaic.Core.Models;

namespace Mosaic.Host.Controls;

public partial class DesktopPetSettingsPanel : UserControl
{
    private sealed record Choice(string Id, string Name);
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
    private DesktopPetBehavior _preview = new();
    private DesktopPetSettings _draft = new();
    private string _selectedId = DesktopPetSettings.PrimaryId;
    private readonly TranslateTransform _bob = new();
    private bool _loading, _sleepPreview;
    private long _typingPreviewUntil;
    public event Action? Changed;
    public DesktopPetSettingsPanel()
    {
        InitializeComponent(); PreviewSprite.RenderTransform = _bob;
        CharacterPicker.ItemsSource = DesktopPetCharacters.All;
        _timer.Tick += (_, _) => PreviewTick();
        IsVisibleChanged += (_, _) => { if (IsVisible) _timer.Start(); else _timer.Stop(); };
        Unloaded += (_, _) => _timer.Stop();
    }
    public void Load(DesktopPetSettings settings)
    {
        _loading = true; _draft = settings.Copy(); EnabledCheck.IsChecked = settings.Enabled;
        RefreshInstances(DesktopPetSettings.PrimaryId); LoadSelected(); _loading = false;
    }
    public void ApplyTo(DesktopPetSettings settings)
    {
        SaveSelected(); _draft.Enabled = EnabledCheck.IsChecked == true; _draft.Validate(); _draft.CopyAllTo(settings);
    }
    private void RefreshInstances(string selected)
    {
        var wasLoading = _loading; _loading = true;
        InstancePicker.ItemsSource = _draft.Slots().Select((slot, index) => new Choice(slot.Id, $"{index + 1} · {DesktopPetCharacters.Get(slot.Options.Character).Name}" + (slot.Visible ? "" : "（已收起）"))).ToList();
        InstancePicker.SelectedValue = selected; _selectedId = selected;
        PetCountLabel.Text = $"同伴名册 · {1 + _draft.Companions.Count} / {DesktopPetSettings.MaximumPets}";
        AddPetButton.IsEnabled = _draft.Companions.Count < DesktopPetSettings.MaximumPets - 1;
        RemovePetButton.IsEnabled = selected != DesktopPetSettings.PrimaryId; _loading = wasLoading;
    }
    private void LoadSelected()
    {
        var wasLoading = _loading; _loading = true;
        var settings = _draft.GetPet(_selectedId);
        CharacterPicker.SelectedValue = settings.Character;
        VisibleCheck.IsChecked = _selectedId == DesktopPetSettings.PrimaryId ? _draft.PrimaryVisible : settings.Enabled;
        CursorCheck.IsChecked = settings.FollowCursor; EnterCheck.IsChecked = settings.ReactToEnter;
        DesktopCheck.IsChecked = settings.ReactToDesktop; TypingCheck.IsChecked = settings.ReactToTyping;
        CloseCheck.IsChecked = settings.ReactToAppClose; IdleCheck.IsChecked = settings.SleepWhenIdle;
        BubbleCheck.IsChecked = settings.ShowBubbles; FullscreenCheck.IsChecked = settings.HideInFullscreen;
        IdleSlider.Maximum = Math.Max(600, settings.IdleSeconds); IdleSlider.Value = settings.IdleSeconds; SizeSlider.Value = settings.Size;
        PositionLabel.Text = "这位伙伴的拖动位置会独立保存在本机。";
        UpdateCharacter(); UpdateLabels(); _loading = wasLoading;
    }
    private void SaveSelected()
    {
        var settings = _draft.GetPet(_selectedId);
        settings.Character = CharacterPicker.SelectedValue is PetCharacter character ? character : PetCharacter.WhaleGirl;
        if (_selectedId == DesktopPetSettings.PrimaryId) _draft.PrimaryVisible = VisibleCheck.IsChecked == true;
        else settings.Enabled = VisibleCheck.IsChecked == true;
        settings.FollowCursor = CursorCheck.IsChecked == true; settings.ReactToEnter = EnterCheck.IsChecked == true;
        settings.ReactToDesktop = DesktopCheck.IsChecked == true; settings.ReactToTyping = TypingCheck.IsChecked == true;
        settings.ReactToAppClose = CloseCheck.IsChecked == true; settings.SleepWhenIdle = IdleCheck.IsChecked == true;
        settings.ShowBubbles = BubbleCheck.IsChecked == true; settings.HideInFullscreen = FullscreenCheck.IsChecked == true;
        settings.IdleSeconds = (int)IdleSlider.Value; settings.Size = (int)SizeSlider.Value;
    }
    private void OnInstanceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || InstancePicker.SelectedValue is not string id) return;
        SaveSelected(); _selectedId = id; LoadSelected(); RemovePetButton.IsEnabled = id != DesktopPetSettings.PrimaryId;
    }
    private void OnAddPet(object sender, RoutedEventArgs e)
    {
        if (_draft.Companions.Count >= DesktopPetSettings.MaximumPets - 1) return;
        SaveSelected(); var companion = new DesktopPetCompanion { Character = PetCharacter.Gpt };
        _draft.Companions.Add(companion); RefreshInstances(companion.Id); LoadSelected(); Changed?.Invoke();
    }
    private void OnRemovePet(object sender, RoutedEventArgs e)
    {
        if (_selectedId == DesktopPetSettings.PrimaryId) return;
        _draft.Companions.RemoveAll(pet => pet.Id == _selectedId);
        RefreshInstances(DesktopPetSettings.PrimaryId); LoadSelected(); Changed?.Invoke();
    }
    private void OnVisibleChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        SaveSelected(); RefreshInstances(_selectedId); Changed?.Invoke();
    }
    private void OnCharacterChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCharacter(); UpdateLabels();
        if (!_loading && CharacterPicker.SelectedValue is PetCharacter character)
        {
            _draft.GetPet(_selectedId).Character = character; RefreshInstances(_selectedId); Changed?.Invoke();
        }
    }
    private void UpdateCharacter()
    {
        if (CharacterPicker.SelectedValue is not PetCharacter character) return;
        var info = DesktopPetCharacters.Get(character); PreviewSprite.SetCharacter(character);
        CharacterName.Text = info.Name; CharacterDescription.Text = info.Description; CharacterTagline.Text = info.Tagline;
        FeedPreviewButton.Content = info.FeedLabel; SignaturePreviewButton.Content = info.SignatureLabel; TeasePreviewButton.Content = info.TeaseLabel;
        System.Windows.Automation.AutomationProperties.SetName(PreviewSprite, info.Name + "动作预览");
        _preview = new(character: character); _sleepPreview = false; _typingPreviewUntil = 0; PreviewTick();
    }
    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { UpdateLabels(); if (!_loading) Changed?.Invoke(); }
    private void UpdateLabels()
    {
        if (IdleLabel is not null && IdleSlider is not null) IdleLabel.Text = $"不操作 {IdleSlider.Value:0} 秒后入睡";
        if (SizeLabel is not null && SizeSlider is not null)
        {
            var multiplier = CharacterPicker?.SelectedValue is PetCharacter character ? DesktopPetCharacters.Get(character).SizeMultiplier : 1;
            SizeLabel.Text = $"桌宠大小 · {SizeSlider.Value * multiplier:0}" + (multiplier == 2 ? "（正常比例 ×2）" : "");
        }
    }
    private void OnResetPosition(object sender, RoutedEventArgs e)
    {
        SaveSelected(); var settings = _draft.GetPet(_selectedId); settings.Left = settings.Top = null;
        PositionLabel.Text = "保存后，这位伙伴会回到默认排列位置。"; Changed?.Invoke();
    }
    private void OnPreview(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        if (tag != "Click") _preview = new(character: PreviewSprite.Character);
        _sleepPreview = tag == "Sleep";
        var now = Environment.TickCount64; _typingPreviewUntil = tag == "Typing" ? now + 4000 : 0;
        if (_sleepPreview) _preview.Tick(now, 1000, true, 90, false, 0, 0);
        else if (Enum.TryParse<PetTrigger>(tag, out var action)) _preview.Trigger(action, now);
        PreviewTick();
    }
    private void PreviewTick()
    {
        var now = Environment.TickCount64;
        if (now < _typingPreviewUntil) _preview.Trigger(PetTrigger.Typing, now);
        _preview.Tick(now, _sleepPreview ? 1000 : 0, _sleepPreview, 90, false, 0, 0);
        var motion = Services.ThemeManager.Current.AnimationsEnabled;
        PreviewSprite.SetPose(!motion && _preview.IsExpression ? _preview.StillPose : !motion && _preview.IsTyping ? PetPose.TypingLeft : !motion && _preview.IsGreeting ? PetPose.GreetA : !motion && _preview.Pose == PetPose.RiceHatTilt ? PetPose.RiceHat : _preview.Pose);
        _bob.Y = motion && !_preview.IsTyping && !_preview.IsExpression ? _preview.Pose == PetPose.Cheer ? -10 * Math.Abs(Math.Sin(now / 100d)) : Math.Sin(now / 900d) * 2 : 0;
        _timer.Interval = TimeSpan.FromMilliseconds(!motion ? 200 : _preview.IsTyping ? 130 : 100);
        var bubble = !motion ? _preview.StillBubble : _preview.Bubble;
        PreviewCaption.Text = bubble.Length > 0 ? bubble : "动作预览，不会改变桌宠开关。";
    }
}
