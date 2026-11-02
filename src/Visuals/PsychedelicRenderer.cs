using System.Windows;
using System.Windows.Media;

namespace Pulsive;

// Analytic trails suggest feedback without screen captures or frame-buffer history.
// Work is bounded, the sample buffer is reused, and palette materials are cached.
internal sealed class PsychedelicRenderer
{
    const int Samples=160,Colours=64;
    readonly Point[] points=new Point[Samples+1];
    readonly Pen[] lines=new Pen[Colours],halos=new Pen[Colours];
    readonly Brush[] fills=new Brush[Colours];
    (int Palette,Color Start,Color End,double Intensity) material;
    bool hasMaterial;
    double lastTime=double.NaN,clock,low,mid,high,energy;

    internal void Draw(DrawingContext dc,int mode,double w,double h,double time,AudioEngine audio,double intensity,int palette,Color start,Color end)
    {
        var next=(palette,start,end,intensity);
        if(!hasMaterial||material!=next)
        {
            material=next; hasMaterial=true;
            for(int i=0;i<Colours;i++)
            {
                Color Colour(byte alpha)=>Visualizer.ColorForPalette(palette,i/(double)(Colours-1),start,end,(byte)(alpha*Math.Clamp(intensity,0,1)));
                var line=new SolidColorBrush(Colour(205)); line.Freeze();
                var halo=new SolidColorBrush(Colour(18)); halo.Freeze();
                var fill=new SolidColorBrush(Colour(8)); fill.Freeze(); fills[i]=fill;
                lines[i]=new Pen(line,1.25); lines[i].Freeze();
                halos[i]=new Pen(halo,6); halos[i].Freeze();
            }
        }
        double dt=double.IsNaN(lastTime)?1d/60:Math.Clamp(time-lastTime,0,.08);
        lastTime=time;
        double Average(int first,int count)
        {
            double sum=0;
            for(int i=first;i<first+count;i++) sum+=audio.Bands[i];
            return sum/count;
        }
        double Smooth(double current,double target)=>current+(target-current)*(1-Math.Exp(-dt*(target>current?12:3.5)));
        low=Smooth(low,Average(0,18)); mid=Smooth(mid,Average(24,36)); high=Smooth(high,Average(65,30)); energy=Smooth(energy,audio.Level);
        clock+=dt*(.35+low*.75);
        double size=Math.Min(w,h),cx=w*.5,cy=h*.52;
        dc.PushOpacity(.38+.62*Math.Clamp(energy+mid*.45,0,1));
        if(mode==8) Kaleidoscope(dc,cx,cy,size);
        else if(mode==9) Vortex(dc,cx,cy,size);
        else Silk(dc,w,h);
        dc.Pop();
    }

    void Stroke(DrawingContext dc,double colour,double opacity=1,bool closed=false,bool fill=false)
    {
        int index=Math.Clamp((int)(colour*(Colours-1)),0,Colours-1);
        var geometry=new StreamGeometry();
        using(var path=geometry.Open())
        {
            path.BeginFigure(points[0],fill,closed);
            path.PolyLineTo(points,true,false);
        }
        geometry.Freeze();
        dc.PushOpacity(Math.Clamp(opacity,0,1));
        dc.DrawGeometry(fill?fills[index]:null,halos[index],geometry);
        dc.DrawGeometry(null,lines[index],geometry);
        dc.Pop();
    }

    void Kaleidoscope(DrawingContext dc,double cx,double cy,double size)
    {
        const int folds=8;
        double spin=clock*.12;
        for(int echo=5;echo>=0;echo--)
        {
            double phase=clock-echo*.12;
            for(int ring=0;ring<5;ring++)
            {
                for(int i=0;i<=Samples;i++)
                {
                    double a=i*Math.Tau/Samples;
                    double radius=size*(.09+ring*.057)*(1+.23*Math.Cos(a*folds+phase*.6)+.08*Math.Sin(a*folds*2-phase*.8));
                    radius+=size*(.012+low*.025)*Math.Cos(a*folds-phase);
                    double twist=spin+.09*Math.Sin(a*folds+phase*.5)*(1+mid);
                    points[i]=new Point(cx+Math.Cos(a+twist)*radius,cy+Math.Sin(a+twist)*radius);
                }
                Stroke(dc,.5+.5*Math.Sin(ring*.62+echo*.15+clock*.2-1.3),(1-echo/6.5)*(.65+low*.35),true,echo==0);
            }
        }
    }

    void Vortex(DrawingContext dc,double cx,double cy,double size)
    {
        const int rings=32;
        // A logarithmic tunnel scrolls outward; its ends fade before wrapping.
        for(int ring=0;ring<rings;ring++)
        {
            double depth=(ring/(double)rings+clock*.065)%1;
            double radius=size*.035*Math.Exp(depth*3.05),opacity=Math.Sin(depth*Math.PI);
            double drift=(1-depth)*size*.10;
            double x=cx+Math.Sin(clock*.6+depth*2)*drift,y=cy+Math.Cos(clock*.43+depth*2)*drift;
            for(int i=0;i<=Samples;i++)
            {
                double a=i*Math.Tau/Samples;
                double warp=1+(.06+low*.10)*Math.Sin(a*5+depth*8-clock*.8)+(.025+high*.025)*Math.Cos(a*9-depth*6+clock*.4);
                double angle=a+depth*2.7+clock*.22;
                points[i]=new Point(x+Math.Cos(angle)*radius*warp,y+Math.Sin(angle)*radius*warp);
            }
            Stroke(dc,.5+.5*Math.Sin(depth*5-clock*.35),opacity,true);
        }
    }

    void Silk(DrawingContext dc,double w,double h)
    {
        // Counter-flowing sheets bend into interference ribbons.
        for(int sheet=0;sheet<2;sheet++)
        for(int line=0;line<25;line++)
        {
            double v=line/24d;
            for(int i=0;i<=Samples;i++)
            {
                double u=i/(double)Samples;
                double x=((sheet==0?u:v)-.5)*2,y=((sheet==0?v:u)-.5)*2;
                double r=Math.Sqrt(x*x+y*y),a=Math.Atan2(y,x);
                double bend=(.16+low*.15)*Math.Sin(x*3.6+y*2+clock*(sheet==0?.65:-.5));
                bend+=(.06+mid*.09)*Math.Sin(r*6-clock*.7+sheet*2);
                a+=.3*Math.Sin(clock*.25)+(.35+low*.2)*Math.Exp(-r*r)*Math.Sin(clock*.35);
                double px=Math.Cos(a)*r,py=Math.Sin(a)*r;
                if(sheet==0) py+=bend; else px+=bend;
                points[i]=new Point(w*(.5+px*.47),h*(.52+py*.40));
            }
            double envelope=Math.Pow(Math.Sin(Math.PI*v),.5);
            Stroke(dc,.5+.5*Math.Sin(v*3+sheet*1.6+clock*.22),envelope*(sheet==0?.85:.65));
        }
    }
}
