using System.Windows;
using System.Windows.Controls;

namespace Pulsive;

public sealed partial class MainWindow
{
    (StackPanel Root,CheckBox Link,ComboBox Mode) BuildFeatherControls()
    {
        var panel=new StackPanel();
        var header=new Grid();
        var mode=Select(["Feather amount","Feather bounding box"],0,190); mode.HorizontalAlignment=HorizontalAlignment.Left; mode.Tag="Up";
        header.Children.Add(mode);
        var link=new CheckBox { Content="Link all sides",IsChecked=prefs.LinkFeather,Foreground=TextColor,FontSize=11,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center };
        header.Children.Add(link); panel.Children.Add(header);
        Grid Row(double[] values,List<System.Windows.Controls.Slider> sliders,RoutedPropertyChangedEventHandler<double> handler)
        {
            var grid=new Grid { Margin=new Thickness(0,10,0,10) };
            string[] names=["LEFT","TOP","RIGHT","BOTTOM"];
            for(int i=0;i<4;i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                var label=new TextBlock { Text=$"{names[i]}  {values[i]:P0}",FontSize=9,Foreground=MutedColor,Margin=new Thickness(0,0,0,5) };
                var slider=Slider(0,1,values[i],_=>{}); int index=i;
                slider.ValueChanged+=(_,e)=> { label.Text=$"{names[index]}  {e.NewValue:P0}"; slider.ToolTip=e.NewValue.ToString("P0"); };
                slider.ValueChanged+=handler; sliders.Add(slider);
                var stack=new StackPanel { Margin=new Thickness(i==0?0:16,0,0,0) }; stack.Children.Add(label); stack.Children.Add(slider); Grid.SetColumn(stack,i); grid.Children.Add(stack);
            }
            return grid;
        }
        var amount=Row([prefs.FeatherLeft,prefs.FeatherTop,prefs.FeatherRight,prefs.FeatherBottom],featherSliders,FeatherChanged);
        var bounds=Row([prefs.FeatherBoundLeft,prefs.FeatherBoundTop,prefs.FeatherBoundRight,prefs.FeatherBoundBottom],featherBoundsSliders,FeatherBoundsChanged); bounds.Visibility=Visibility.Collapsed;
        panel.Children.Add(amount); panel.Children.Add(bounds);
        var hint=new TextBlock { Text="Amount controls fade softness; 0% gives a crisp edge.",FontSize=10,Foreground=MutedColor };
        panel.Children.Add(hint);
        mode.SelectionChanged+=(_,_)=>
        {
            bool box=mode.SelectedIndex==1;
            amount.Visibility=box?Visibility.Collapsed:Visibility.Visible; bounds.Visibility=box?Visibility.Visible:Visibility.Collapsed;
            link.IsChecked=box?prefs.LinkFeatherBounds:prefs.LinkFeather;
            hint.Text=box?"Inset each box edge to choose where the fade starts.":"Amount controls fade softness; 0% gives a crisp edge.";
        };
        link.Click+=(_,_)=>
        {
            bool box=mode.SelectedIndex==1,linked=link.IsChecked==true;
            if(box) prefs.LinkFeatherBounds=linked; else prefs.LinkFeather=linked;
            if(linked)
            {
                updatingFeather=true; var sliders=box?featherBoundsSliders:featherSliders;
                foreach(var slider in sliders) slider.Value=sliders[0].Value;
                updatingFeather=false;
                if(box) ApplyFeatherBounds(); else ApplyFeatherSliders();
            }
            Save();
        };
        return (panel,link,mode);
    }
    void FeatherBoundsChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(updatingFeather) return;
        updatingFeather=true;
        if(prefs.LinkFeatherBounds) foreach(var slider in featherBoundsSliders) slider.Value=e.NewValue;
        updatingFeather=false; ApplyFeatherBounds();
    }
    void ApplyFeatherBounds()
    {
        if(featherBoundsSliders.Count!=4) return;
        prefs.FeatherBoundLeft=featherBoundsSliders[0].Value; prefs.FeatherBoundTop=featherBoundsSliders[1].Value;
        prefs.FeatherBoundRight=featherBoundsSliders[2].Value; prefs.FeatherBoundBottom=featherBoundsSliders[3].Value;
        UpdateFeather(); Save();
    }
}
