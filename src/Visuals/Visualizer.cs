using System.Windows;
using System.Windows.Media;

namespace Pulsive;

public sealed class Visualizer : FrameworkElement,IDisposable
{
    public static readonly string[] Modes = ["Aurora", "Spectrum", "Orbit", "Waveform", "Bloom", "Stardust", "Terrain", "Helix", "Kaleidoscope", "Vortex", "Liquid Silk", "Nebula", "Acid Tunnel", "Prism Flow"];
    public static readonly string[] Palettes = ["Iridescent", "Arctic", "Sunset", "Orchid", "Custom"];
    public Color CustomStart { get; set; } = Color.FromRgb(104,239,222);
    public Color CustomEnd { get; set; } = Color.FromRgb(193,122,255);
    int mode;
    public int Mode
    {
        get=>mode;
        set { if(mode==value) return; mode=value; if(value<11) { projectMRenderer?.Dispose(); projectMRenderer=null; } }
    }
    public int Palette { get; set; }
    string? milkdropPresetPath;
    internal string? MilkdropPresetPath
    {
        get=>milkdropPresetPath;
        set
        {
            if(milkdropPresetPath==value) return;
            milkdropPresetPath=value;
            if(projectMRenderer?.Error!=null) { projectMRenderer.Dispose(); projectMRenderer=null; }
        }
    }
    public bool UsePulsiveColorsForMilkdrop { get; set; }
    public bool ScaleMilkdropResolutionWithWindow { get; set; }
    string? milkdropTextureDirectory;
    internal string? MilkdropTextureDirectory
    {
        get=>milkdropTextureDirectory;
        set
        {
            if(milkdropTextureDirectory==value) return;
            milkdropTextureDirectory=value;
            if(projectMRenderer?.Error!=null) { projectMRenderer.Dispose(); projectMRenderer=null; }
        }
    }
    public double Intensity { get; set; } = .9;
    public double Zoom { get; set; } = 1;
    public double Time { get; set; }
    public AudioEngine Audio { get; }
    internal StardustField Dust { get; } = new();
    StardustRenderer? dustRenderer;
    PsychedelicRenderer? psychedelicRenderer;
    ProjectMRenderer? projectMRenderer;
    internal bool SkipPresetTransition { get; set; }
    public void Dispose() { projectMRenderer?.Dispose(); projectMRenderer=null; }
    public void SetFeather(Thickness edges,Thickness bounds=default) => OpacityMask = EdgeFeather.CreateMask(edges,bounds);
    public Visualizer(AudioEngine audio) { Audio = audio; IsHitTestVisible = false; ClipToBounds = true; RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.LowQuality); }

    Color Hue(double t, byte alpha = 255) => ColorForPalette(Palette,t,CustomStart,CustomEnd,(byte)(alpha*Intensity));
    internal static Color ColorForPalette(int palette,double t,Color start,Color end,byte alpha=255)
    {
        if(palette!=4) return PaletteColor(palette,t,alpha);
        t=Math.Clamp(t,0,1);
        return Color.FromArgb(alpha,(byte)Math.Round(start.R+(end.R-start.R)*t),(byte)Math.Round(start.G+(end.G-start.G)*t),(byte)Math.Round(start.B+(end.B-start.B)*t));
    }
    internal static Color PaletteColor(int palette,double t,byte alpha=255)
    {
        double h = palette switch { 1 => 175 + 50 * t, 2 => 325 + 85 * t, 3 => 255 + 75 * t, _ => 165 + 160 * t };
        h = (h % 360 + 360) % 360 / 60;
        double x = 1 - Math.Abs(h % 2 - 1);
        var (r,g,b) = (int)h switch { 0 => (1d,x,0d), 1 => (x,1d,0d), 2 => (0d,1d,x), 3 => (0d,x,1d), 4 => (x,0d,1d), _ => (1d,0d,x) };
        return Color.FromArgb(alpha, (byte)(50 + 205*r), (byte)(50 + 205*g), (byte)(50 + 205*b));
    }
    SolidColorBrush Brush(double t, byte a = 255) => new(Hue(t,a));
    static StreamGeometry Path(IReadOnlyList<Point> points, bool closed = false)
    {
        var geo = new StreamGeometry();
        using(var c = geo.Open()) { if(points.Count > 0) { c.BeginFigure(points[0], closed, closed); c.PolyLineTo(points.Skip(1).ToArray(), true, false); } }
        geo.Freeze(); return geo;
    }
    void Trace(DrawingContext dc, IReadOnlyList<Point> points, double hue, double width = 2, byte opacity = 230, bool closed = false)
    {
        var geo = Path(points, closed);
        foreach (var (size, alpha) in new[] { (width * 7, (byte)(opacity * .035)), (width * 3, (byte)(opacity * .12)), (width, opacity) })
        {
            if(alpha==0) continue;
            dc.DrawGeometry(null, new Pen(Brush(hue, alpha), size) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, geo);
        }
    }
    void Dot(DrawingContext dc, Point p, double radius, double hue, byte opacity = 255)
    {
        dc.DrawEllipse(Brush(hue, (byte)(opacity * .04)), null, p, radius * 5, radius * 5);
        dc.DrawEllipse(Brush(hue, (byte)(opacity * .14)), null, p, radius * 2.3, radius * 2.3);
        dc.DrawEllipse(Brush(hue, opacity), null, p, radius, radius);
    }
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if(w < 1 || h < 1) return;
        double t = Time, level = Audio.Level, bass = Audio.Bass;
        var bands = Audio.Bands;
        double cx = w / 2, cy = h * .52, scale = Math.Min(w, h);
        dc.PushTransform(new ScaleTransform(Zoom,Zoom,cx,cy));
        // Every mode retains a quiet silhouette at silence, without fabricating audio activity.
        switch(Mode)
        {
            case >=11:
                dc.PushOpacity(Math.Clamp(Intensity,0,1));
                projectMRenderer??=new();
                projectMRenderer.SkipPresetTransition=SkipPresetTransition;
                projectMRenderer.ScaleResolutionWithWindow=ScaleMilkdropResolutionWithWindow;
                var dpi=VisualTreeHelper.GetDpi(this);
                projectMRenderer.DpiScaleX=dpi.DpiScaleX; projectMRenderer.DpiScaleY=dpi.DpiScaleY;
                projectMRenderer.Draw(dc,MilkdropPresetPath??System.IO.Path.Combine(AppContext.BaseDirectory,"projectm","presets",ProjectMRenderer.Presets[Math.Clamp(Mode-11,0,2)]+".milk"),w,h,t,Audio,UsePulsiveColorsForMilkdrop?Palette:-1,CustomStart,CustomEnd,MilkdropTextureDirectory);
                SkipPresetTransition=false;
                dc.Pop();
                break;
            case 0:
                for(int layer = 0; layer < 7; layer++)
                {
                    var pts = new List<Point>();
                    for(int i=0;i<=180;i++)
                    {
                        double x = i/180d, envelope = Math.Pow(Math.Sin(Math.PI*x), .8);
                        double wave = Math.Sin(x*9 + t*.5 + layer*.38) + .35*Math.Sin(x*21-t*.7+layer*.5);
                        double audio = bands[Math.Min(95,(int)(x*95))];
                        pts.Add(new Point(w*(.06+.88*x), cy + envelope*(wave*(18+level*h*.22)+Math.Sin(x*15+t+layer*.6)*audio*h*.2) + (layer-3)*5));
                    }
                    Trace(dc,pts,layer/7d,1.4+level*1.5,(byte)(100+layer*19));
                }
                break;
            case 1:
                int count=64;
                double gap=w*.84/count, bw=Math.Max(2,gap*.5);
                for(int i=0;i<count;i++)
                {
                    double v=bands[i*96/count], bh=3+v*h*.65;
                    double x=w*.08+i*gap, y=cy-bh/2;
                    dc.DrawRoundedRectangle(Brush(i/(double)count,20),null,new Rect(x-bw,y-5,bw*3,bh+10),bw,bw);
                    dc.DrawRoundedRectangle(Brush(i/(double)count),null,new Rect(x,y,bw,bh),bw/2,bw/2);
                    Dot(dc,new Point(x+bw/2,y-5),1,i/(double)count,150);
                }
                break;
            case 2:
                double radius=scale*(.22+bass*.035);
                for(int ring=0;ring<3;ring++)
                {
                    var pts=new List<Point>();
                    for(int i=0;i<=192;i++)
                    {
                        double a=i/192d*Math.Tau, v=bands[(int)(i%192/192d*96)];
                        double r=radius+ring*7+v*scale*(.09+ring*.025);
                        pts.Add(new Point(cx+Math.Cos(a-Math.PI/2)*r,cy+Math.Sin(a-Math.PI/2)*r));
                    }
                    Trace(dc,pts,ring*.4,1.6,200,true);
                }
                for(int i=0;i<96;i++)
                {
                    double a=i/96d*Math.Tau-Math.PI/2, r=radius+18;
                    Trace(dc,[new(cx+Math.Cos(a)*r,cy+Math.Sin(a)*r),new(cx+Math.Cos(a)*(r+3+bands[i]*scale*.2),cy+Math.Sin(a)*(r+3+bands[i]*scale*.2))],i/96d,2);
                }
                break;
            case 3:
                for(int layer=3;layer>=0;layer--)
                {
                    var pts=new List<Point>();
                    for(int i=0;i<256;i++)
                    {
                        double x=i/255d, env=Math.Sin(x*Math.PI);
                        pts.Add(new Point(w*(.06+x*.88),cy+Audio.Wave[i]*h*.40*(1-layer*.14)*env+Math.Sin(x*12+t*.4+layer*.8)*3));
                    }
                    Trace(dc,pts,layer/3d,layer==0?2.3:1.2,(byte)(245-layer*48));
                }
                break;
            case 4:
                for(int petal=0;petal<8;petal++)
                {
                    var pts=new List<Point>();
                    for(int i=0;i<=240;i++)
                    {
                        double a=i/240d*Math.Tau;
                        double r=scale*(.12+.10*Math.Sin(3*a+t*.16+petal*.13)+.05*Math.Cos(5*a-t*.1))*(1+bass*.65)+bands[(i*95/240)]*scale*.08;
                        double angle=a+petal*Math.PI/4;
                        pts.Add(new Point(cx+Math.Cos(angle)*r,cy+Math.Sin(angle)*r));
                    }
                    Trace(dc,pts,petal/8d,1.1,160,true);
                }
                Dot(dc,new Point(cx,cy),2+level*7,.3);
                break;
            case 5:
                Dust.Advance(t,level,bass,bands);
                (dustRenderer??=new()).Draw(dc,Dust,w,h,cx,cy,Intensity,Palette,CustomStart,CustomEnd);
                break;
            case 6:
                for(int row=0;row<17;row++)
                {
                    var pts=new List<Point>();
                    double depth=row/16d;
                    for(int i=0;i<=96;i++)
                    {
                        double x=i/96d, env=Math.Sin(x*Math.PI);
                        double ridge=Math.Sin(x*14+row*.27+t*.3)*.35+.65;
                        pts.Add(new Point(cx+(x-.5)*w*(.35+depth*.58),h*.32+depth*h*.43-env*ridge*(12+bands[Math.Min(95,i)]*h*.18)*(0.3+depth)));
                    }
                    Trace(dc,pts,depth,1.1,(byte)(40+depth*200));
                }
                break;
            case >=8 and <=10:
                (psychedelicRenderer??=new()).Draw(dc,Mode,w,h,t,Audio,Intensity,Palette,CustomStart,CustomEnd);
                break;
            case 7:
                for(int strand=0;strand<2;strand++)
                {
                    var pts=new List<Point>();
                    for(int i=0;i<=160;i++)
                    {
                        double x=i/160d, a=x*Math.Tau*2+t*.65+strand*Math.PI;
                        double amp=25+level*h*.20+bands[Math.Min(95,(int)(x*95))]*h*.12;
                        pts.Add(new Point(w*(.08+x*.84),cy+Math.Sin(a)*amp));
                        if(i%8==0 && strand==0)
                        {
                            Trace(dc,[pts[^1],new(pts[^1].X,cy-Math.Sin(a)*amp)],x,1,90);
                            Dot(dc,pts[^1],2+level*2,x);
                        }
                    }
                    Trace(dc,pts,strand==0?.15:.85,2);
                }
                break;
        }
        dc.Pop();
    }
}
