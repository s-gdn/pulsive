using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;

namespace Pulsive;

internal sealed class PresetOption
{
    public string Id { get; init; }="";
    public string DisplayName { get; init; }="";
    public bool CanDelete { get; init; }
    public bool IsAction { get; init; }
}

public sealed partial class MainWindow
{
    PresetLibrary? presetLibrary;
    ComboBox toolbarPresets=null!,settingsPresets=null!;
    PresetNameDialog? presetDialog;
    Button savePresetChanges=null!;
    bool syncingPresets,applyingPreset,isPresetDirty;
    List<PresetOption> presetOptions=[];

    void InitializePresets(Grid toolbarGrid,Grid heading)
    {
        presetLibrary=new PresetLibrary(Path.Combine(Path.GetDirectoryName(SettingsPath)!,"presets.json"));
        ComboBox Picker(double width,double height)
        {
            var picker=Select([], -1,width); picker.Height=height; picker.Tag="PresetPicker";
            TextSearch.SetTextPath(picker,nameof(PresetOption.DisplayName));
            picker.ToolTip="User presets · visual settings";
            picker.ItemTemplate=(DataTemplate)XamlReader.Parse("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid MinWidth="230" MaxWidth="300">
                <Grid.Resources><BooleanToVisibilityConverter x:Key="Visible"/></Grid.Resources>
                <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                <TextBlock Text="{Binding DisplayName}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" Margin="0,0,8,0"/>
                <Button Grid.Column="1" Width="24" Height="24" Tag="DeletePreset" ToolTip="Delete preset" Visibility="{Binding CanDelete, Converter={StaticResource Visible}}">
                  <Path Data="M 0,0 L 8,8 M 8,0 L 0,8" Width="8" Height="8" Stretch="None" Stroke="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" StrokeThickness="1.4" StrokeStartLineCap="Round" StrokeEndLineCap="Round" HorizontalAlignment="Center" VerticalAlignment="Center" IsHitTestVisible="False"/>
                </Button>
              </Grid>
            </DataTemplate>
            """);
            picker.SelectionChanged+=(_,_)=>
            {
                if(syncingPresets||picker.SelectedItem is not PresetOption option) return;
                if(option.IsAction) { SyncPresetSelection(); ShowSavePreset(); }
                else ApplyPreset(option.Id);
            };
            picker.AddHandler(ButtonBase.ClickEvent,new RoutedEventHandler((_,e)=>
            {
                if(e.OriginalSource is not Button { Tag:"DeletePreset",DataContext:PresetOption { CanDelete:true } option }) return;
                e.Handled=true; DeletePreset(option.Id);
            }));
            return picker;
        }
        toolbarPresets=Picker(176,34); toolbarPresets.Margin=new Thickness(8,0,0,0); toolbarPresets.HorizontalAlignment=HorizontalAlignment.Left;
        Grid.SetColumn(toolbarPresets,2); toolbarGrid.Children.Add(toolbarPresets);
        settingsPresets=Picker(182,30);
        var existingHint=heading.Children.OfType<TextBlock>().FirstOrDefault(child=>Grid.GetColumn(child)==1);
        if(existingHint!=null) heading.Children.Remove(existingHint);
        var settingsPicker=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,8,0) };
        settingsPicker.Children.Add(new TextBlock { Text="PRESETS",Foreground=MutedColor,FontSize=9,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0) });
        settingsPicker.Children.Add(settingsPresets); Grid.SetColumn(settingsPicker,1); heading.Children.Add(settingsPicker);
        savePresetChanges=new Button { Content="Save changes",Width=112,Height=30,FontSize=11,Margin=new Thickness(8,0,0,0),Visibility=Visibility.Collapsed };
        savePresetChanges.Click+=(_,_)=>ConfirmSavePreset(); settingsPicker.Children.Add(savePresetChanges);
        if(prefs.ActivePresetId==null&&PresetLibrary.Matches(prefs,PresetLibrary.Default)) prefs.ActivePresetId=PresetLibrary.DefaultId;
        if(prefs.ActivePresetId!=PresetLibrary.DefaultId&&presetLibrary.Presets.All(p=>p.Id!=prefs.ActivePresetId)) prefs.ActivePresetId=null;
        RefreshPresetPickers(); TrackPresetChanges();
    }
    void RefreshPresetPickers()
    {
        presetOptions=[new() { Id=PresetLibrary.DefaultId,DisplayName="Default" },..presetLibrary!.Presets.OrderBy(p=>p.Name,StringComparer.OrdinalIgnoreCase).Select(p=>new PresetOption { Id=p.Id,DisplayName=p.Name,CanDelete=true }),new() { Id="save-new",DisplayName="Save as new preset…",IsAction=true }];
        syncingPresets=true;
        try { toolbarPresets.ItemsSource=presetOptions; settingsPresets.ItemsSource=presetOptions; }
        finally { syncingPresets=false; }
        SyncPresetSelection();
    }
    void SyncPresetSelection()
    {
        syncingPresets=true;
        try
        {
            var selected=presetOptions.FirstOrDefault(p=>!p.IsAction&&p.Id==prefs.ActivePresetId);
            toolbarPresets.SelectedItem=isPresetDirty?null:selected; settingsPresets.SelectedItem=isPresetDirty?null:selected;
            string label=selected==null?"Unsaved settings":selected.DisplayName+(isPresetDirty?" •":"");
            toolbarPresets.Text=label; settingsPresets.Text=label;
            savePresetChanges.Visibility=isPresetDirty?Visibility.Visible:Visibility.Collapsed;
            savePresetChanges.Content=selected is { CanDelete:true }?"Save changes":"Save as new";
        }
        finally { syncingPresets=false; }
    }
    void TrackPresetChanges()
    {
        if(presetLibrary==null||applyingPreset) return;
        var baseline=prefs.ActivePresetId==PresetLibrary.DefaultId?PresetLibrary.Default:presetLibrary.Presets.FirstOrDefault(p=>p.Id==prefs.ActivePresetId)?.Settings;
        isPresetDirty=baseline==null||!PresetLibrary.Matches(prefs,baseline); SyncPresetSelection();
    }
    void ConfirmSavePreset()
    {
        if(!isPresetDirty) return;
        var current=presetLibrary!.Presets.FirstOrDefault(p=>p.Id==prefs.ActivePresetId);
        if(current==null) { ShowSavePreset(); return; }
        presetDialog=new PresetNameDialog(settingsWindow.IsVisible?settingsWindow:this,Resources,_=>{},$"Overwrite “{current.Name}” with the current visual settings?",()=>
        {
            presetLibrary.Update(current.Id,prefs); isPresetDirty=false; RefreshPresetPickers(); Save();
        });
        try { presetDialog.ShowDialog(); } finally { presetDialog=null; backdrops.RefreshNative(); }
    }
    void ShowSavePreset()
    {
        foreach(var picker in selectors) picker.IsDropDownOpen=false;
        if(presetLibrary!.LoadError is { } error)
        {
            presetDialog=new PresetNameDialog(settingsWindow.IsVisible?settingsWindow:this,Resources,_=>{},error);
            try { presetDialog.ShowDialog(); } finally { presetDialog=null; }
            return;
        }
        presetDialog=new PresetNameDialog(settingsWindow.IsVisible?settingsWindow:this,Resources,name=>
        {
            Save();
            var added=presetLibrary!.Add(name,prefs);
            prefs.ActivePresetId=added.Id; isPresetDirty=false; RefreshPresetPickers(); Save();
        });
        try { presetDialog.ShowDialog(); }
        finally { presetDialog=null; backdrops.RefreshNative(); }
    }
    void DeletePreset(string id)
    {
        var current=presetLibrary!.Presets.FirstOrDefault(p=>p.Id==id);
        if(current==null) return;
        foreach(var picker in selectors) picker.IsDropDownOpen=false;
        presetDialog=new PresetNameDialog(settingsWindow.IsVisible?settingsWindow:this,Resources,_=>{},$"Delete “{current.Name}”? This cannot be undone.",()=>
        {
            presetLibrary.Delete(id);
            if(prefs.ActivePresetId==id) { prefs.ActivePresetId=null; isPresetDirty=true; }
            RefreshPresetPickers(); Save();
        },confirmationLabel:"Delete");
        try { presetDialog.ShowDialog(); } finally { presetDialog=null; backdrops.RefreshNative(); }
    }
    void ApplyPreset(string id)
    {
        var settings=id==PresetLibrary.DefaultId?PresetLibrary.Default:presetLibrary!.Presets.FirstOrDefault(p=>p.Id==id)?.Settings;
        if(settings==null) return;
        applyingPreset=true;
        try
        {
            PresetLibrary.Copy(settings,prefs);
            RestoreVisualMode(prefs.Mode,prefs.MilkdropPresetId);
            visual.UsePulsiveColorsForMilkdrop=prefs.UsePulsiveColorsForMilkdrop;
            milkdropColorsToggle.IsChecked=prefs.UsePulsiveColorsForMilkdrop;
            paletteSelect.SelectedIndex=Math.Clamp(prefs.Palette,0,Visualizer.Palettes.Length-1);
            sensitivitySlider.Value=Math.Clamp(prefs.Gain,.25,5); brightnessSlider.Value=Math.Clamp(prefs.Intensity,.1,1); zoomSlider.Value=Math.Clamp(prefs.Zoom,.25,4);
            visual.CustomStart=ParseColor(prefs.CustomColorStart,Visualizer.ColorForPalette(0,0,default,default));
            visual.CustomEnd=ParseColor(prefs.CustomColorEnd,Visualizer.ColorForPalette(0,1,default,default));
            gradientStartPicker.SelectedColor=visual.CustomStart; gradientEndPicker.SelectedColor=visual.CustomEnd;
            backgroundColorPicker.SelectedColor=ParseColor(prefs.BackgroundColor,System.Windows.Media.Color.FromRgb(16,23,37));
            borderColorPicker.SelectedColor=ParseColor(prefs.BorderColor,System.Windows.Media.Color.FromRgb(104,239,222));
            backgroundBlurSlider.Value=Math.Clamp(prefs.BackgroundBlurAmount,0,48);
            SetBackgroundType(prefs.BackgroundType<0?(prefs.BackgroundBlur?2:0):prefs.BackgroundType);
            SetBorderType(prefs.BorderType);
            updatingFeather=true;
            try
            {
                double[] amount=[prefs.FeatherLeft,prefs.FeatherTop,prefs.FeatherRight,prefs.FeatherBottom],bounds=[prefs.FeatherBoundLeft,prefs.FeatherBoundTop,prefs.FeatherBoundRight,prefs.FeatherBoundBottom];
                for(int i=0;i<4;i++) { featherSliders[i].Value=Math.Clamp(amount[i],0,1); featherBoundsSliders[i].Value=Math.Clamp(bounds[i],0,1); }
                linkFeatherToggle.IsChecked=featherControlSelect.SelectedIndex==1?prefs.LinkFeatherBounds:prefs.LinkFeather;
            }
            finally { updatingFeather=false; }
            UpdateFeather(); UpdateBorder(); visual.InvalidateVisual(); prefs.ActivePresetId=id; isPresetDirty=false;
        }
        finally { applyingPreset=false; }
        TrackPresetChanges(); Save();
    }
}
