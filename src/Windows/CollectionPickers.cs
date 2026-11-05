using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace Pulsive;

public sealed partial class MainWindow
{
    sealed record CollectionChoiceRow(string Kind,string Key,string Name,bool Expanded=false,int Index=-1,ShuffleOption? Option=null);
    readonly Dictionary<string,bool> styleGroups=new();
    ListBox? styleCollectionList;
    ListBox shuffleCollectionList=null!;
    string? selectedShuffleCollection;
    bool syncingShuffleCollections;

    void InitializeStylePicker()
    {
        modeSelect.DropDownOpened+=(_,_)=>
        {
            if(modeSelect.Template.FindName("PART_Popup",modeSelect) is not Popup { Child:Border glass }) return;
            styleCollectionList??=CreateCollectionList(260);
            glass.Child=styleCollectionList; RefreshStyleCollectionRows();
            styleCollectionList.Focus();
        };
    }
    void SetShuffleEnabled(bool enabled)
    {
        prefs.ShuffleEnabled=enabled; shuffleToggle.IsChecked=enabled; ResetShuffle(); Save();
    }
    ListBox CreateCollectionList(double maximumHeight)
    {
        var list=new ListBox { MaxHeight=maximumHeight,MinWidth=250,MaxWidth=380,Background=Brushes.Transparent,Foreground=TextColor,BorderThickness=new Thickness(0),Padding=new Thickness(0),Template=milkdropList.Template,ItemContainerStyle=milkdropList.ItemContainerStyle,ItemTemplate=CollectionChoiceTemplate() };
        VirtualizingPanel.SetIsVirtualizing(list,true); VirtualizingPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);
        list.AddHandler(ButtonBase.ClickEvent,new RoutedEventHandler((_,e)=>
        {
            if(e.OriginalSource is not ToggleButton { DataContext:CollectionChoiceRow { Kind:"Header" } row }) return;
            e.Handled=true; styleGroups[row.Key]=!row.Expanded; RefreshStyleCollectionRows();
        }));
        list.PreviewMouseLeftButtonUp+=(_,e)=>
        {
            if(ChoiceRowAt(e.OriginalSource) is { Kind:"Style" } row) { ChooseStyle(row); e.Handled=true; }
        };
        list.PreviewKeyDown+=(_,e)=>
        {
            if(e.Key==Key.Escape) { modeSelect.IsDropDownOpen=false; e.Handled=true; }
            if(e.Key==Key.Enter&&list.SelectedItem is CollectionChoiceRow { Kind:"Style" } row) { ChooseStyle(row); e.Handled=true; }
        };
        return list;
    }
    void ChooseStyle(CollectionChoiceRow row) { modeSelect.SelectedIndex=row.Index; modeSelect.IsDropDownOpen=false; }
    static CollectionChoiceRow? ChoiceRowAt(object? source)
    {
        for(var current=source as DependencyObject;current!=null;current=current is Visual?VisualTreeHelper.GetParent(current):LogicalTreeHelper.GetParent(current))
            if(current is FrameworkElement { DataContext:CollectionChoiceRow row }) return row;
        return null;
    }
    void RefreshStyleCollectionRows()
    {
        if(styleCollectionList==null) return;
        var rows=new List<CollectionChoiceRow>();
        foreach(var group in visualChoices.Select((choice,index)=>(choice,index)).GroupBy(item=>item.choice.Milkdrop?.Collection.Id??"native"))
        {
            string name=group.First().choice.Milkdrop?.Collection.Name??"Pulsive";
            bool expanded=styleGroups.TryGetValue(group.Key,out bool value)?value:group.Key is "native" or "bundled";
            rows.Add(new("Header",group.Key,$"{name} · {group.Count()}",expanded));
            if(expanded) rows.AddRange(group.Select(item=>new CollectionChoiceRow("Style",group.Key,item.choice.Name,Index:item.index)));
        }
        styleCollectionList.ItemsSource=rows;
        styleCollectionList.SelectedItem=rows.FirstOrDefault(row=>row.Kind=="Style"&&row.Index==modeSelect.SelectedIndex);
    }
    void RefreshShuffleRows()
    {
        var groups=shuffleOptions.GroupBy(option=>option.GroupKey).ToArray();
        if(!groups.Any(group=>group.Key==selectedShuffleCollection)) selectedShuffleCollection=groups.FirstOrDefault()?.Key;
        syncingShuffleCollections=true;
        try
        {
            var rows=groups.Select(group=>new CollectionChoiceRow("Collection",group.Key,$"{group.First().Group} · {group.Count()}")).ToArray();
            shuffleCollectionList.ItemsSource=rows;
            shuffleCollectionList.SelectedItem=rows.FirstOrDefault(row=>row.Key==selectedShuffleCollection);
        }
        finally { syncingShuffleCollections=false; }
        shuffleList.ItemsSource=shuffleOptions.Where(option=>option.GroupKey==selectedShuffleCollection).Select(option=>new CollectionChoiceRow("Shuffle",option.GroupKey,option.Name,Option:option)).ToArray();
    }
    ListBox CreateSidebar(DataTemplate template,double height)
    {
        var list=new ListBox { Height=height,MinWidth=0,AllowDrop=true,Background=Brushes.Transparent,Foreground=TextColor,BorderBrush=new SolidColorBrush(Color.FromArgb(40,255,255,255)),BorderThickness=new Thickness(1),Padding=new Thickness(6),FocusVisualStyle=null,Template=milkdropList.Template,ItemContainerStyle=milkdropList.ItemContainerStyle,ItemTemplate=template };
        VirtualizingPanel.SetIsVirtualizing(list,true); VirtualizingPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);
        return list;
    }
    static Grid CollectionPanels(ListBox collections,ListBox entries)
    {
        var grid=new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(175),MinWidth=125 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        collections.Margin=new Thickness(0,0,8,0); grid.Children.Add(collections);
        Grid.SetColumn(entries,1); grid.Children.Add(entries); return grid;
    }
    static DataTemplate CollectionChoiceTemplate()=>(DataTemplate)XamlReader.Parse("""
    <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
      <Grid>
        <ToggleButton x:Name="Header" Visibility="Collapsed" IsChecked="{Binding Expanded,Mode=OneWay}" Cursor="Hand" FocusVisualStyle="{x:Null}">
          <ToggleButton.Template><ControlTemplate TargetType="ToggleButton">
            <Grid Background="Transparent" Margin="10,8" MinHeight="22">
              <Grid.ColumnDefinitions><ColumnDefinition Width="20"/><ColumnDefinition/></Grid.ColumnDefinitions>
              <Path x:Name="Arrow" Data="M 2,2 L 7,7 L 2,12" Width="10" Height="14" Stroke="#C6CBD3" StrokeThickness="1.5" VerticalAlignment="Center" RenderTransformOrigin="0.5,0.5"/>
              <TextBlock Grid.Column="1" Text="{Binding Name}" Foreground="#EBEDF0" FontWeight="SemiBold" FontSize="12" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
            </Grid>
            <ControlTemplate.Triggers><Trigger Property="IsChecked" Value="True"><Setter TargetName="Arrow" Property="RenderTransform"><Setter.Value><RotateTransform Angle="90"/></Setter.Value></Setter></Trigger></ControlTemplate.Triggers>
          </ControlTemplate></ToggleButton.Template>
        </ToggleButton>
        <TextBlock x:Name="Style" Text="{Binding Name}" ToolTip="{Binding Name}" Foreground="#EBEDF0" Margin="30,8,10,8" TextTrimming="CharacterEllipsis" FontSize="12"/>
        <CheckBox x:Name="Option" Visibility="Collapsed" IsChecked="{Binding Option.IsIncluded,Mode=TwoWay,UpdateSourceTrigger=PropertyChanged}" ToolTip="{Binding Name}">
          <CheckBox.Template><ControlTemplate TargetType="CheckBox">
            <Grid Background="Transparent" Margin="20,8,10,8" MinHeight="24">
              <Grid.ColumnDefinitions><ColumnDefinition Width="22"/><ColumnDefinition/></Grid.ColumnDefinitions>
              <Border Width="21" Height="21" CornerRadius="7" Background="#15FFFFFF" BorderBrush="#50FFFFFF" BorderThickness="1" VerticalAlignment="Center">
                <Path x:Name="Tick" Data="M 3,8 L 7,12 L 14,3" Stroke="#F5F6F8" StrokeThickness="1.6" Width="17" Height="16" HorizontalAlignment="Center" VerticalAlignment="Center" Visibility="Collapsed"/>
              </Border>
              <TextBlock Grid.Column="1" Text="{Binding Name}" Foreground="#EBEDF0" FontSize="12" Margin="12,0,0,0" TextTrimming="CharacterEllipsis" VerticalAlignment="Center"/>
            </Grid>
            <ControlTemplate.Triggers><Trigger Property="IsChecked" Value="True"><Setter TargetName="Tick" Property="Visibility" Value="Visible"/></Trigger></ControlTemplate.Triggers>
          </ControlTemplate></CheckBox.Template>
        </CheckBox>
      </Grid>
      <DataTemplate.Triggers>
        <DataTrigger Binding="{Binding Kind}" Value="Header"><Setter TargetName="Header" Property="Visibility" Value="Visible"/><Setter TargetName="Style" Property="Visibility" Value="Collapsed"/></DataTrigger>
        <DataTrigger Binding="{Binding Kind}" Value="Collection"><Setter TargetName="Style" Property="Margin" Value="10,10"/><Setter TargetName="Style" Property="FontWeight" Value="SemiBold"/></DataTrigger>
        <DataTrigger Binding="{Binding Kind}" Value="Shuffle"><Setter TargetName="Option" Property="Visibility" Value="Visible"/><Setter TargetName="Style" Property="Visibility" Value="Collapsed"/></DataTrigger>
      </DataTemplate.Triggers>
    </DataTemplate>
    """);
}
