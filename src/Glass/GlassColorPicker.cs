using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Pulsive;

internal sealed class GlassColorPicker : Button
{
    readonly Popup popup;
    readonly Canvas square=new() { Height=145,Focusable=true,Cursor=Cursors.Cross };
    readonly Ellipse marker=new() { Width=12,Height=12,Stroke=Brushes.White,StrokeThickness=2,Fill=Brushes.Transparent,IsHitTestVisible=false };
    readonly Ellipse swatch=new() { Width=17,Height=17,Margin=new Thickness(0,0,9,0) };
    readonly TextBlock valueText=new() { VerticalAlignment=VerticalAlignment.Center };
    readonly Slider hue=new() { Minimum=0,Maximum=360,Height=28,IsMoveToPointEnabled=true };
    Color selected;
    double h,s,v;
    bool updating;
    internal event EventHandler? ColorChanged;
    internal bool IsPickerOpen => popup.IsOpen;
    internal FrameworkElement PickerSurface => (FrameworkElement)popup.Child;
    internal Color SelectedColor { get=>selected; set=>SelectColor(value); }

    internal GlassColorPicker(Color initial,string title="BORDER COLOUR")
    {
        Height=30; HorizontalContentAlignment=HorizontalAlignment.Left;
        SetResourceReference(StyleProperty,typeof(Button));
        ToolTip="Choose colour";
        var face=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(11,0,11,0) };
        face.Children.Add(swatch); face.Children.Add(valueText); Content=face;
        var content=new StackPanel { Width=250 };
        content.Children.Add(new TextBlock { Text=title,Foreground=Brushes.White,FontSize=10,Margin=new Thickness(0,0,0,12) });
        var layers=new Grid();
        layers.Children.Add(new Border { Background=new LinearGradientBrush(Colors.White,Colors.Transparent,0) });
        layers.Children.Add(new Border { Background=new LinearGradientBrush(Colors.Transparent,Colors.Black,90) });
        square.Children.Add(layers); square.Children.Add(marker);
        square.SizeChanged+=(_,_)=> { layers.Width=square.ActualWidth; layers.Height=square.ActualHeight; square.Clip=new RectangleGeometry(new Rect(0,0,square.ActualWidth,square.ActualHeight),10,10); PositionMarker(); };
        square.MouseLeftButtonDown+=(_,e)=> { square.Focus(); square.CaptureMouse(); Pick(e.GetPosition(square)); e.Handled=true; };
        square.MouseMove+=(_,e)=> { if(square.IsMouseCaptured&&e.LeftButton==MouseButtonState.Pressed) Pick(e.GetPosition(square)); };
        square.MouseLeftButtonUp+=(_,e)=> { if(square.IsMouseCaptured) { Pick(e.GetPosition(square)); square.ReleaseMouseCapture(); e.Handled=true; } };
        square.KeyDown+=(_,e)=>
        {
            double step=Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?.1:.01;
            if(e.Key==Key.Left) s-=step; else if(e.Key==Key.Right) s+=step;
            else if(e.Key==Key.Up) v+=step; else if(e.Key==Key.Down) v-=step; else return;
            s=Math.Clamp(s,0,1); v=Math.Clamp(v,0,1); ApplyHsv(); e.Handled=true;
        };
        content.Children.Add(square);
        content.Children.Add(new TextBlock { Text="HUE",Foreground=Brushes.White,FontSize=9,Margin=new Thickness(0,12,0,0) });
        var hueTrack=new Grid();
        var rainbow=new LinearGradientBrush { StartPoint=new Point(0,0),EndPoint=new Point(1,0) };
        for(int i=0;i<=6;i++) rainbow.GradientStops.Add(new GradientStop(FromHsv(i*60,1,1),i/6d));
        hueTrack.Children.Add(new Border { Background=rainbow,Height=7,CornerRadius=new CornerRadius(4),Margin=new Thickness(2,0,2,0),VerticalAlignment=VerticalAlignment.Center });
        hueTrack.Children.Add(hue); content.Children.Add(hueTrack);
        hue.ValueChanged+=(_,e)=> { if(!updating) { h=e.NewValue; ApplyHsv(); } };
        var presets=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,10,0,0) };
        foreach(var color in new[] { Colors.White,Color.FromRgb(104,239,222),Colors.DeepSkyBlue,Colors.MediumPurple,Colors.HotPink,Colors.Orange,Colors.Red })
        {
            var button=new Button { Width=29,Height=29,Margin=new Thickness(0,0,6,0),Content=new Ellipse { Width=18,Height=18,Fill=new SolidColorBrush(color) },ToolTip=$"#{color.R:X2}{color.G:X2}{color.B:X2}" };
            button.Click+=(_,_)=>SelectColor(color); presets.Children.Add(button);
        }
        content.Children.Add(presets);
        var glass=new BackdropSurface { Child=content,Padding=new Thickness(16),Adaptive=false,TintOpacity=.85,CornerRadius=new CornerRadius(20),HoverHighlight=false };
        popup=new Popup { Child=glass,PlacementTarget=this,Placement=PlacementMode.Bottom,VerticalOffset=8,AllowsTransparency=true,StaysOpen=false };
        // Keep the popup in the logical tree so it inherits the glass control styles.
        AddLogicalChild(popup);
        Click+=(_,e)=> { if(!ReferenceEquals(e.OriginalSource,this)) return; popup.IsOpen=!popup.IsOpen; if(popup.IsOpen) square.Focus(); };
        popup.Closed+=(_,_)=>BackdropSurface.Service?.RefreshNative();
        SelectColor(initial,false);
    }
    internal void ClosePicker()=>popup.IsOpen=false;
    void Pick(Point point)
    {
        s=Math.Clamp(point.X/Math.Max(1,square.ActualWidth),0,1);
        v=1-Math.Clamp(point.Y/Math.Max(1,square.ActualHeight),0,1); ApplyHsv();
    }
    void SelectColor(Color color,bool notify=true)
    {
        double r=color.R/255d,g=color.G/255d,b=color.B/255d,max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),delta=max-min;
        v=max; s=max==0?0:delta/max;
        if(delta>0) h=((max==r?(g-b)/delta:max==g?(b-r)/delta+2:(r-g)/delta+4)*60+360)%360;
        selected=Color.FromRgb(color.R,color.G,color.B); Refresh();
        if(notify) ColorChanged?.Invoke(this,EventArgs.Empty);
    }
    void ApplyHsv() { selected=FromHsv(h,s,v); Refresh(); ColorChanged?.Invoke(this,EventArgs.Empty); }
    void Refresh()
    {
        updating=true; hue.Value=h; updating=false;
        swatch.Fill=new SolidColorBrush(selected); valueText.Text=$"#{selected.R:X2}{selected.G:X2}{selected.B:X2}";
        square.Background=new SolidColorBrush(FromHsv(h,1,1)); PositionMarker();
    }
    void PositionMarker()
    {
        Canvas.SetLeft(marker,Math.Clamp(s*square.ActualWidth-6,0,Math.Max(0,square.ActualWidth-12)));
        Canvas.SetTop(marker,Math.Clamp((1-v)*square.ActualHeight-6,0,Math.Max(0,square.ActualHeight-12)));
    }
    internal static Color FromHsv(double hue,double saturation,double value)
    {
        double h=(hue%360+360)%360/60,c=value*saturation,x=c*(1-Math.Abs(h%2-1)),m=value-c;
        var (r,g,b)=(int)h switch { 0=>(c,x,0d),1=>(x,c,0d),2=>(0d,c,x),3=>(0d,x,c),4=>(x,0d,c),_=>(c,0d,x) };
        return Color.FromRgb((byte)Math.Round((r+m)*255),(byte)Math.Round((g+m)*255),(byte)Math.Round((b+m)*255));
    }
}
