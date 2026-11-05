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
        if(selectedMilkdropCollection==collection) return;
        selectedMilkdropCollection=collection; milkdropList.SelectedItems.Clear(); RefreshMilkdropList();
    }
    void ShowNewMilkdropCollection()
    {
        if(importingMilkdrop) return;
        if(milkdropList.ContextMenu!=null) milkdropList.ContextMenu.IsOpen=false;
        presetDialog=new PresetNameDialog(settingsWindow,Resources,name=>
        {
            selectedMilkdropCollection=milkdropLibrary.CreateCollection(name);
            RefreshMilkdropList();
            var header=milkdropCollectionList.Items.OfType<MilkdropRow>().First(row=>row.Kind=="Collection"&&row.Collection==selectedMilkdropCollection);
            milkdropCollectionList.SelectedItem=header; milkdropCollectionList.ScrollIntoView(header);
        },promptTitle:"NEW COLLECTION",saveLabel:"Create");
        try { presetDialog.ShowDialog(); }
        finally { presetDialog=null; backdrops.RefreshNative(); }
    }
    void ConfigureMilkdropCollections()
    {
        milkdropList.PreviewMouseDown+=(_,e)=>
        {
            if(MilkdropRowAt(e.OriginalSource) is { } row) SetMilkdropImportTarget(row.Collection);
        };
        milkdropList.SelectionChanged+=(_,_)=> { if(!batchingMilkdropSelection) UpdateMilkdropSelection(); };
        ConfigureMilkdropSelectionDrag();
        milkdropList.AddHandler(ButtonBase.ClickEvent,new RoutedEventHandler(async (_,e)=>
        {
            if(e.OriginalSource is not Button { Tag:"ImportTextures",DataContext:MilkdropRow row }) return;
            e.Handled=true;
            var picker=new OpenFolderDialog { Title="Import textures for "+row.Collection.Name };
            if(picker.ShowDialog(settingsWindow)==true) await ImportMilkdropTextureFolder(row.Collection,picker.FolderName);
        }));
        milkdropList.PreviewKeyDown+=(_,e)=>
        {
            if(e.Key==Key.Delete) { DeleteMilkdropSelection(); e.Handled=true; return; }
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
                foreach(var collection in milkdropLibrary.Collections.Where(collection=>collection.Id is not ("bundled" or "imported")))
                {
                    var item=new MenuItem { Header=collection.Name,IsEnabled=SelectedMilkdropEntries().Any(selected=>!selected.Bundled&&selected.Collection!=collection) };
                    item.Click+=(_,_)=>
                    {
                        try { milkdropLibrary.MoveMany(SelectedMilkdropEntries().Where(item=>!item.Bundled),collection); RefreshMilkdropList(); RebuildVisualiserChoices(); Save(); }
                        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException) { milkdropStatus.Text=error.Message; }
                    };
                    move.Items.Add(item);
                }
                menu.Items.Add(move);
            }
        };
        menu.Closed+=(_,_)=>backdrops.RefreshNative();
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
            var header=milkdropCollectionList.Items.OfType<MilkdropRow>().FirstOrDefault(row=>row.Kind=="Collection"&&row.Collection==selectedMilkdropCollection);
            if(header!=null) { milkdropCollectionList.SelectedItem=header; milkdropCollectionList.ScrollIntoView(header); }
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
