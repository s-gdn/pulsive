using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Pulsive;

public sealed partial class MainWindow
{
    readonly ManualTempoClock manualTempo=new();
    long lastManualBeat;
    TextBox manualBpmInput=null!;
    Button manualTapButton=null!;
    Slider shuffleHoldSlider=null!,shuffleFadeSlider=null!;
    bool syncingShuffle;
    Action refreshShuffleTiming=()=>{};
    void NormalizeShuffleSettings()
    {
        prefs.ExcludedShufflePresets??=[];
        prefs.ShuffleTimingMode=Math.Clamp(prefs.ShuffleTimingMode,0,2); prefs.ShuffleBeats=Math.Clamp(prefs.ShuffleBeats,1,256);
        prefs.ShuffleInterval=double.IsFinite(prefs.ShuffleInterval)?Math.Clamp(prefs.ShuffleInterval,3,600):30;
        prefs.ShuffleTransition=double.IsFinite(prefs.ShuffleTransition)?Math.Clamp(prefs.ShuffleTransition,0,8):1.5;
        prefs.ManualBpm=double.IsFinite(prefs.ManualBpm)?Math.Clamp(prefs.ManualBpm,20,400):120;
    }
    void BuildManualTempoControls(StackPanel page,int index)
    {
        var row=new Grid { Margin=new Thickness(0,0,0,12) }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        manualBpmInput=new TextBox { Text=prefs.ManualBpm.ToString("0.##",CultureInfo.InvariantCulture),Height=32,MaxLength=7,Foreground=TextColor,CaretBrush=TextColor,Background=Brushes.Transparent,BorderThickness=new Thickness(0),VerticalContentAlignment=VerticalAlignment.Center,ToolTip="Enter BPM from 20 to 400",Padding=new Thickness(10,0,10,0) };
        var host=new FrameworkElementFactory(typeof(ScrollViewer)); host.Name="PART_ContentHost"; host.SetValue(FrameworkElement.MarginProperty,new Thickness(10,0,10,0));
        host.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        manualBpmInput.Template=new ControlTemplate(typeof(TextBox)) { VisualTree=host };
        var glass=new BackdropSurface { Child=manualBpmInput,CornerRadius=new CornerRadius(15),Adaptive=false,TintOpacity=.1,HoverHighlight=false };
        row.Children.Add(Labeled("MANUAL BPM",glass,0));
        var tap=new Button { Content="Tap BPM",Height=32,Width=110,FontSize=12,Margin=new Thickness(10,0,0,0),VerticalAlignment=VerticalAlignment.Bottom,ToolTip="Tap once on each beat. The first tap starts a new synced beat grid; tap again to measure BPM." };
        manualTapButton=tap;
        Grid.SetColumn(tap,1); row.Children.Add(tap); page.Children.Insert(index,row);
        manualTempo.SetBpm(prefs.ManualBpm,watch.Elapsed.TotalSeconds);
        manualBpmInput.TextChanged+=(_,_)=>
        {
            if(syncingShuffle) return;
            bool valid=double.TryParse(manualBpmInput.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out double bpm)||double.TryParse(manualBpmInput.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out bpm);
            if(!valid||!double.IsFinite(bpm)||bpm<20||bpm>400) { manualBpmInput.Foreground=Brushes.Salmon; return; }
            manualBpmInput.Foreground=TextColor; prefs.ManualBpm=bpm; manualTempo.SetBpm(bpm,watch.Elapsed.TotalSeconds); lastManualBeat=0; shuffleBeatElapsed=0; Save();
        };
        tap.Click+=(_,_)=>
        {
            if(prefs.ShuffleTimingMode!=2) shuffleTimingSelect.SelectedIndex=2;
            double now=watch.Elapsed.TotalSeconds;
            bool first=manualTempo.Tap(now);
            if(first) { shuffleBeatElapsed=0; lastManualBeat=0; }
            prefs.ManualBpm=manualTempo.Bpm;
            syncingShuffle=true; try { manualBpmInput.Text=prefs.ManualBpm.ToString("0.##",CultureInfo.InvariantCulture); } finally { syncingShuffle=false; }
            UpdateShuffle(1d/60); Save();
        };
        var previous=refreshShuffleTiming;
        refreshShuffleTiming=()=> { previous(); row.Visibility=prefs.ShuffleTimingMode==2?Visibility.Visible:Visibility.Collapsed; };
    }
    void SyncShuffleSettings()
    {
        if(shuffleToggle==null||manualBpmInput==null) return;
        syncingShuffle=true;
        try
        {
            NormalizeShuffleSettings();
            shuffleToggle.IsChecked=prefs.ShuffleEnabled;
            shuffleTimingSelect.SelectedIndex=Math.Clamp(prefs.ShuffleTimingMode,0,2);
            shuffleHoldSlider.Value=Math.Clamp(prefs.ShuffleInterval,3,600); shuffleFadeSlider.Value=Math.Clamp(prefs.ShuffleTransition,0,8);
            shuffleBeatSlider.Value=Math.Clamp(prefs.ShuffleBeats,1,256);
            manualBpmInput.Text=prefs.ManualBpm.ToString("0.##",CultureInfo.InvariantCulture); manualBpmInput.Foreground=TextColor;
            manualTempo.SetBpm(prefs.ManualBpm,watch.Elapsed.TotalSeconds);
            RefreshShuffleList(); refreshShuffleTiming(); ResetShuffle();
        }
        finally { syncingShuffle=false; }
    }
}
