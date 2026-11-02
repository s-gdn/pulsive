using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Pulsive;

// Geometry uses a fixed, symmetric viewport instead of font baselines.
public sealed class ToolbarIcon : Control
{
    public string Kind { get; set; }="";
    public ToolbarIcon() { Width=20; Height=20; IsHitTestVisible=false; }
    protected override void OnRender(DrawingContext dc)
    {
        var pen=new Pen(Foreground,1.6) { StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round };
        dc.PushTransform(new TranslateTransform((ActualWidth-20)/2,(ActualHeight-20)/2));
        void Line(double x,double y,double ex,double ey)=>dc.DrawLine(pen,new Point(x,y),new Point(ex,ey));
        switch(Kind)
        {
            case "─": Line(4,10,16,10); break;
            case "□": dc.DrawRoundedRectangle(null,pen,new Rect(4,4,12,12),1.5,1.5); break;
            case "×": Line(5,5,15,15); Line(15,5,5,15); break;
            case "↗": Line(5,15,15,5); Line(8,5,15,5); Line(15,5,15,12); break;
            case "⚙":
                dc.DrawEllipse(null,pen,new Point(10,10),5,5); dc.DrawEllipse(null,pen,new Point(10,10),1.8,1.8);
                for(int i=0;i<8;i++) { double a=i*Math.PI/4; Line(10+Math.Cos(a)*5,10+Math.Sin(a)*5,10+Math.Cos(a)*7.4,10+Math.Sin(a)*7.4); }
                break;
            case "◈": Line(10,2,18,10); Line(18,10,10,18); Line(10,18,2,10); Line(2,10,10,2); dc.DrawEllipse(Foreground,null,new Point(10,10),2.5,2.5); break;
        }
        dc.Pop();
    }
}
