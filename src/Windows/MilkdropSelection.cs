using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Pulsive;

public sealed partial class MainWindow
{
    const string MilkdropMoveFormat="Pulsive.MilkdropEntryIds";
    Button enableMilkdropButton=null!,deleteMilkdropButton=null!;
    bool batchingMilkdropSelection,dragMilkdropCandidate;
    Point milkdropDragStart;
    MilkdropRow? deferredMilkdropClick;
    MilkdropEntry[] SelectedMilkdropEntries()=>milkdropList.SelectedItems.OfType<MilkdropRow>().Select(row=>row.Entry).OfType<MilkdropEntry>().Distinct().ToArray();
    MilkdropEntry[] MilkdropActionEntries()
    {
        var selected=SelectedMilkdropEntries();
        return selected.Length>0?selected:milkdropLibrary.Entries.Where(entry=>entry.Collection==selectedMilkdropCollection).ToArray();
    }
    void AddMilkdropSelectionButtons(Panel actions)
    {
        enableMilkdropButton=new Button { Content="Enable selected",Width=130,Height=32,FontSize=12,Margin=new Thickness(8,0,0,0),IsEnabled=false };
        enableMilkdropButton.Click+=(_,_)=>
        {
            var selected=MilkdropActionEntries(); bool enable=!selected.All(entry=>entry.IsEnabled);
            batchingMilkdrop=true;
            try { foreach(var entry in selected) entry.IsEnabled=enable; } finally { batchingMilkdrop=false; }
            SyncMilkdropVisibility(); UpdateMilkdropSelection();
        };
        deleteMilkdropButton=new Button { Content="Delete selected",Width=110,Height=32,FontSize=12,Margin=new Thickness(8,0,0,0),IsEnabled=false };
        deleteMilkdropButton.Click+=(_,_)=>DeleteMilkdropSelection();
        actions.Children.Add(enableMilkdropButton); actions.Children.Add(deleteMilkdropButton);
    }
    void UpdateMilkdropSelection()
    {
        if(milkdropList==null||enableMilkdropButton==null) return;
        var selected=MilkdropActionEntries();
        string scope=SelectedMilkdropEntries().Length>0?"selected":"collection";
        enableMilkdropButton.IsEnabled=!importingMilkdrop&&selected.Length>0;
        enableMilkdropButton.Content=selected.Length>0&&selected.All(entry=>entry.IsEnabled)?$"Disable {scope}":$"Enable {scope}";
        deleteMilkdropButton.Content=scope=="collection"?"Delete presets":"Delete selected";
        deleteMilkdropButton.ToolTip=scope=="collection"?"Delete all imported presets in the selected collection":"Delete selected imported presets";
        deleteMilkdropButton.IsEnabled=!importingMilkdrop&&selected.Any(entry=>!entry.Bundled);
        selectMilkdropButton.Content=SelectedMilkdropEntries().Length>0&&SelectedMilkdropEntries().Length==milkdropList.Items.OfType<MilkdropRow>().Count(row=>row.Entry!=null)?"Unselect all":"Select all";
        milkdropCount.Text=$"{milkdropLibrary.Entries.Count(entry=>entry.IsEnabled)}/{milkdropLibrary.Entries.Count} enabled · {SelectedMilkdropEntries().Length} selected";
    }
    void SelectAllMilkdropEntries()
    {
        if(importingMilkdrop) return;
        bool clear=SelectedMilkdropEntries().Length==milkdropList.Items.OfType<MilkdropRow>().Count(row=>row.Entry!=null);
        batchingMilkdropSelection=true;
        try
        {
            milkdropList.SelectedItems.Clear();
            if(!clear)
            {
                foreach(var row in milkdropList.Items.OfType<MilkdropRow>().Where(row=>row.Entry!=null)) milkdropList.SelectedItems.Add(row);
            }
        }
        finally { batchingMilkdropSelection=false; }
        UpdateMilkdropSelection();
    }
    void DeleteMilkdropSelection()
    {
        if(importingMilkdrop) return;
        var selected=MilkdropActionEntries().Where(entry=>!entry.Bundled).ToArray(); if(selected.Length==0) return;
        presetDialog=new PresetNameDialog(settingsWindow,Resources,_=>{},$"Delete {selected.Length} preset(s) from the library? Original source files are kept. Bundled presets are protected.",()=>
        {
            int cached=milkdropLibrary.Delete(selected);
            foreach(var entry in selected) entry.PropertyChanged-=MilkdropEntryChanged;
            RefreshMilkdropList(); SyncMilkdropVisibility();
            milkdropStatus.Text=$"Deleted {selected.Length} preset(s)."+(cached>0?$" {cached} unused cached file(s) could not be removed.":"");
        },confirmationLabel:"Delete");
        try { presetDialog.ShowDialog(); } finally { presetDialog=null; backdrops.RefreshNative(); }
    }
    static bool IsMilkdropButton(object? source)
    {
        for(var node=source as DependencyObject;node!=null;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))
            if(node is ButtonBase) return true;
        return false;
    }
    void ConfigureMilkdropSelectionDrag()
    {
        milkdropList.PreviewMouseLeftButtonDown+=(_,e)=>
        {
            deferredMilkdropClick=null; dragMilkdropCandidate=false;
            if(IsMilkdropButton(e.OriginalSource)||MilkdropRowAt(e.OriginalSource) is not { Entry:not null } row) return;
            milkdropDragStart=e.GetPosition(milkdropList); dragMilkdropCandidate=true;
            if(milkdropList.SelectedItems.Contains(row)&&Keyboard.Modifiers==ModifierKeys.None) { deferredMilkdropClick=row; e.Handled=true; milkdropList.Focus(); }
        };
        milkdropList.PreviewMouseLeftButtonUp+=(_,_)=>
        {
            dragMilkdropCandidate=false;
            if(deferredMilkdropClick is { } row) { milkdropList.SelectedItems.Clear(); milkdropList.SelectedItem=row; deferredMilkdropClick=null; }
        };
        milkdropList.PreviewMouseMove+=(_,e)=>
        {
            if(!dragMilkdropCandidate||e.LeftButton!=MouseButtonState.Pressed||importingMilkdrop) return;
            var delta=e.GetPosition(milkdropList)-milkdropDragStart;
            if(Math.Abs(delta.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(delta.Y)<SystemParameters.MinimumVerticalDragDistance) return;
            dragMilkdropCandidate=false; deferredMilkdropClick=null;
            var selected=SelectedMilkdropEntries();
            if(selected.Length==0||selected.Any(entry=>entry.Bundled)) { milkdropStatus.Text="Bundled presets cannot be moved. Select imported presets to drag into a custom collection."; return; }
            var data=new DataObject(); data.SetData(MilkdropMoveFormat,selected.Select(entry=>entry.Id).ToArray(),false);
            DragDrop.DoDragDrop(milkdropList,data,DragDropEffects.Move); e.Handled=true;
        };
    }
    bool CanDropMilkdropSelection(object? source)=>!importingMilkdrop&&MilkdropRowAt(source)?.Collection.Id is { } id&&id is not ("bundled" or "imported");
    void DropMilkdropSelection(IDataObject data,object? source)
    {
        if(!CanDropMilkdropSelection(source)||MilkdropRowAt(source) is not { } row) { milkdropStatus.Text="Drop onto a custom collection. Bundled and Imported are protected destinations."; return; }
        if(data.GetData(MilkdropMoveFormat,false) is not string[] ids) return;
        try
        {
            var chosen=ids.ToHashSet(StringComparer.Ordinal);
            var entries=milkdropLibrary.Entries.Where(entry=>chosen.Contains(entry.Id)).ToArray();
            milkdropLibrary.MoveMany(entries,row.Collection); selectedMilkdropCollection=row.Collection;
            RefreshMilkdropList(); RebuildVisualiserChoices(); Save();
            milkdropStatus.Text=$"Moved {entries.Length} preset(s) to {row.Collection.Name}.";
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException) { milkdropStatus.Text=error.Message; }
    }
}
