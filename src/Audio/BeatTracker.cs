namespace Pulsive;

// Time-domain detector: 1024 fresh PCM samples -> RMS -> adaptive threshold
// crossing -> inter-onset intervals -> BPM. Live timing follows sample counts,
// independently of the visualiser's rendering rate.
internal sealed class BeatTracker
{
    internal const int FrameSize=1024;
    const double ThresholdRatio=1.35;
    const double RearmRatio=1.1;
    const double MinimumRms=.00015;
    readonly object gate=new();
    readonly double[] intervals=new double[5];
    double time,average,silence,lastOnset=-100,frameEnergy,bpm,confidence;
    int intervalCount,intervalPosition,frameSamples,sampleRate;
    long beatCount;
    bool initialized,armed=true;
    internal readonly record struct BeatState(double Bpm,double Confidence,long BeatCount,bool IsLocked,double AudioSeconds);
    internal BeatState State { get { lock(gate) return new(bpm,confidence,beatCount,Locked,time); } }
    internal double Bpm { get { lock(gate) return bpm; } }
    internal double Confidence { get { lock(gate) return confidence; } }
    internal long BeatCount { get { lock(gate) return beatCount; } }
    internal bool IsLocked { get { lock(gate) return Locked; } }
    bool Locked=>bpm>0&&silence<Math.Max(.6,120/bpm)&&time-lastOnset<Math.Max(2,180/bpm);

    internal void Reset()
    {
        lock(gate)
        {
            ClearTempo(); time=frameEnergy=0; frameSamples=sampleRate=0; beatCount=0;
        }
    }
    void ClearTempo()
    {
        Array.Clear(intervals); intervalCount=intervalPosition=0;
        average=silence=bpm=confidence=0; lastOnset=-100; initialized=false; armed=true;
    }
    // Loopback can stop sending packets in silence. Clear stale detection state
    // without inventing samples or counting beats from the UI timer.
    internal void MarkSilent()
    {
        lock(gate)
        {
            if(!initialized&&bpm==0&&frameSamples==0) return;
            ClearTempo(); frameSamples=0; frameEnergy=0;
        }
    }
    internal void ProcessSamples(ReadOnlySpan<float> samples,int rate)
    {
        if(rate<=0) throw new ArgumentOutOfRangeException(nameof(rate));
        lock(gate)
        {
            if(sampleRate!=0&&sampleRate!=rate) { ClearTempo(); frameEnergy=0; frameSamples=0; }
            sampleRate=rate;
            foreach(float value in samples)
            {
                double sample=float.IsFinite(value)?value:0;
                frameEnergy+=sample*sample;
                if(++frameSamples!=FrameSize) continue;
                AdvanceRmsCore(Math.Sqrt(frameEnergy/FrameSize),FrameSize/(double)rate);
                frameSamples=0; frameEnergy=0;
            }
        }
    }
    // Used by generated preview audio and envelope-level verification only.
    internal void AdvanceRms(double rms,double dt)
    {
        if(!double.IsFinite(rms)||!double.IsFinite(dt)||dt<=0) return;
        lock(gate) AdvanceRmsCore(Math.Max(0,rms),dt);
    }
    void AdvanceRmsCore(double rms,double dt)
    {
        time+=dt; silence=rms<MinimumRms?silence+dt:0;
        if(!initialized) { average=rms; initialized=true; return; }
        // Compare with the preceding moving average; update it after detection
        // so the current transient cannot lift its own threshold.
        double threshold=Math.Max(MinimumRms*2,average*ThresholdRatio);
        if(rms<=Math.Max(MinimumRms,average*RearmRatio)) armed=true;
        if(armed&&rms>threshold&&time-lastOnset>=.2)
        {
            double onsetTime=time-dt*.5;
            double interval=onsetTime-lastOnset;
            lastOnset=onsetTime; armed=false; beatCount++;
            // Tolerate frame quantisation at the ends of the 40-240 BPM range.
            if(interval>=.2125&&interval<=1.725)
            {
                intervals[intervalPosition]=interval;
                intervalPosition=(intervalPosition+1)%intervals.Length;
                intervalCount=Math.Min(intervalCount+1,intervals.Length);
                Estimate();
            }
        }
        average+=(rms-average)*(1-Math.Exp(-dt/.45));
        if(silence>2) ClearTempo();
    }
    void Estimate()
    {
        if(intervalCount<3) return;
        Span<double> sorted=stackalloc double[5];
        intervals.AsSpan(0,intervalCount).CopyTo(sorted); sorted=sorted[..intervalCount]; sorted.Sort();
        double median=sorted[intervalCount/2],sum=0; int supporters=0;
        foreach(double interval in sorted)
            if(Math.Abs(interval-median)<=median*.1) { sum+=interval; supporters++; }
        if(supporters<3) return;
        double candidate=Math.Clamp(60/(sum/supporters),40,240);
        bpm=bpm==0||Math.Abs(candidate-bpm)/bpm>.1?candidate:bpm+(candidate-bpm)*.6;
        confidence=supporters/(double)intervalCount;
    }
}
