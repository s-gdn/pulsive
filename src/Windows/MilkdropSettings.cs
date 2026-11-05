using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;

namespace Pulsive;

public sealed partial class MainWindow
{
    const int NativeModeCount=11;
    readonly MilkdropLibrary milkdropLibrary;
    sealed record VisualChoice(string Name,int Mode,MilkdropEntry? Milkdrop=null);
    List<VisualChoice> visualChoices=[];
    bool syncingVisualModes,batchingMilkdrop,importingMilkdrop;
    Button milkdropSettingsTab=null!,importMilkdropButton=null!,selectMilkdropButton=null!;
    ListBox milkdropList=null!;
    CheckBox milkdropColorsToggle=null!;
    TextBlock milkdropStatus=null!,milkdropCount=null!;

    void RebuildVisualiserChoices()
    {
        visualChoices=Visualizer.Modes.Take(NativeModeCount).Select((name,index)=>new VisualChoice(name,index)).ToList();
        for(int i=0;i<milkdropLibrary.Entries.Count;i++)
        {
            var entry=milkdropLibrary.Entries[i];
            if(entry.IsEnabled) visualChoices.Add(new VisualChoice(entry.Name,NativeModeCount+i,entry));
        }
        syncingVisualModes=true;
        try
        {
            modeSelect.ItemsSource=visualChoices.Select(choice=>choice.Name).ToArray();
            RestoreVisualMode(prefs.Mode,prefs.MilkdropPresetId);
        }
        finally { syncingVisualModes=false; }
        RefreshShuffleList();
    }
    void RestoreVisualMode(int mode,string? id)
    {
        int index=mode>=NativeModeCount&&!string.IsNullOrEmpty(id)
            ?visualChoices.FindIndex(choice=>choice.Milkdrop?.Id==id)
            :visualChoices.FindIndex(choice=>choice.Mode==mode);
        index=Math.Max(0,index);
        bool wasSyncing=syncingVisualModes; syncingVisualModes=true;
        try { modeSelect.SelectedIndex=index; ApplyVisualChoice(index); }
        finally { syncingVisualModes=wasSyncing; }
    }
    void ApplyVisualChoice(int index)
    {
        if(index<0||index>=visualChoices.Count) return;
        var choice=visualChoices[index];
        visual.Mode=choice.Mode; visual.MilkdropPresetPath=choice.Milkdrop?.FilePath;
        visual.MilkdropTextureDirectory=choice.Milkdrop?.Collection.TextureDirectory;
        prefs.Mode=choice.Mode; prefs.MilkdropPresetId=choice.Milkdrop?.Id;
        visual.InvalidateVisual();
    }
    StackPanel BuildMilkdropPage()
    {
        selectedMilkdropCollection=milkdropLibrary.Collections.Single(c=>c.Id=="imported");
        var page=new StackPanel { Visibility=Visibility.Collapsed };
        page.Children.Add(new TextBlock { Text="MILKDROP LIBRARY",FontSize=10,FontWeight=FontWeights.SemiBold,Foreground=AccentColor,Margin=new Thickness(0,0,0,8) });
        page.Children.Add(new TextBlock { Text="Drop a pack folder to create a collection, or select one to import .milk files. Click headers to collapse.",FontSize=12,Foreground=MutedColor,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12) });
        var actions=new Grid { Margin=new Thickness(0,0,0,10) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        importMilkdropButton=new Button { Content="Import .milk files…",Height=32,Width=148,FontSize=12 };
        importMilkdropButton.Click+=async (_,_)=>
        {
            var picker=new OpenFileDialog { Title="Import MilkDrop presets",Filter="MilkDrop presets (*.milk)|*.milk",Multiselect=true,CheckFileExists=true };
            if(picker.ShowDialog(settingsWindow)==true) await ImportMilkdropFiles(picker.FileNames);
        };
        actions.Children.Add(importMilkdropButton);
        selectMilkdropButton=new Button { Height=32,Width=130,FontSize=12,Margin=new Thickness(8,0,0,0) };
        selectMilkdropButton.Click+=(_,_)=>SetAllMilkdrop(!milkdropLibrary.Entries.All(entry=>entry.IsEnabled));
        Grid.SetColumn(selectMilkdropButton,1); actions.Children.Add(selectMilkdropButton);
        newMilkdropCollectionButton=new Button { Content="New collection…",Width=130,Height=32,FontSize=12,Margin=new Thickness(8,0,0,0) };
        newMilkdropCollectionButton.Click+=(_,_)=>ShowNewMilkdropCollection(); Grid.SetColumn(newMilkdropCollectionButton,2); actions.Children.Add(newMilkdropCollectionButton);
        milkdropCount=new TextBlock { MaxWidth=290,TextTrimming=TextTrimming.CharacterEllipsis, Foreground=MutedColor,FontSize=11,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right };
        Grid.SetColumn(milkdropCount,3); actions.Children.Add(milkdropCount); page.Children.Add(actions);
        milkdropList=new ListBox { Height=282,AllowDrop=true,Background=Brushes.Transparent,Foreground=TextColor,BorderBrush=new SolidColorBrush(Color.FromArgb(40,255,255,255)),BorderThickness=new Thickness(1),Padding=new Thickness(6),FocusVisualStyle=null };
        VirtualizingPanel.SetIsVirtualizing(milkdropList,true);
        VirtualizingPanel.SetVirtualizationMode(milkdropList,VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(milkdropList,ScrollUnit.Pixel);
        milkdropList.Template=(ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListBox">
          <Border CornerRadius="16" Background="#15000000" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}">
            <ScrollViewer Style="{DynamicResource PulsiveScrollViewer}" Focusable="False" CanContentScroll="True" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled"><ItemsPresenter/></ScrollViewer>
          </Border>
        </ControlTemplate>
        """);
        milkdropList.ItemContainerStyle=(Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListBoxItem">
          <Setter Property="HorizontalContentAlignment" Value="Stretch"/><Setter Property="FocusVisualStyle" Value="{x:Null}" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBoxItem">
            <Border x:Name="Row" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" CornerRadius="10" Background="Transparent" Margin="1"><ContentPresenter/></Border>
            <ControlTemplate.Triggers><Trigger Property="IsSelected" Value="True"><Setter TargetName="Row" Property="Background" Value="#0FFFFFFF"/></Trigger></ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
        milkdropList.ItemTemplate=(DataTemplate)XamlReader.Parse("""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Grid>
            <ToggleButton x:Name="CollectionHeader" Tag="ToggleCollection" Visibility="Collapsed" IsChecked="{Binding Collection.IsExpanded,Mode=OneWay}" FocusVisualStyle="{x:Null}" Cursor="Hand" ToolTip="Expand or collapse collection">
              <ToggleButton.Template><ControlTemplate TargetType="ToggleButton">
                <Grid Background="Transparent" Margin="10,8,10,5" MinHeight="22">
                  <Grid.ColumnDefinitions><ColumnDefinition Width="22"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                  <Path x:Name="Arrow" Data="M 2,2 L 7,7 L 2,12" Stroke="#C6CBD3" StrokeThickness="1.5" StrokeStartLineCap="Round" StrokeEndLineCap="Round" Width="10" Height="14" VerticalAlignment="Center" HorizontalAlignment="Center" RenderTransformOrigin="0.5,0.5"/>
                  <TextBlock Grid.Column="1" Text="{Binding Collection.Name}" Foreground="#EBEDF0" FontSize="12" FontWeight="SemiBold" TextTrimming="CharacterEllipsis" VerticalAlignment="Center"/>
                  <TextBlock Grid.Column="2" Text="{Binding Collection.TextureBadge}" Foreground="#B5BBC4" FontSize="9" VerticalAlignment="Center"/>
                </Grid>
                <ControlTemplate.Triggers><Trigger Property="IsChecked" Value="True"><Setter TargetName="Arrow" Property="RenderTransform"><Setter.Value><RotateTransform Angle="90"/></Setter.Value></Setter></Trigger></ControlTemplate.Triggers>
              </ControlTemplate></ToggleButton.Template>
            </ToggleButton>
            <Border x:Name="TexturesRow" Visibility="Collapsed" Background="#06FFFFFF" CornerRadius="10" Padding="10,6" ToolTip="{Binding Collection.TextureDetails}">
              <Grid>
                <Grid.ColumnDefinitions><ColumnDefinition Width="22"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                <Border Width="21" Height="21" CornerRadius="7" Background="#15FFFFFF" BorderBrush="#50FFFFFF" BorderThickness="1" VerticalAlignment="Center">
                  <Path x:Name="TextureTick" Data="M 3,8 L 7,12 L 14,3" Stroke="#F5F6F8" StrokeThickness="1.6" StrokeStartLineCap="Round" StrokeEndLineCap="Round" Width="17" Height="16" HorizontalAlignment="Center" VerticalAlignment="Center" Visibility="Collapsed"/>
                </Border>
                <StackPanel Grid.Column="1" Margin="12,0" VerticalAlignment="Center">
                  <TextBlock Text="{Binding Collection.TextureLabel}" Foreground="#DDE1E6" FontSize="11" TextTrimming="CharacterEllipsis"/>
                  <TextBlock Text="{Binding Collection.TextureDetails}" Foreground="#9299A4" FontSize="10" TextTrimming="CharacterEllipsis"/>
                </StackPanel>
                <Button Grid.Column="2" Content="Import folder…" Tag="ImportTextures" Width="112" Height="28" FontSize="11"/>
              </Grid>
            </Border>
            <CheckBox x:Name="PresetRow" IsChecked="{Binding Entry.IsEnabled,Mode=TwoWay,UpdateSourceTrigger=PropertyChanged}" Foreground="#EBEDF0" HorizontalContentAlignment="Stretch" MinHeight="44" ToolTip="{Binding Entry.Name}">
              <CheckBox.Template><ControlTemplate TargetType="CheckBox">
                <Grid Background="Transparent" Margin="10,6">
                  <Grid.ColumnDefinitions><ColumnDefinition Width="22"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                  <Border x:Name="Box" Width="21" Height="21" CornerRadius="7" Background="#15FFFFFF" BorderBrush="#50FFFFFF" BorderThickness="1" VerticalAlignment="Center">
                    <Path x:Name="Tick" Data="M 3,8 L 7,12 L 14,3" Stroke="#F5F6F8" StrokeThickness="1.6" StrokeStartLineCap="Round" StrokeEndLineCap="Round" Width="17" Height="16" HorizontalAlignment="Center" VerticalAlignment="Center" Visibility="Collapsed"/>
                  </Border>
                  <StackPanel Grid.Column="1" Margin="12,0" VerticalAlignment="Center">
                    <TextBlock Text="{Binding Entry.Name}" TextTrimming="CharacterEllipsis" FontSize="12" Foreground="{TemplateBinding Foreground}"/>
                    <TextBlock x:Name="Status" Text="{Binding Entry.TextureStatus}" TextTrimming="CharacterEllipsis" FontSize="10" Foreground="#B5BBC4"/>
                  </StackPanel>
                  <TextBlock Grid.Column="2" Text="{Binding Entry.Origin}" FontSize="9" Foreground="#9299A4" VerticalAlignment="Center"/>
                </Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsChecked" Value="True"><Setter TargetName="Tick" Property="Visibility" Value="Visible"/><Setter TargetName="Box" Property="Background" Value="#28FFFFFF"/></Trigger>
                  <DataTrigger Binding="{Binding Entry.TextureStatus}" Value=""><Setter TargetName="Status" Property="Visibility" Value="Collapsed"/></DataTrigger>
                </ControlTemplate.Triggers>
              </ControlTemplate></CheckBox.Template>
            </CheckBox>
          </Grid>
          <DataTemplate.Triggers>
            <DataTrigger Binding="{Binding Kind}" Value="Collection"><Setter TargetName="CollectionHeader" Property="Visibility" Value="Visible"/><Setter TargetName="PresetRow" Property="Visibility" Value="Collapsed"/></DataTrigger>
            <DataTrigger Binding="{Binding Kind}" Value="Textures"><Setter TargetName="TexturesRow" Property="Visibility" Value="Visible"/><Setter TargetName="PresetRow" Property="Visibility" Value="Collapsed"/></DataTrigger>
            <DataTrigger Binding="{Binding Collection.HasTextures}" Value="True"><Setter TargetName="TextureTick" Property="Visibility" Value="Visible"/></DataTrigger>
          </DataTemplate.Triggers>
        </DataTemplate>
        """);
        ConfigureMilkdropCollections();
        milkdropList.PreviewDragOver+=(_,e)=>
        {
            var files=DropFiles(e.Data);
            bool accept=!importingMilkdrop&&files.Any(file=>Directory.Exists(file)||Path.GetExtension(file).Equals(".milk",StringComparison.OrdinalIgnoreCase));
            e.Effects=accept?DragDropEffects.Copy:DragDropEffects.None;
            milkdropList.BorderBrush=accept?AccentColor:new SolidColorBrush(Color.FromArgb(40,255,255,255)); e.Handled=true;
        };
        milkdropList.DragLeave+=(_,_)=>ResetMilkdropDropBorder();
        milkdropList.PreviewDrop+=async (_,e)=>
        {
            e.Handled=true; ResetMilkdropDropBorder();
            var files=DropFiles(e.Data); var row=MilkdropRowAt(e.OriginalSource);
            if(row!=null) SetMilkdropImportTarget(row.Collection);
            var folders=files.Where(Directory.Exists).ToArray();
            if(folders.Length>0) await ImportMilkdropFolders(folders,row?.Kind=="Textures"?row.Collection:null);
            await ImportMilkdropFiles(files.Where(File.Exists),row?.Collection);
        };
        page.Children.Add(milkdropList);
        milkdropColorsToggle=new CheckBox { Content="Use pulsive colors for MilkDrop",IsChecked=prefs.UsePulsiveColorsForMilkdrop,Foreground=TextColor,FontSize=12,Margin=new Thickness(0,16,0,0) };
        milkdropColorsToggle.Click+=(_,_)=> { prefs.UsePulsiveColorsForMilkdrop=milkdropColorsToggle.IsChecked==true; visual.UsePulsiveColorsForMilkdrop=prefs.UsePulsiveColorsForMilkdrop; visual.InvalidateVisual(); Save(); };
        page.Children.Add(milkdropColorsToggle);
        milkdropImportBar=BuildMilkdropImportBar();
        milkdropImportLabel=new TextBlock { FontSize=11,Foreground=MutedColor,Margin=new Thickness(0,0,0,4),Visibility=Visibility.Collapsed,TextTrimming=TextTrimming.CharacterEllipsis };
        var progressGroup=new StackPanel(); progressGroup.Children.Add(milkdropImportLabel); progressGroup.Children.Add(milkdropImportBar); page.Children.Insert(3,progressGroup);
        milkdropStatus=new TextBlock { Text=milkdropLibrary.LoadError??"Imported files are copied into your local Pulsive library.",FontSize=11,Foreground=MutedColor,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0) };
        page.Children.Add(milkdropStatus);
        foreach(var entry in milkdropLibrary.Entries) entry.PropertyChanged+=MilkdropEntryChanged;
        RefreshMilkdropList(); return page;
    }
    static string[] DropFiles(IDataObject data)=>data.GetDataPresent(DataFormats.FileDrop)?data.GetData(DataFormats.FileDrop) as string[]??[]:[];
    void ResetMilkdropDropBorder()=>milkdropList.BorderBrush=new SolidColorBrush(Color.FromArgb(40,255,255,255));
    void MilkdropEntryChanged(object? sender,PropertyChangedEventArgs e) { if(!batchingMilkdrop&&e.PropertyName==nameof(MilkdropEntry.IsEnabled)) SyncMilkdropVisibility(); }
    void RefreshMilkdropList()
    {
        var rows=new List<MilkdropRow>();
        var grouped=milkdropLibrary.Entries.ToLookup(entry=>entry.Collection);
        foreach(var collection in milkdropLibrary.Collections.OrderBy(c=>c.Id=="bundled"?0:c.Id=="imported"?1:2).ThenBy(c=>c.Name,StringComparer.OrdinalIgnoreCase))
        {
            rows.Add(new("Collection",collection));
            if(!collection.IsExpanded) continue;
            rows.Add(new("Textures",collection));
            rows.AddRange(grouped[collection].OrderBy(entry=>entry.Name,StringComparer.OrdinalIgnoreCase).Select(entry=>new MilkdropRow("Preset",collection,entry)));
        }
        milkdropList.ItemsSource=rows;
        UpdateMilkdropCount();
    }
    void UpdateMilkdropCount()
    {
        int enabled=milkdropLibrary.Entries.Count(entry=>entry.IsEnabled);
        milkdropCount.Text=$"{enabled}/{milkdropLibrary.Entries.Count} · {selectedMilkdropCollection?.Name??"Imported"}";
        milkdropCount.ToolTip="Import into: "+(selectedMilkdropCollection?.Name??"Imported");
        importMilkdropButton.ToolTip=milkdropCount.ToolTip;
        selectMilkdropButton.Content=enabled==milkdropLibrary.Entries.Count?"Unselect all":"Select all";
    }
    void SetAllMilkdrop(bool enabled)
    {
        batchingMilkdrop=true;
        try { foreach(var entry in milkdropLibrary.Entries) entry.IsEnabled=enabled; }
        finally { batchingMilkdrop=false; }
        SyncMilkdropVisibility();
    }
    void SyncMilkdropVisibility()
    {
        prefs.DisabledMilkdropPresets=milkdropLibrary.Entries.Where(entry=>!entry.IsEnabled).Select(entry=>entry.Id).ToList();
        RebuildVisualiserChoices(); UpdateMilkdropCount(); Save();
    }
    async Task ImportMilkdropFiles(IEnumerable<string> files,MilkdropCollection? target=null)
    {
        if(importingMilkdrop) return;
        var paths=files.ToArray(); if(paths.Length==0) return;
        string collectionId=(target??selectedMilkdropCollection)?.Id??"imported";
        var progress=BeginMilkdropImport("Importing MilkDrop presets…");
        try
        {
            var result=await Task.Run(()=>milkdropLibrary.Import(paths,collectionId,progress));
            if(closing) return;
            foreach(var entry in result.Added) entry.PropertyChanged+=MilkdropEntryChanged;
            RefreshMilkdropList(); SyncMilkdropVisibility();
            milkdropStatus.Text=$"Imported {result.Added.Count} preset(s)."+(result.Duplicates>0?$" {result.Duplicates} already in the library.":"")+(result.Errors.Count>0?" "+string.Join(" ",result.Errors.Take(3)):"");
        }
        finally
        {
            EndMilkdropImport();
        }
    }
}
