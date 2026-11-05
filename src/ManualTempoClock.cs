namespace Pulsive;

internal sealed class ManualTempoClock
{
    readonly Queue<double> intervals=new();
    double phaseStart,lastTap=double.NaN;
    long tapBeat;
    internal double Bpm { get; private set; }=120;
    internal double FirstTap { get; private set; }=double.NaN;
    internal void SetBpm(double bpm,double now)
    {
        Bpm=Math.Clamp(bpm,20,400); phaseStart=now; lastTap=FirstTap=double.NaN; tapBeat=0; intervals.Clear();
    }
    internal long Count(double now)=>tapBeat+(long)Math.Floor(Math.Max(0,now-phaseStart)*Bpm/60+1e-7);
    internal bool Tap(double now)
    {
        bool first=double.IsNaN(lastTap)||now-lastTap>Math.Max(3,180/Bpm)||now<lastTap;
        if(first)
        {
            intervals.Clear(); FirstTap=phaseStart=lastTap=now; tapBeat=0; return true;
        }
        double interval=now-lastTap;
        if(interval<.15) return false;
        intervals.Enqueue(interval); while(intervals.Count>7) intervals.Dequeue();
        var sorted=intervals.Order().ToArray(); double median=sorted[sorted.Length/2];
        var accepted=sorted.Where(value=>Math.Abs(value-median)<=median*.25).ToArray();
        Bpm=Math.Clamp(60/accepted.Average(),20,400);
        tapBeat++; phaseStart=lastTap=now;
        return false;
    }
}
