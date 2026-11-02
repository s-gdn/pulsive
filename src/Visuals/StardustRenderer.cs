using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pulsive;

// Cache the expensive glow rasterisation. A frame is a few batched trail paths
// and one image draw per star, with no per-star opacity groups or gradients.
internal sealed class StardustRenderer
{
    const int Hues=16,Levels=32,SpriteSize=48;
    readonly BitmapSource[] sprites=new BitmapSource[Hues*Levels];
    readonly Color[] colors=new Color[Hues];
    readonly Pen?[] trailPens=new Pen[Hues*Levels],haloPens=new Pen[Hues*Levels],gleamPens=new Pen[Hues*Levels];
    readonly StreamGeometry[] trails=Enumerable.Range(0,Hues).Select(_=>new StreamGeometry()).ToArray();
    readonly StreamGeometryContext?[] contexts=new StreamGeometryContext[Hues];
    readonly bool[] hasTrail=new bool[Hues];
    int palette=-1;
    Color customStart,customEnd;

    internal void Draw(DrawingContext dc,StardustField field,double width,double height,double cx,double cy,double intensity,int currentPalette,Color start,Color end)
    {
        if(palette!=currentPalette||(currentPalette==4&&(customStart!=start||customEnd!=end))) { customStart=start; customEnd=end; BuildSprites(currentPalette); }
        double power=0;
        foreach(var star in field.Stars) power+=star.Power;
        power/=field.Stars.Length;
        int trailLevel=Level((8+power*58)/255*intensity);
        Array.Clear(hasTrail);
        try
        {
            for(int i=0;i<Hues;i++) contexts[i]=trails[i].Open();
            foreach(var star in field.Stars)
            {
                if(star.Depth<=.55) continue;
                var oldest=star.Trail[(star.Head+1)%star.Trail.Length];
                double dx=(star.X-oldest.X)*width,dy=(star.Y-oldest.Y)*height;
                if(dx*dx+dy*dy<.09) continue; // Subpixel trails contribute no visible detail.
                int hue=Hue(star.Hue); hasTrail[hue]=true;
                var context=contexts[hue]!;
                context.BeginFigure(new Point(cx+oldest.X*width,cy+oldest.Y*height),false,false);
                for(int i=2;i<=star.Trail.Length;i++)
                {
                    var point=star.Trail[(star.Head+i)%star.Trail.Length];
                    context.LineTo(new Point(cx+point.X*width,cy+point.Y*height),true,false);
                }
                context.LineTo(new Point(cx+star.X*width,cy+star.Y*height),true,false);
            }
        }
        finally { for(int i=0;i<Hues;i++) { contexts[i]?.Close(); contexts[i]=null; } }
        for(int i=0;i<Hues;i++)
        {
            if(!hasTrail[i]||trailLevel==0) continue;
            dc.DrawGeometry(null,Pen(i,trailLevel,haloPens,2.6,.15),trails[i]);
            dc.DrawGeometry(null,Pen(i,trailLevel,trailPens,.85,1),trails[i]);
        }
        foreach(var star in field.Stars)
        {
            double twinkle=field.Twinkle(star);
            double radius=(star.Size+star.Depth*star.Depth*1.5)*(1+star.Power*.85);
            double opacity=Math.Clamp((28+star.Depth*58+star.Power*175)*twinkle,0,255)/255*intensity;
            int level=Level(opacity),hue=Hue(star.Hue);
            if(level==0) continue;
            double x=cx+star.X*width,y=cy+star.Y*height,extent=radius*4.5;
            dc.DrawImage(sprites[hue*Levels+level],new Rect(x-extent,y-extent,extent*2,extent*2));
            if(star.Depth>.9&&star.Size>1)
            {
                double gleam=radius*(1.6+star.Power*.6);
                int glow=Level((15+star.Power*85)*twinkle/255*intensity);
                var pen=Pen(hue,glow,gleamPens,.6,1);
                dc.DrawLine(pen,new Point(x-gleam,y),new Point(x+gleam,y));
                dc.DrawLine(pen,new Point(x,y-gleam),new Point(x,y+gleam));
            }
        }
    }
    Pen Pen(int hue,int level,Pen?[] cache,double width,double alpha)
    {
        int index=hue*Levels+level;
        if(cache[index] is { } cached) return cached;
        var color=colors[hue]; color.A=(byte)Math.Round(level/(double)(Levels-1)*255*alpha);
        var brush=new SolidColorBrush(color); brush.Freeze();
        var pen=new Pen(brush,width) { StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round };
        pen.Freeze(); cache[index]=pen; return pen;
    }
    void BuildSprites(int currentPalette)
    {
        palette=currentPalette;
        Array.Clear(trailPens); Array.Clear(haloPens); Array.Clear(gleamPens);
        var shape=new (double Glow,double Body,double Core)[SpriteSize*SpriteSize];
        double scale=SpriteSize/9d;
        for(int y=0;y<SpriteSize;y++) for(int x=0;x<SpriteSize;x++)
        {
            double dx=(x+.5-SpriteSize/2d)/scale,dy=(y+.5-SpriteSize/2d)/scale;
            double distance=Math.Sqrt(dx*dx+dy*dy),t=distance/4.5;
            double glow=t<.3?180+(55-180)*t/.3:t<.7?55+(5-55)*(t-.3)/.4:t<1?5*(1-t)/.3:0;
            shape[y*SpriteSize+x]=(glow/255,Math.Clamp((.65-distance)*scale+.5,0,1),Math.Clamp((.35-distance)*scale+.5,0,1));
        }
        for(int hue=0;hue<Hues;hue++)
        {
            var color=colors[hue]=Visualizer.ColorForPalette(currentPalette,hue/(double)Hues,customStart,customEnd);
            double coreR=(byte)(175+color.R*.3),coreG=(byte)(175+color.G*.3),coreB=(byte)(175+color.B*.3);
            for(int level=0;level<Levels;level++)
            {
                double opacity=level/(double)(Levels-1);
                var pixels=new byte[SpriteSize*SpriteSize*4];
                for(int i=0;i<shape.Length;i++)
                {
                    var sample=shape[i];
                    double ag=sample.Glow*opacity,ab=sample.Body*opacity,ac=sample.Core*opacity;
                    double colored=ab+ag*(1-ab),alpha=ac+colored*(1-ac);
                    int p=i*4;
                    pixels[p]=(byte)Math.Round(coreB*ac+color.B*colored*(1-ac));
                    pixels[p+1]=(byte)Math.Round(coreG*ac+color.G*colored*(1-ac));
                    pixels[p+2]=(byte)Math.Round(coreR*ac+color.R*colored*(1-ac));
                    pixels[p+3]=(byte)Math.Round(alpha*255);
                }
                var bitmap=BitmapSource.Create(SpriteSize,SpriteSize,96,96,PixelFormats.Pbgra32,null,pixels,SpriteSize*4);
                bitmap.Freeze(); sprites[hue*Levels+level]=bitmap;
            }
        }
    }
    static int Hue(double hue)=>Math.Clamp((int)(hue*Hues),0,Hues-1);
    static int Level(double opacity)=>Math.Clamp((int)Math.Round(opacity*(Levels-1)),0,Levels-1);
}
