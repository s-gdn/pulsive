using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pulsive;

internal sealed class OverlayBorder : FrameworkElement
{
    internal static readonly string[] Styles=["None","Fixed color","Gradient","Glow","Reactive glow"];
    int style=-1,palette=-1;
    Color color;
    Color gradientStart,gradientEnd;
    Pen[] pens=[];
    BitmapSource[]? idleGlow,activeGlow,steadyGlow;
    double drive,bassBaseline;
    internal const double GlowPadding=40;
    internal bool ExternalGlow { get; init; }
    internal OverlayBorder() { IsHitTestVisible=false; ClipToBounds=true; }
    internal void SetAppearance(int type,Color tint,int paletteIndex,Color start=default,Color end=default)
    {
        type=Math.Clamp(type,0,Styles.Length-1);
        if(style==type&&color==tint&&palette==paletteIndex&&(paletteIndex!=4||(gradientStart==start&&gradientEnd==end))) return;
        gradientStart=start; gradientEnd=end;
        style=type; color=tint; palette=paletteIndex;
        Pen Stroke(Brush brush,double width) { brush.Freeze(); var pen=new Pen(brush,width); pen.Freeze(); return pen; }
        Brush Solid(byte alpha)=>new SolidColorBrush(Color.FromArgb(alpha,tint.R,tint.G,tint.B));
        if(type==2)
        {
            var gradient=new LinearGradientBrush { StartPoint=new Point(0,0),EndPoint=new Point(1,1) };
            for(int i=0;i<4;i++) gradient.GradientStops.Add(new GradientStop(Visualizer.ColorForPalette(paletteIndex,i/3d,start,end),i/3d));
            pens=[Stroke(gradient,1.3)];
        }
        else pens=[Stroke(Solid(230),1.2)];
        idleGlow=activeGlow=steadyGlow=null;
        InvalidateVisual();
    }
    internal void Update(double level,double bass,double dt)
    {
        dt=Math.Clamp(dt,0,.1);
        double energy=Math.Clamp((bass-.18)/.65,0,1);
        // Bass bands are logarithmic and tend to sit high during music. Detect
        // fresh rises above a slower baseline so individual beats remain visible.
        bassBaseline+=(energy-bassBaseline)*(1-Math.Exp(-dt/.65));
        double onset=Math.Clamp((energy-bassBaseline)*5,0,1);
        double target=Math.Clamp(energy*.3+level*.25+onset*.9,0,1);
        drive+=(target-drive)*(1-Math.Exp(-dt/(target>drive?.025:.18)));
        if(style==4) InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        if(style<=0||ActualWidth<20||ActualHeight<20) return;
        if(ExternalGlow)
        {
            if(style<3) return;
            EnsureGlows();
            if(style==3) DrawGlow(dc,steadyGlow!);
            else { DrawGlow(dc,idleGlow!); dc.PushOpacity(.08+.92*drive); DrawGlow(dc,activeGlow!); dc.Pop(); }
            return;
        }
        // Align the crisp stroke's outside edge with the window. Clip the wider
        // glow inward instead of reserving an empty gutter around the border.
        dc.PushClip(new RectangleGeometry(new Rect(0,0,ActualWidth,ActualHeight),24,24));
        double inset=pens[^1].Thickness/2;
        var rect=new Rect(inset,inset,ActualWidth-inset*2,ActualHeight-inset*2);
        dc.PushOpacity(style==4?.65+.35*drive:.8);
        foreach(var pen in pens) dc.DrawRoundedRectangle(null,pen,rect,24-inset,24-inset);
        dc.Pop();
        dc.Pop();
    }
    void DrawGlow(DrawingContext dc,BitmapSource[] tiles)
    {
        double corner=Math.Min(80,Math.Min(ActualWidth,ActualHeight)/2);
        for(int y=0;y<3;y++) for(int x=0;x<3;x++)
        {
            if(x==1&&y==1) continue;
            double left=x==0?0:x==1?corner:ActualWidth-corner,top=y==0?0:y==1?corner:ActualHeight-corner;
            double width=x==1?ActualWidth-2*corner:corner,height=y==1?ActualHeight-2*corner:corner;
            if(width>0&&height>0) dc.DrawImage(tiles[y*3+x],new Rect(left,top,width,height));
        }
    }
    void EnsureGlows()
    {
        if(idleGlow!=null) return;
        BitmapSource[] Glow(int reach,double peak)
        {
            const int size=180;
            var drawing=new DrawingVisual();
            using(var dc=drawing.RenderOpen())
            {
                dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude,new RectangleGeometry(new Rect(0,0,size,size)),new RectangleGeometry(new Rect(40,40,100,100),24,24)));
                double inset=pens[^1].Thickness/2;
                var rect=new Rect(40+inset,40+inset,100-inset*2,100-inset*2);
                double previous=0,sigma=reach/3d;
                for(int distance=reach;distance>=1;distance--)
                {
                    double coverage=peak*Math.Exp(-distance*distance/(2*sigma*sigma));
                    byte alpha=(byte)Math.Clamp(Math.Round((coverage-previous)/(1-previous)*255),0,255);
                    previous=coverage;
                    if(alpha==0) continue;
                    var brush=new SolidColorBrush(Color.FromArgb(alpha,color.R,color.G,color.B));
                    dc.DrawRoundedRectangle(null,new Pen(brush,distance*2),rect,24-pens[^1].Thickness/2,24-pens[^1].Thickness/2);
                }
                dc.Pop();
            }
            var bitmap=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32); bitmap.Render(drawing); bitmap.Freeze();
            var tiles=new BitmapSource[9];
            for(int y=0;y<3;y++) for(int x=0;x<3;x++)
            {
                var crop=new Int32Rect(x==0?0:x==1?80:100,y==0?0:y==1?80:100,x==1?20:80,y==1?20:80);
                var tile=new CroppedBitmap(bitmap,crop); tile.Freeze(); tiles[y*3+x]=tile;
            }
            return tiles;
        }
        // Small cached corner/edge textures scale to any window size. Frames
        // only crossfade these tiles: no repeated glow rasterisation or blur.
        idleGlow=Glow(18,.22); activeGlow=Glow(34,.72); steadyGlow=Glow(30,.65);
    }
}
