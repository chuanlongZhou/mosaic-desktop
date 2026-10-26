using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Mosaic.PetWorldDemo;

public partial class WorldWindow : Window
{
    public WorldDirector World { get; } = new();
    public LibraryScene Scene { get; }
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<(Button Button, TextBlock Activity)> _roster = [];
    private double _last, _lastRefresh;
    private bool _updating, _immersive;
    private WindowState _previousWindowState;
    public WorldWindow()
    {
        InitializeComponent();Scene=new(World);SceneBox.Child=Scene;
        ChapterPicker.ItemsSource=WorldDirector.Chapters;ChapterPicker.SelectedIndex=0;
        foreach(var actor in World.Actors)
        {
            var panel=new StackPanel();
            panel.Children.Add(new TextBlock{Text=actor.Name,FontSize=14,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(actor.Color))});
            var activity=new TextBlock{FontSize=11,Foreground=new SolidColorBrush(Color.FromRgb(183,195,174)),Margin=new(0,5,0,0),TextWrapping=TextWrapping.Wrap};panel.Children.Add(activity);
            var button=new Button{Content=panel,HorizontalContentAlignment=HorizontalAlignment.Left,Padding=new(12,9,12,9),Margin=new(0,0,0,7)};
            button.Click+=(_,_)=>{World.SelectedActor=actor.Id;RefreshUi();Scene.InvalidateVisual();};Roster.Children.Add(button);_roster.Add((button,activity));
        }
        Scene.SelectionChanged+=RefreshUi;
        _timer.Tick+=(_,_)=>
        {
            var now=_clock.Elapsed.TotalSeconds;var elapsed=Math.Min(.15,now-_last);_last=now;
            // An invisible/minimized preview has no continuous rendering cost and cannot catch up in a burst.
            if(WindowState==WindowState.Minimized||!IsVisible)return;
            World.Advance(elapsed);Scene.InvalidateVisual();
            if(now-_lastRefresh>.15){RefreshUi();_lastRefresh=now;}
        };
        Loaded+=(_,_)=>{_last=_clock.Elapsed.TotalSeconds;_timer.Start();RefreshUi();};
        Closed+=(_,_)=>_timer.Stop();
        RefreshUi();
    }
    private void RefreshUi()
    {
        _updating=true;ChapterPicker.SelectedIndex=World.Current.Chapter;_updating=false;
        ChapterLabel.Text=$"{WorldDirector.Chapters[World.Current.Chapter]}  ·  {World.BeatIndex+1}/{World.Story.Count}";
        SummaryLabel.Text=World.Gathering?"她们正绕过家具，去下一个活动点……":World.Summary;
        PauseButton.Content=World.Paused?"▶  继续":"Ⅱ  暂停";
        for(var i=0;i<_roster.Count;i++)
        {
            var actor=World.Actors[i];_roster[i].Activity.Text=actor.Dragging?"被你轻轻拎起":actor.Activity;
            _roster[i].Button.Background=new SolidColorBrush(i==World.SelectedActor?Color.FromRgb(66,85,67):Color.FromRgb(45,62,49));
        }
        SelectedTitle.Text=$"和 {World.Actors[World.SelectedActor].Name} 互动";
        RecentLines.Text=string.Join("\n\n",World.Journal.Take(2));
        CoverageLabel.Text=$"已见姿态 {World.Actors.Sum(a=>a.SeenPoses.Count)}/145 · 拖动和摸头也会被记录";
    }
    private void OnPause(object sender,RoutedEventArgs e){World.Paused=!World.Paused;RefreshUi();}
    private void OnNext(object sender,RoutedEventArgs e){World.NextBeat();RefreshUi();Scene.InvalidateVisual();}
    private void OnSpeed(object sender,RoutedEventArgs e){World.Speed=World.Speed==1?2:World.Speed==2?4:1;SpeedButton.Content=$"{World.Speed}× 节奏";}
    private void OnChapterChanged(object sender,SelectionChangedEventArgs e){if(!_updating&&ChapterPicker.SelectedIndex>=0){World.JumpChapter(ChapterPicker.SelectedIndex);if(Scene is not null)Scene.InvalidateVisual();}}
    private void React(SceneAction action){World.Interact(World.SelectedActor,action);RefreshUi();Scene.InvalidateVisual();}
    private void OnPat(object sender,RoutedEventArgs e)=>React(SceneAction.Pat);
    private void OnFeed(object sender,RoutedEventArgs e)=>React(SceneAction.Eat);
    private void OnGreet(object sender,RoutedEventArgs e)=>React(SceneAction.Greet);
    private void OnToken(object sender,RoutedEventArgs e)=>React(SceneAction.TokenOops);
    private void OnImmersive(object sender,RoutedEventArgs e)=>ToggleImmersive();
    private void ToggleImmersive()
    {
        _immersive=!_immersive;
        if(_immersive){_previousWindowState=WindowState;WindowState=WindowState.Normal;WindowStyle=WindowStyle.None;WindowState=WindowState.Maximized;}
        else{WindowState=WindowState.Normal;WindowStyle=WindowStyle.SingleBorderWindow;WindowState=_previousWindowState;}
        Header.Visibility=Footer.Visibility=Aside.Visibility=_immersive?Visibility.Collapsed:Visibility.Visible;
        AsideColumn.Width=new GridLength(_immersive?0:286);ExitHint.Visibility=_immersive?Visibility.Visible:Visibility.Collapsed;
    }
    private void OnKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape&&_immersive){ToggleImmersive();e.Handled=true;}
        else if(e.Key==Key.Space&&Keyboard.FocusedElement is not ComboBox){World.Paused=!World.Paused;RefreshUi();e.Handled=true;}
        else if(e.Key==Key.Right&&Keyboard.FocusedElement is not ComboBox){World.NextBeat();RefreshUi();e.Handled=true;}
    }
}
