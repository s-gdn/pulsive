using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pulsive;

public sealed class BackdropSurface : Border
{
    static BackdropSurface()
    {
        // A constructor-local Background would override template bindings and
        // triggers, including the selected settings tab's accent fill.
        BackgroundProperty.OverrideMetadata(typeof(BackdropSurface),new FrameworkPropertyMetadata(Brushes.Transparent,FrameworkPropertyMetadataOptions.AffectsRender));
        CornerRadiusProperty.OverrideMetadata(typeof(BackdropSurface),new FrameworkPropertyMetadata(new CornerRadius(18),FrameworkPropertyMetadataOptions.AffectsRender));
    }
    internal static BackdropService? Service { get; set; }
    internal BackdropService? registered;
    BitmapSource? backdrop;
    Rect sourceBounds;
    bool? black;
    double blurRadius=14;
    public double BlurRadius
    {
        get=>blurRadius;
        set { if(blurRadius==value) return; blurRadius=value; InvalidateVisual(); }
    }
    public bool Adaptive { get; set; } = true;
    public double TintOpacity { get; set; } = .065;
    public bool Outline { get; set; } = true;
    public bool HoverHighlight { get; set; } = true;
    public bool RequireFrame { get; set; }
    static readonly Brush glassLight=GlassGradient(Colors.White,18,4,0);
    static readonly Brush subtleLight=GlassGradient(Colors.White,8,2,0);
    static readonly Brush lightEdge=GlassGradient(Colors.White,65,14,32);
    static readonly Brush darkEdge=GlassGradient(Colors.Black,45,12,32);
    static readonly Brush grain=CreateGrain();
    static Brush CreateGrain()
    {
        const int size=64;
        var pixels=new byte[size*size*4]; var random=new Random(0x612A);
        for(int i=0;i<pixels.Length;i+=4)
        {
            byte value=(byte)random.Next(4);
            pixels[i]=pixels[i+1]=pixels[i+2]=value; pixels[i+3]=3;
        }
        var image=BitmapSource.Create(size,size,96,96,PixelFormats.Pbgra32,null,pixels,size*4); image.Freeze();
        var brush=new ImageBrush(image) { TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,size,size),Stretch=Stretch.Fill };
        brush.Freeze(); return brush;
    }
    static Brush GlassGradient(Color color,byte top,byte middle,byte bottom)
    {
        Color Alpha(byte a)=>Color.FromArgb(a,color.R,color.G,color.B);
        var brush=new LinearGradientBrush { StartPoint=new Point(0,0),EndPoint=new Point(.25,1) };
        brush.GradientStops.Add(new GradientStop(Alpha(top),0));
        brush.GradientStops.Add(new GradientStop(Alpha(middle),.45));
        brush.GradientStops.Add(new GradientStop(Alpha(bottom),1));
        brush.Freeze(); return brush;
    }
    public BackdropSurface()
    {
        RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.LowQuality);
        SetValue(TextBlock.ForegroundProperty,Brushes.White);
        Loaded+=(_,_)=> { registered=Service; registered?.Register(this); };
        Unloaded+=(_,_)=> { registered?.Unregister(this); registered=null; };
        SizeChanged+=(_,_)=>InvalidateVisual();
    }
    internal Rect ScreenBounds()
    {
        var a=PointToScreen(new Point()); var b=PointToScreen(new Point(ActualWidth,ActualHeight)); return new Rect(a,b);
    }
    internal void SetFrame(DesktopPixels raw,BitmapSource blurred)
    {
        backdrop=blurred; sourceBounds=raw.Bounds;
        if(Adaptive)
        {
            var forced=registered?.ContrastBackground?.Invoke(this);
            double luminance;
            if(forced.HasValue) luminance=BackgroundBlur.Luminance(forced.Value);
            else
            {
                var rect=ScreenBounds(); luminance=0;
                for(int y=1;y<=3;y++) for(int x=1;x<=5;x++) luminance+=BackgroundBlur.Luminance(raw.Sample(rect.X+rect.Width*x/6,rect.Y+rect.Height*y/4))/15;
            }
            black=BackgroundBlur.UseBlack(luminance,black); ApplyContrast();
        }
        InvalidateVisual();
    }
    internal void SetContrastBackground(Color? color)
    {
        if(!Adaptive||!color.HasValue) return;
        black=BackgroundBlur.UseBlack(BackgroundBlur.Luminance(color.Value),black); ApplyContrast(); InvalidateVisual();
    }
    void ApplyContrast()
    {
            for(DependencyObject? parent=VisualTreeHelper.GetParent(this);parent!=null;parent=VisualTreeHelper.GetParent(parent))
                if(parent is BackdropSurface { Adaptive:false,TintOpacity:>=.5 }) { black=false; break; }
            Brush foreground=black==true?Brushes.Black:Brushes.White;
            SetValue(TextBlock.ForegroundProperty,foreground);
            for(DependencyObject? parent=VisualTreeHelper.GetParent(this);parent!=null;parent=VisualTreeHelper.GetParent(parent))
            {
                if(parent is Button or ComboBox or System.Windows.Controls.ContextMenu) { ((Control)parent).SetCurrentValue(Control.ForegroundProperty,foreground); break; }
            }
    }
    internal bool? UsesBlack => black;
    internal bool NativeActive { get; private set; }
    internal void SetNative(bool enabled) { if(NativeActive!=enabled) { NativeActive=enabled; InvalidateVisual(); } }
    internal bool BackdropReady => NativeActive||backdrop!=null;
    protected override void OnRender(DrawingContext dc)
    {
        if(ActualWidth<1||ActualHeight<1) return;
        if(RequireFrame&&BlurRadius<=0) return;
        if(RequireFrame&&!NativeActive&&backdrop is null) return;
        var rect=new Rect(0,0,ActualWidth,ActualHeight);
        double radius=Math.Min(CornerRadius.TopLeft,Math.Min(ActualWidth,ActualHeight)/2);
        dc.PushOpacity(RequireFrame?BlurStrength.Amount(BlurRadius):1);
        dc.PushClip(new RectangleGeometry(rect,radius,radius));
        if(!NativeActive&&backdrop!=null&&!sourceBounds.IsEmpty&&PresentationSource.FromVisual(this)!=null)
        {
            var screen=ScreenBounds();
            var crop=new Rect((screen.X-sourceBounds.X)/sourceBounds.Width*backdrop.PixelWidth,(screen.Y-sourceBounds.Y)/sourceBounds.Height*backdrop.PixelHeight,screen.Width/sourceBounds.Width*backdrop.PixelWidth,screen.Height/sourceBounds.Height*backdrop.PixelHeight);
            dc.DrawRectangle(new ImageBrush(backdrop) { ViewboxUnits=BrushMappingMode.Absolute,Viewbox=crop,Stretch=Stretch.Fill },null,rect);
        }
        // A quiet fallback also keeps controls usable if a protected app cannot be sampled.
        byte alpha=(byte)(255*(backdrop is null&&!NativeActive?.72:TintOpacity));
        dc.DrawRectangle(new SolidColorBrush(black==true?Color.FromArgb(alpha,255,255,255):Color.FromArgb(alpha,0,0,0)),null,rect);
        dc.DrawRectangle(RequireFrame?subtleLight:glassLight,null,rect);
        dc.DrawRectangle(grain,null,rect);
        if(Background is SolidColorBrush fill&&fill.Color.A>0) dc.DrawRectangle(fill,null,rect);
        if(HoverHighlight&&IsMouseOver&&!RequireFrame) dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(18,black==true?(byte)0:(byte)255,black==true?(byte)0:(byte)255,black==true?(byte)0:(byte)255)),null,rect);
        dc.Pop();
        if(Outline) dc.DrawRoundedRectangle(null,new Pen(black==true?darkEdge:lightEdge,1),new Rect(.5,.5,Math.Max(0,ActualWidth-1),Math.Max(0,ActualHeight-1)),radius,radius);
        dc.Pop();
    }
}
