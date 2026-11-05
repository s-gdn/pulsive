using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pulsive;

internal sealed class ShuffleOption : INotifyPropertyChanged
{
    public string Id { get; init; }="";
    public string Name { get; init; }="";
    public string Group { get; init; }="";
    public string GroupKey { get; init; }="";
    bool included=true;
    public bool IsIncluded { get=>included; set { if(included==value) return; included=value; PropertyChanged?.Invoke(this,new(nameof(IsIncluded))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed partial class MainWindow
{
    readonly ShuffleCycle shuffleCycle=new();
    readonly Image shuffleSnapshot=new() { IsHitTestVisible=false,Stretch=Stretch.Fill,Visibility=Visibility.Collapsed };
    List<ShuffleOption> shuffleOptions=[];
    ListBox shuffleList=null!;
    CheckBox shuffleToggle=null!;
    TextBlock shuffleCount=null!;
    Button selectShuffleButton=null!;
    Button shuffleSettingsTab=null!;
    ComboBox shuffleTimingSelect=null!;
    Slider shuffleBeatSlider=null!;
    TextBlock shuffleTempoStatus=null!;
    long lastShuffleBeat;
    int shuffleBeatElapsed;
    bool shuffleApplying,batchingShuffle;
    double shuffleElapsed,transitionElapsed;
    double activeTransitionDuration;
    bool transitioning;
    string? shuffledEntry;

    StackPanel BuildShufflePage()
    {
        prefs.ExcludedShufflePresets??=[];
        prefs.ShuffleInterval=double.IsFinite(prefs.ShuffleInterval)?Math.Clamp(prefs.ShuffleInterval,3,600):30;
        prefs.ShuffleTransition=double.IsFinite(prefs.ShuffleTransition)?Math.Clamp(prefs.ShuffleTransition,0,8):1.5;
        prefs.ShuffleTimingMode=Math.Clamp(prefs.ShuffleTimingMode,0,2);
        prefs.ManualBpm=double.IsFinite(prefs.ManualBpm)?Math.Clamp(prefs.ManualBpm,20,400):120;
        prefs.ShuffleBeats=Math.Clamp(prefs.ShuffleBeats,1,256);
        var page=new StackPanel { Visibility=Visibility.Collapsed };
        page.Children.Add(new TextBlock { Text="SHUFFLE",FontSize=10,FontWeight=FontWeights.SemiBold,Foreground=AccentColor,Margin=new Thickness(0,0,0,8) });
        shuffleToggle=new CheckBox { Content="Shuffle visualisers and presets",IsChecked=prefs.ShuffleEnabled,Foreground=TextColor,FontSize=12,Margin=new Thickness(0,0,0,10) };
        shuffleToggle.Click+=(_,_)=>SetShuffleEnabled(shuffleToggle.IsChecked==true);
        page.Children.Add(shuffleToggle);
        page.Children.Add(new TextBlock { Text="All styles and saved presets are included by default. Uncheck entries to leave them out. Saved presets also apply their appearance settings.",Foreground=MutedColor,FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12) });
        var times=new Grid { Margin=new Thickness(0,0,0,12) }; times.ColumnDefinitions.Add(new ColumnDefinition()); times.ColumnDefinitions.Add(new ColumnDefinition());
        var hold=Slider(3,600,prefs.ShuffleInterval,v=> { prefs.ShuffleInterval=v; shuffleElapsed=0; });
        var fade=Slider(0,8,prefs.ShuffleTransition,v=>prefs.ShuffleTransition=v);
        shuffleHoldSlider=hold; shuffleFadeSlider=fade;
        var holdGroup=(StackPanel)Labeled($"TIME BETWEEN TRANSITIONS · {prefs.ShuffleInterval:0} s",hold,0);
        var fadeGroup=(StackPanel)Labeled($"TRANSITION DURATION · {prefs.ShuffleTransition:0.0} s",fade,1);
        hold.ValueChanged+=(_,e)=>((TextBlock)holdGroup.Children[0]).Text=$"TIME BETWEEN TRANSITIONS · {e.NewValue:0} s";
        fade.ValueChanged+=(_,e)=>((TextBlock)fadeGroup.Children[0]).Text=$"TRANSITION DURATION · {e.NewValue:0.0} s";
        times.Children.Add(holdGroup); times.Children.Add(fadeGroup); page.Children.Add(times);
        var beatTiming=new Grid { Margin=new Thickness(0,0,0,12) }; beatTiming.ColumnDefinitions.Add(new ColumnDefinition()); beatTiming.ColumnDefinitions.Add(new ColumnDefinition());
        shuffleTimingSelect=Select(["Seconds","Beats (automatic BPM, beta)","BPM (manual entry / tap)"],prefs.ShuffleTimingMode,double.NaN);
        beatTiming.Children.Add(Labeled("TRANSITION TIMING",shuffleTimingSelect,0));
        shuffleBeatSlider=Slider(1,256,prefs.ShuffleBeats,v=> { prefs.ShuffleBeats=(int)Math.Round(v); shuffleBeatElapsed=0; });
        shuffleBeatSlider.IsSnapToTickEnabled=true; shuffleBeatSlider.TickFrequency=1;
        var beatGroup=(StackPanel)Labeled($"BEATS BETWEEN TRANSITIONS · {prefs.ShuffleBeats}",shuffleBeatSlider,1);
        shuffleBeatSlider.ValueChanged+=(_,_)=>((TextBlock)beatGroup.Children[0]).Text=$"BEATS BETWEEN TRANSITIONS · {prefs.ShuffleBeats}";
        beatTiming.Children.Add(beatGroup); page.Children.Insert(page.Children.IndexOf(times),beatTiming);
        shuffleTempoStatus=new TextBlock { FontSize=11,Foreground=MutedColor,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12) };
        page.Children.Insert(page.Children.IndexOf(times)+1,shuffleTempoStatus);
        void UpdateTiming()
        {
            bool beats=prefs.ShuffleTimingMode!=0;
            holdGroup.IsEnabled=!beats; holdGroup.Opacity=beats?.35:1;
            beatGroup.IsEnabled=beats; beatGroup.Opacity=beats?1:.35;
            shuffleTempoStatus.Visibility=beats?Visibility.Visible:Visibility.Collapsed;
            UpdateShuffleTempoStatus();
        }
        refreshShuffleTiming=UpdateTiming;
        BuildManualTempoControls(page,page.Children.IndexOf(times));
        shuffleTimingSelect.SelectionChanged+=(_,_)=> { if(syncingShuffle) return; prefs.ShuffleTimingMode=Math.Clamp(shuffleTimingSelect.SelectedIndex,0,2); ResetShuffle(); refreshShuffleTiming(); Save(); };
        refreshShuffleTiming();
        var actions=new DockPanel { Margin=new Thickness(0,0,0,10) };
        selectShuffleButton=new Button { Content="Unselect all",Width=130,Height=32,FontSize=12 };
        selectShuffleButton.Click+=(_,_)=>
        {
            bool include=!shuffleOptions.All(option=>option.IsIncluded);
            batchingShuffle=true;
            try { foreach(var option in shuffleOptions) option.IsIncluded=include; }
            finally { batchingShuffle=false; }
            SaveShuffleSelection();
        };
        actions.Children.Add(selectShuffleButton);
        shuffleCount=new TextBlock { Foreground=MutedColor,FontSize=11,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right };
        actions.Children.Add(shuffleCount); page.Children.Add(actions);
        shuffleList=new ListBox { Height=270,Background=Brushes.Transparent,Foreground=TextColor,BorderBrush=new SolidColorBrush(Color.FromArgb(40,255,255,255)),BorderThickness=new Thickness(1),Padding=new Thickness(6),FocusVisualStyle=null,Template=milkdropList.Template,ItemContainerStyle=milkdropList.ItemContainerStyle };
        VirtualizingPanel.SetIsVirtualizing(shuffleList,true); VirtualizingPanel.SetVirtualizationMode(shuffleList,VirtualizationMode.Recycling); VirtualizingPanel.SetScrollUnit(shuffleList,ScrollUnit.Pixel);
        shuffleList.ItemTemplate=CollectionChoiceTemplate();
        shuffleCollectionList=CreateSidebar(CollectionChoiceTemplate(),270);
        shuffleCollectionList.SelectionChanged+=(_,_)=> {
            if(syncingShuffleCollections||shuffleCollectionList.SelectedItem is not CollectionChoiceRow row) return;
            selectedShuffleCollection=row.Key; RefreshShuffleRows();
        };
        page.Children.Add(CollectionPanels(shuffleCollectionList,shuffleList));
        return page;
    }
    void RefreshShuffleList()
    {
        if(shuffleList==null||presetLibrary==null) return;
        var choices=visualChoices.Select(choice=>new ShuffleOption { Id=VisualShuffleId(choice),Name=choice.Name,Group=choice.Milkdrop?.Collection.Name??"Pulsive",GroupKey=choice.Milkdrop?.Collection.Id??"native" }).ToList();
        choices.Add(new() { Id="user:"+PresetLibrary.DefaultId,Name="Default",Group="User presets",GroupKey="user" });
        choices.AddRange(presetLibrary.Presets.Select(p=>new ShuffleOption { Id="user:"+p.Id,Name=p.Name,Group="User presets",GroupKey="user" }));
        var exclusions=prefs.ExcludedShufflePresets.ToHashSet(StringComparer.Ordinal);
        foreach(var option in choices)
        {
            option.IsIncluded=!exclusions.Contains(option.Id);
            option.PropertyChanged+=(_,_)=> { if(!batchingShuffle) SaveShuffleSelection(); };
        }
        shuffleOptions=choices; RefreshShuffleRows(); UpdateShuffleCount();
    }
    static string VisualShuffleId(VisualChoice choice)=>choice.Milkdrop!=null?"milk:"+choice.Milkdrop.Id:"style:"+choice.Mode;
    void UpdateShuffleCount()
    {
        int count=shuffleOptions.Count(o=>o.IsIncluded);
        shuffleCount.Text=$"{count}/{shuffleOptions.Count} included"+(count<2?" · Select at least two":"");
        selectShuffleButton.Content=count==shuffleOptions.Count?"Unselect all":"Select all";
    }
    void SaveShuffleSelection()
    {
        var available=shuffleOptions.Select(o=>o.Id).ToHashSet();
        prefs.ExcludedShufflePresets=prefs.ExcludedShufflePresets.Where(id=>!available.Contains(id)).Concat(shuffleOptions.Where(o=>!o.IsIncluded).Select(o=>o.Id)).Distinct().ToList();
        ResetShuffle(); UpdateShuffleCount(); Save();
    }
    void ResetShuffle()
    {
        shuffleElapsed=0; shuffleBeatElapsed=0; lastShuffleBeat=audio.Beats.BeatCount; shuffledEntry=null; shuffleCycle.Reset(); ClearShuffleTransition();
        lastManualBeat=manualTempo.Count(watch.Elapsed.TotalSeconds);
    }
    void ManualVisualChange()
    {
        if(!shuffleApplying) ResetShuffle();
    }
    void UpdateShuffle(double dt)
    {
        var beatState=audio.Beats.State;
        long manualBeat=manualTempo.Count(watch.Elapsed.TotalSeconds);
        int newManualBeats=(int)Math.Clamp(manualBeat-lastManualBeat,0,64); lastManualBeat=manualBeat;
        long beat=beatState.BeatCount;
        if(beat<lastShuffleBeat) shuffleBeatElapsed=0;
        int newBeats=(int)Math.Clamp(beat-lastShuffleBeat,0,16); lastShuffleBeat=beat;
        UpdateShuffleTempoStatus();
        if(transitioning)
        {
            transitionElapsed+=dt;
            double progress=Math.Clamp(transitionElapsed/activeTransitionDuration,0,1);
            // Smoothstep gives the fade a gentle start and finish.
            double blend=progress*progress*(3-2*progress);
            shuffleSnapshot.Opacity=1-blend; visual.Opacity=blend;
            if(progress>=1) { ClearShuffleTransition(); shuffleElapsed=0; shuffleBeatElapsed=0; }
            return;
        }
        if(!prefs.ShuffleEnabled||dt<=0||importingMilkdrop||(settingsOpen&&!Equals(shuffleSettingsTab.Tag,"ActivePage"))||presetDialog?.IsVisible==true||ContextMenu.IsOpen||selectors.Any(s=>s.IsDropDownOpen)||colorPickers.Any(p=>p.IsPickerOpen)) return;
        if(prefs.ShuffleTimingMode!=0)
        {
            if(prefs.ShuffleTimingMode==1&&!beatState.IsLocked) return;
            shuffleBeatElapsed+=prefs.ShuffleTimingMode==2?newManualBeats:newBeats;
            if(shuffleBeatElapsed<prefs.ShuffleBeats) return;
        }
        else
        {
            shuffleElapsed+=dt;
            if(shuffleElapsed<prefs.ShuffleInterval) return;
        }
        shuffleElapsed=0; shuffleBeatElapsed=0;
        var included=shuffleOptions.Where(o=>o.IsIncluded).Select(o=>o.Id).ToArray();
        if(included.Length<2) return;
        string? current=shuffledEntry??(visualChoices.FirstOrDefault(c=>c.Mode==visual.Mode&&c.Milkdrop?.Id==prefs.MilkdropPresetId) is { } choice?VisualShuffleId(choice):null);
        var next=shuffleCycle.Next(included,current);
        if(next==null) return;
        StartShuffleTransition(next);
    }
    void UpdateShuffleTempoStatus()
    {
        if(shuffleTempoStatus==null||prefs.ShuffleTimingMode==0) return;
        if(prefs.ShuffleTimingMode==2) { shuffleTempoStatus.Text=$"{prefs.ManualBpm:0.##} BPM · {shuffleBeatElapsed}/{prefs.ShuffleBeats} beats · Tap on the first beat to synchronise"; return; }
        string status=audio.Beats.IsLocked?$"≈ {audio.Beats.Bpm:0} BPM · {shuffleBeatElapsed}/{prefs.ShuffleBeats} beats":audio.Beats.Bpm>0?$"≈ {audio.Beats.Bpm:0} BPM · {shuffleBeatElapsed}/{prefs.ShuffleBeats} beats · Waiting for rhythm":"Detecting tempo… Waiting for a steady rhythm. No beat transitions during silence.";
        if(shuffleTempoStatus.Text!=status) shuffleTempoStatus.Text=status;
    }
    void StartShuffleTransition(string id)
    {
        ClearShuffleTransition();
        activeTransitionDuration=prefs.ShuffleTransition;
        if(activeTransitionDuration>0&&visual.ActualWidth>0&&visual.ActualHeight>0)
        {
            // One bounded bitmap of the outgoing style, never a second live engine.
            double scale=Math.Min(1,Math.Min(1280/visual.ActualWidth,720/visual.ActualHeight));
            var frame=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(visual.ActualWidth*scale)),Math.Max(1,(int)Math.Ceiling(visual.ActualHeight*scale)),96*scale,96*scale,PixelFormats.Pbgra32);
            frame.Render(visual); frame.Freeze(); shuffleSnapshot.Source=frame; shuffleSnapshot.Opacity=1; shuffleSnapshot.Visibility=Visibility.Visible;
            if(!root.Children.Contains(shuffleSnapshot)) root.Children.Insert(root.Children.IndexOf(visual)+1,shuffleSnapshot);
            transitioning=true; transitionElapsed=0;
        }
        shuffleApplying=true;
        visual.SkipPresetTransition=true;
        try
        {
            if(id.StartsWith("user:",StringComparison.Ordinal))
            {
                var playlist=PresetLibrary.ShuffleSnapshot(prefs); string? owner=prefs.ActivePresetId;
                ApplyPreset(id[5..]);
                PresetLibrary.CopyShuffle(playlist,prefs); prefs.ActivePresetId=owner; TrackPresetChanges(); Save();
            }
            else
            {
                int index=visualChoices.FindIndex(c=>VisualShuffleId(c)==id);
                if(index>=0) { modeSelect.SelectedIndex=index; ApplyVisualChoice(index); Save(); }
            }
            shuffledEntry=id;
        }
        finally { shuffleApplying=false; }
        if(transitioning) visual.Opacity=0;
    }
    void ClearShuffleTransition()
    {
        transitioning=false; shuffleSnapshot.Source=null; shuffleSnapshot.Visibility=Visibility.Collapsed;
        if(visual!=null) visual.Opacity=1;
    }
}
