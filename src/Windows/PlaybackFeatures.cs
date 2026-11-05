using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Pulsive;

public sealed partial class MainWindow
{
    readonly TextBlock nowPlayingText=new() { IsHitTestVisible=false,TextTrimming=TextTrimming.CharacterEllipsis,Text="Unknown",Effect=new DropShadowEffect { Color=Colors.Black,BlurRadius=5,ShadowDepth=1,Opacity=.65 } };
    readonly NowPlayingService nowPlayingService=new();
    readonly DispatcherTimer mediaTimer=new() { Interval=TimeSpan.FromSeconds(1) };
    readonly CancellationTokenSource playbackCancellation=new();
    NowPlayingInfo nowPlaying=NowPlayingInfo.Unknown;
    bool readingMedia,syncingNowPlaying;
    CheckBox nowPlayingToggle=null!;
    ComboBox nowPlayingPosition=null!,nowPlayingFormat=null!;
    Slider nowPlayingSize=null!,nowPlayingOpacity=null!;
    GlassColorPicker nowPlayingPicker=null!;
    Grid toolbarGrid=null!;
    bool fullscreen;
    Rect fullscreenRestore;
    WindowState fullscreenRestoreState;
    double fullscreenRestoreMaxHeight;

    void InitializePlaybackFeatures(Grid bar,StackPanel appearance)
    {
        toolbarGrid=bar;
        bar.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        bar.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        SizeChanged+=(_,_)=>UpdateCompactToolbar();
        IsVisibleChanged+=(_,_)=>lastShuffleBeat=audio.Beats.BeatCount;
        StateChanged+=(_,_)=>lastShuffleBeat=audio.Beats.BeatCount;
        root.Children.Insert(root.Children.IndexOf(toolbar),nowPlayingText);
        var content=new StackPanel();
        content.Children.Add(new TextBlock { Text="NOW PLAYING",FontSize=10,Foreground=AccentColor,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8) });
        nowPlayingToggle=new CheckBox { Content="Show now playing",IsChecked=prefs.ShowNowPlaying,Foreground=TextColor,FontSize=12,Margin=new Thickness(0,0,0,10) };
        nowPlayingToggle.Click+=(_,_)=> { prefs.ShowNowPlaying=nowPlayingToggle.IsChecked==true; UpdateNowPlaying(); Save(); };
        content.Children.Add(nowPlayingToggle);
        var options=new Grid(); for(int i=0;i<3;i++) options.ColumnDefinitions.Add(new ColumnDefinition());
        nowPlayingPosition=Select(["Bottom left","Bottom centre","Bottom right","Top left","Top centre","Top right"],Math.Clamp(prefs.NowPlayingPosition,0,5),double.NaN);
        nowPlayingFormat=Select(["Title · Artist","Title only","Artist — Title"],Math.Clamp(prefs.NowPlayingFormat,0,2),double.NaN);
        nowPlayingPicker=Picker(ParseColor(prefs.NowPlayingColor,Colors.White),"NOW PLAYING COLOUR");
        options.Children.Add(Labeled("POSITION",nowPlayingPosition,0)); options.Children.Add(Labeled("FORMAT",nowPlayingFormat,1)); options.Children.Add(Labeled("COLOUR",nowPlayingPicker,2)); content.Children.Add(options);
        var sizes=new Grid { Margin=new Thickness(0,10,0,0) }; sizes.ColumnDefinitions.Add(new ColumnDefinition()); sizes.ColumnDefinitions.Add(new ColumnDefinition());
        nowPlayingSize=Slider(10,48,Math.Clamp(prefs.NowPlayingSize,10,48),v=> { if(!syncingNowPlaying) { prefs.NowPlayingSize=v; UpdateNowPlaying(); } });
        nowPlayingOpacity=Slider(.2,1,Math.Clamp(prefs.NowPlayingOpacity,.2,1),v=> { if(!syncingNowPlaying) { prefs.NowPlayingOpacity=v; UpdateNowPlaying(); } });
        sizes.Children.Add(Labeled("TEXT SIZE",nowPlayingSize,0)); sizes.Children.Add(Labeled("OPACITY",nowPlayingOpacity,1)); content.Children.Add(sizes);
        content.Children.Add(new TextBlock { Text="Uses Windows media controls (for example Spotify and supported browsers). Unavailable titles show as Unknown.",FontSize=11,Foreground=MutedColor,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0) });
        appearance.Children.Add(new Border { Child=content,Margin=new Thickness(0,10,0,0),Padding=new Thickness(10),CornerRadius=new CornerRadius(16),Background=new SolidColorBrush(Color.FromArgb(5,255,255,255)),BorderBrush=new SolidColorBrush(Color.FromArgb(22,255,255,255)),BorderThickness=new Thickness(1) });
        nowPlayingPosition.SelectionChanged+=(_,_)=> { if(!syncingNowPlaying) { prefs.NowPlayingPosition=nowPlayingPosition.SelectedIndex; UpdateNowPlaying(); Save(); } };
        nowPlayingFormat.SelectionChanged+=(_,_)=> { if(!syncingNowPlaying) { prefs.NowPlayingFormat=nowPlayingFormat.SelectedIndex; UpdateNowPlaying(); Save(); } };
        nowPlayingPicker.ColorChanged+=(_,_)=> { if(!syncingNowPlaying) { prefs.NowPlayingColor=Hex(nowPlayingPicker.SelectedColor); UpdateNowPlaying(); Save(); } };
        mediaTimer.Tick+=async (_,_)=>await RefreshNowPlaying();
        UpdateNowPlaying(); RefreshShuffleList();
    }
    void UpdateCompactToolbar()
    {
        if(toolbarGrid==null||ActualWidth<1) return;
        bool compact=ActualWidth<590,hideBrand=ActualWidth<420;
        toolbar.Height=compact?76:34;
        toolbarGrid.Children[0].Visibility=hideBrand?Visibility.Collapsed:Visibility.Visible;
        Grid.SetColumn(modeSelect,hideBrand?0:1); Grid.SetColumnSpan(modeSelect,hideBrand?2:1);
        Grid.SetRow(toolbarPresets,compact?1:0); Grid.SetColumn(toolbarPresets,compact?0:2); Grid.SetColumnSpan(toolbarPresets,compact?3:1);
        toolbarPresets.Margin=compact?new Thickness(0,8,0,0):new Thickness(8,0,0,0);
        if(nowPlayingPosition!=null) PositionNowPlaying();
    }
    void PositionNowPlaying()
    {
        int position=Math.Clamp(prefs.NowPlayingPosition,0,5);
        nowPlayingText.HorizontalAlignment=(position%3) switch { 0=>HorizontalAlignment.Left,2=>HorizontalAlignment.Right,_=>HorizontalAlignment.Center };
        nowPlayingText.VerticalAlignment=position>=3?VerticalAlignment.Top:VerticalAlignment.Bottom;
        nowPlayingText.Margin=new Thickness(20,toolbar.Height+24,20,20);
        nowPlayingText.MaxWidth=Math.Max(1,ActualWidth-40);
    }
    void UpdateNowPlaying()
    {
        if(nowPlayingToggle==null) return;
        nowPlayingText.Visibility=prefs.ShowNowPlaying?Visibility.Visible:Visibility.Collapsed;
        nowPlayingText.Text=nowPlaying.Format(prefs.NowPlayingFormat);
        nowPlayingText.FontSize=Math.Clamp(prefs.NowPlayingSize,10,48);
        nowPlayingText.Opacity=Math.Clamp(prefs.NowPlayingOpacity,.2,1);
        nowPlayingText.Foreground=new SolidColorBrush(ParseColor(prefs.NowPlayingColor,Colors.White));
        PositionNowPlaying();
        foreach(var control in new Control[] { nowPlayingPosition,nowPlayingFormat,nowPlayingPicker,nowPlayingSize,nowPlayingOpacity }) { control.IsEnabled=prefs.ShowNowPlaying; control.Opacity=prefs.ShowNowPlaying?1:.4; }
        if(prefs.ShowNowPlaying) { mediaTimer.Start(); _=RefreshNowPlaying(); }
        else { mediaTimer.Stop(); nowPlaying=NowPlayingInfo.Unknown; }
    }
    void SyncNowPlayingSettings()
    {
        if(nowPlayingToggle==null) return;
        syncingNowPlaying=true;
        try
        {
            nowPlayingToggle.IsChecked=prefs.ShowNowPlaying;
            nowPlayingPosition.SelectedIndex=Math.Clamp(prefs.NowPlayingPosition,0,5); nowPlayingFormat.SelectedIndex=Math.Clamp(prefs.NowPlayingFormat,0,2);
            nowPlayingSize.Value=Math.Clamp(prefs.NowPlayingSize,10,48); nowPlayingOpacity.Value=Math.Clamp(prefs.NowPlayingOpacity,.2,1);
            nowPlayingPicker.SelectedColor=ParseColor(prefs.NowPlayingColor,Colors.White);
        }
        finally { syncingNowPlaying=false; }
        UpdateNowPlaying();
    }
    async Task RefreshNowPlaying()
    {
        if(readingMedia||closing||!prefs.ShowNowPlaying||App.IsUiTest) return;
        readingMedia=true;
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(playbackCancellation.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var info=await Task.Run(()=>nowPlayingService.ReadAsync(timeout.Token));
            if(!closing&&prefs.ShowNowPlaying) { nowPlaying=info; nowPlayingText.Text=info.Format(prefs.NowPlayingFormat); }
        }
        finally { readingMedia=false; }
    }
    void StopPlaybackFeatures()
    {
        mediaTimer.Stop(); playbackCancellation.Cancel(); playbackCancellation.Dispose(); ClearShuffleTransition();
    }
    void SetFullscreen(bool enabled)
    {
        if(enabled==fullscreen) return;
        var handle=new WindowInteropHelper(this).Handle;
        if(enabled)
        {
            var monitor=new FullscreenMonitor { Size=Marshal.SizeOf<FullscreenMonitor>() };
            if(!GetMonitorInfo(MonitorFromWindow(handle,2),ref monitor)) return;
            fullscreenRestoreState=WindowState; fullscreenRestore=WindowState==WindowState.Normal?new Rect(Left,Top,Width,Height):RestoreBounds;
            fullscreenRestoreMaxHeight=MaxHeight;
            fullscreen=true; WindowState=WindowState.Normal; MaxHeight=double.PositiveInfinity;
            var transform=PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice;
            Width=(monitor.Right-monitor.Left)*transform.M11; Height=(monitor.Bottom-monitor.Top)*transform.M22;
            // Native coordinates avoid virtual-desktop / mixed-DPI origin rounding.
            SetFullscreenWindowPos(handle,IntPtr.Zero,monitor.Left,monitor.Top,monitor.Right-monitor.Left,monitor.Bottom-monitor.Top,0x0024);
        }
        else
        {
            fullscreen=false; MaxHeight=fullscreenRestoreMaxHeight;
            Left=fullscreenRestore.Left; Top=fullscreenRestore.Top; Width=fullscreenRestore.Width; Height=fullscreenRestore.Height; WindowState=fullscreenRestoreState;
        }
        visualBackdrop.CornerRadius=solidBackdrop.CornerRadius=new CornerRadius(enabled?0:24);
        overlayBorder.Visibility=enabled?Visibility.Collapsed:Visibility.Visible;
        if(enabled) outerGlow?.Hide();
        UpdateHoverControls(); backdrops.RefreshNative();
    }
    [StructLayout(LayoutKind.Sequential)] struct FullscreenMonitor
    {
        public int Size,Left,Top,Right,Bottom,WorkLeft,WorkTop,WorkRight,WorkBottom,Flags;
    }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr handle,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr monitor,ref FullscreenMonitor info);
    [DllImport("user32.dll",EntryPoint="SetWindowPos")] static extern bool SetFullscreenWindowPos(IntPtr handle,IntPtr after,int x,int y,int width,int height,uint flags);
}
