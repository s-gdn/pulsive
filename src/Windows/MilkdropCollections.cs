using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using Microsoft.Win32;

namespace Pulsive;

public sealed partial class MainWindow
{
    sealed record MilkdropRow(string Kind,MilkdropCollection Collection,MilkdropEntry? Entry=null);
    MilkdropCollection? selectedMilkdropCollection;
    Button newMilkdropCollectionButton=null!;
    static MilkdropRow? MilkdropRowAt(object? source)
    {
        for(var current=source as DependencyObject;current!=null;current=current is Visual?VisualTreeHelper.GetParent(current):LogicalTreeHelper.GetParent(current))
            if(current is FrameworkElement { DataContext:MilkdropRow row }) return row;
        return null;
    }
    void SetMilkdropImportTarget(MilkdropCollection collection)
    {
        selectedMilkdropCollection=collection; UpdateMilkdropCount();
    }
    void ShowNewMilkdropCollection()
    {
        if(importingMilkdrop) return;
        if(milkdropList.ContextMenu!=null) milkdropList.ContextMenu.IsOpen=false;
        presetDialog=new PresetNameDialog(settingsWindow,Resources,name=>
        {
            selectedMilkdropCollection=milkdropLibrary.CreateCollection(name);
            RefreshMilkdropList();
            var header=milkdropList.Items.OfType<MilkdropRow>().First(row=>row.Kind=="Collection"&&row.Collection==selectedMilkdropCollection);
            milkdropList.SelectedItem=header; milkdropList.ScrollIntoView(header);
        },promptTitle:"NEW COLLECTION",saveLabel:"Create");
        try { presetDialog.ShowDialog(); }
        finally { presetDialog=null; backdrops.RefreshNative(); }
    }
    void ConfigureMilkdropCollections()
    {
        milkdropList.PreviewMouseDown+=(_,e)=>
        {
            if(MilkdropRowAt(e.OriginalSource) is { } row) { milkdropList.SelectedItem=row; SetMilkdropImportTarget(row.Collection); }
        };
        milkdropList.SelectionChanged+=(_,_)=> { if(milkdropList.SelectedItem is MilkdropRow row) SetMilkdropImportTarget(row.Collection); };
        milkdropList.AddHandler(ButtonBase.ClickEvent,new RoutedEventHandler(async (_,e)=>
        {
            if(e.OriginalSource is ToggleButton { Tag:"ToggleCollection",DataContext:MilkdropRow header })
            { e.Handled=true; ToggleMilkdropCollection(header.Collection); return; }
            if(e.OriginalSource is not Button { Tag:"ImportTextures",DataContext:MilkdropRow row }) return;
            e.Handled=true;
            var picker=new OpenFolderDialog { Title="Import textures for "+row.Collection.Name };
            if(picker.ShowDialog(settingsWindow)==true) await ImportMilkdropTextureFolder(row.Collection,picker.FolderName);
        }));
        milkdropList.PreviewKeyDown+=(_,e)=>
        {
            if(milkdropList.SelectedItem is MilkdropRow { Kind:"Collection" } header&&e.Key is Key.Space or Key.Enter)
            { e.Handled=true; ToggleMilkdropCollection(header.Collection); }
        };
        var menu=new ContextMenu(); menu.Items.Add(new MenuItem { Header="New collection…" });
        milkdropList.ContextMenu=menu;
        milkdropList.ContextMenuOpening+=(_,_)=>
        {
            menu.Items.Clear();
            var create=new MenuItem { Header="New collection…",IsEnabled=!importingMilkdrop }; create.Click+=(_,_)=>ShowNewMilkdropCollection(); menu.Items.Add(create);
            if(milkdropList.SelectedItem is MilkdropRow { Entry: { Bundled:false } entry })
            {
                var move=new MenuItem { Header="Move to collection",IsEnabled=!importingMilkdrop };
                foreach(var collection in milkdropLibrary.Collections)
                {
                    var item=new MenuItem { Header=collection.Name,IsEnabled=collection!=entry.Collection };
                    item.Click+=(_,_)=>
                    {
                        try { milkdropLibrary.Move(entry,collection); RefreshMilkdropList(); RebuildVisualiserChoices(); Save(); }
                        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException) { milkdropStatus.Text=error.Message; }
                    };
                    move.Items.Add(item);
                }
                menu.Items.Add(move);
            }
        };
        menu.Closed+=(_,_)=>backdrops.RefreshNative();
    }
    void ToggleMilkdropCollection(MilkdropCollection collection)
    {
        if(importingMilkdrop) return;
        try
        {
            milkdropLibrary.SetExpanded(collection,!collection.IsExpanded);
            RefreshMilkdropList();
            var row=milkdropList.Items.OfType<MilkdropRow>().First(item=>item.Kind=="Collection"&&item.Collection==collection);
            milkdropList.SelectedItem=row; milkdropList.ScrollIntoView(row);
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException) { milkdropStatus.Text=error.Message; }
    }
    async Task ImportMilkdropFolders(IEnumerable<string> folders,MilkdropCollection? textureTarget=null)
    {
        if(importingMilkdrop) return;
        var paths=folders.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var progress=BeginMilkdropImport("Scanning and importing folders…"); int folderIndex=0;
        var messages=new List<string>();
        try
        {
            foreach(string folder in paths)
            {
                importBatch=$"Folder {++folderIndex}/{paths.Length} · ";
                try
                {
                    bool onlyTextures=textureTarget!=null&&!await Task.Run(()=>MilkdropLibrary.ContainsMilkFiles(folder));
                    if(onlyTextures)
                    {
                        int count=await Task.Run(()=>milkdropLibrary.ImportTextures(textureTarget!,folder,progress));
                        messages.Add($"Imported {count} texture(s) for {textureTarget!.Name}.");
                    }
                    else
                    {
                        var result=await Task.Run(()=>milkdropLibrary.ImportFolder(folder,progress));
                        foreach(var entry in result.Presets.Added) entry.PropertyChanged+=MilkdropEntryChanged;
                        selectedMilkdropCollection=result.Collection;
                        messages.Add($"Created {result.Collection.Name}: {result.Presets.Added.Count} preset(s), {result.Textures} texture(s).");
                        messages.AddRange(result.Errors.Take(2));
                    }
                }
                catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException) { messages.Add(error.Message); }
                if(closing) return;
            }
            RefreshMilkdropList(); SyncMilkdropVisibility();
            var header=milkdropList.Items.OfType<MilkdropRow>().FirstOrDefault(row=>row.Kind=="Collection"&&row.Collection==selectedMilkdropCollection);
            if(header!=null) { milkdropList.SelectedItem=header; milkdropList.ScrollIntoView(header); }
            milkdropStatus.Text=string.Join(" ",messages.Take(3))+(messages.Count>3?$" (+{messages.Count-3} more details)":"");
            milkdropStatus.ToolTip=string.Join("\n",messages);
        }
        finally { EndMilkdropImport(); }
    }
    async Task ImportMilkdropTextureFolder(MilkdropCollection collection,string folder)
    {
        if(importingMilkdrop) return;
        var progress=BeginMilkdropImport("Importing textures for "+collection.Name+"…");
        try
        {
            int count=await Task.Run(()=>milkdropLibrary.ImportTextures(collection,folder,progress));
            if(closing) return;
            RefreshMilkdropList(); RebuildVisualiserChoices();
            milkdropStatus.Text=$"Imported {count} texture(s) for {collection.Name}. "+collection.TextureDetails;
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException) { milkdropStatus.Text=error.Message; }
        finally { EndMilkdropImport(); }
    }
}
