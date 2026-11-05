using NAudio.Dsp;

namespace Pulsive;

// Reuses the visualiser FFT. Spectral flux is sampled at 50 Hz; short-window
// autocorrelation runs twice a second, without another capture stream or FFT.
internal sealed class BeatTracker
{
    const double Step=.02;
    readonly double[] previousSpectrum=new double[24];
    readonly double[] history=new double[300];
    readonly double[] correlations=new double[52];
    int position,count;
    double time,untilSample,untilEstimate,mean,variance,lastOnset=-100,nextBeat;
    double silence,pendingTempo,fluxEnvelope;
    int pendingCount;
    bool wasAbove;
    internal double Bpm { get; private set; }
    internal double Confidence { get; private set; }
    internal long BeatCount { get; private set; }
    internal bool IsLocked=>Bpm>0&&Confidence>=.32&&silence<.25&&time-lastOnset<Math.Max(1.5,180/Bpm);

    internal void Reset()
    {
        Array.Clear(previousSpectrum); Array.Clear(history); Array.Clear(correlations);
        position=count=pendingCount=0; time=untilSample=untilEstimate=mean=variance=silence=pendingTempo=nextBeat=fluxEnvelope=0;
        Bpm=Confidence=0; BeatCount=0; lastOnset=-100; wasAbove=false;
    }
    internal void Analyze(Complex[] spectrum,int sampleRate,double rms,double dt)
    {
        double flux=0;
        for(int band=0;band<previousSpectrum.Length;band++)
        {
            int lo=Math.Clamp((int)(40*Math.Pow(200,band/24d)*spectrum.Length/sampleRate),1,spectrum.Length/2-1);
            int hi=Math.Clamp((int)(40*Math.Pow(200,(band+1)/24d)*spectrum.Length/sampleRate),lo+1,spectrum.Length/2);
            double energy=0;
            for(int bin=lo;bin<hi;bin++) energy+=spectrum[bin].X*spectrum[bin].X+spectrum[bin].Y*spectrum[bin].Y;
            double magnitude=Math.Log(1+Math.Sqrt(energy/(hi-lo))*1000);
            flux+=Math.Max(0,magnitude-previousSpectrum[band])*(band<9?1.5:1);
            previousSpectrum[band]=magnitude;
        }
        Advance(flux/24,rms,dt);
    }
    internal void Advance(double onset,double rms,double dt)
    {
        if(!double.IsFinite(dt)||dt<=0) return;
        dt=Math.Min(dt,.1); time+=dt;
        silence=rms<.00015?silence+dt:0;
        double blend=1-Math.Exp(-dt*2),difference=onset-mean;
        mean+=difference*blend; variance+=(difference*difference-variance)*blend;
        bool above=onset>Math.Max(.002,mean+Math.Sqrt(Math.Max(0,variance))*.65);
        if(above&&!wasAbove&&time-lastOnset>.2)
        {
            lastOnset=time;
            if(IsLocked)
            {
                double period=60/Bpm;
                double error=time-(nextBeat-period);
                error-=Math.Round(error/period)*period;
                if(Math.Abs(error)<period*.22) nextBeat+=error*.2;
            }
        }
        wasAbove=above;
        fluxEnvelope=Math.Max(onset,fluxEnvelope*Math.Exp(-dt*25));
        untilSample+=dt;
        while(untilSample>=Step)
        {
            untilSample-=Step; history[position]=silence>.15?0:fluxEnvelope;
            position=(position+1)%history.Length; count=Math.Min(count+1,history.Length);
        }
        untilEstimate+=dt;
        if(untilEstimate>=.5) { untilEstimate=0; Estimate(); }
        if(silence>1.5)
        {
            Bpm=Confidence=0; count=0; pendingCount=0;
        }
        if(IsLocked&&time>=nextBeat)
        {
            BeatCount++;
            double period=60/Bpm;
            nextBeat+=period;
            if(nextBeat<=time) nextBeat=time+period;
        }
    }
    double Sample(int age)=>history[(position-1-age+history.Length)%history.Length];
    void Estimate()
    {
        int window=Math.Min(count,200);
        if(window<120) return;
        double average=0; for(int i=0;i<window;i++) average+=Sample(i)/window;
        double bestScore=0; int bestLag=0;
        // 60–200 BPM. Prefer the first strong periodic peak over its multiples.
        for(int lag=14;lag<=51;lag++)
        {
            double cross=0,a2=0,b2=0;
            for(int i=0;i<window-lag;i++)
            {
                double a=Sample(i)-average,b=Sample(i+lag)-average;
                cross+=a*b; a2+=a*a; b2+=b*b;
            }
            correlations[lag]=a2*b2>1e-12?Math.Max(0,cross/Math.Sqrt(a2*b2)):0;
        }
        for(int lag=15;lag<=50;lag++)
        {
            if(correlations[lag]<.32||correlations[lag]<correlations[lag-1]||correlations[lag]<correlations[lag+1]) continue;
            // A short period supported by its multiples beats a stronger
            // long-period coincidence (for example 180 BPM mistaken for 60).
            double score=correlations[lag]-.025*lag/50;
            if(lag*2<=51) score+=Harmonic(lag*2)*.5;
            if(lag*3<=51) score+=Harmonic(lag*3)*.25;
            if(Bpm>0&&Math.Abs(60/(lag*Step)-Bpm)/Bpm<.08) score+=.12;
            if(score>bestScore) { bestScore=score; bestLag=lag; }
        }
        if(bestLag==0||bestScore<.32) { Confidence*=.75; return; }
        double left=correlations[bestLag-1],center=correlations[bestLag],right=correlations[bestLag+1];
        double curve=left-2*center+right;
        double offset=Math.Abs(curve)>1e-6?Math.Clamp(.5*(left-right)/curve,-.5,.5):0;
        double tempo=Math.Clamp(60/((bestLag+offset)*Step),60,200);
        if(Bpm>0&&Math.Abs(tempo-Bpm)/Bpm>.12)
        {
            if(Math.Abs(tempo-pendingTempo)/tempo<.06) pendingCount++; else { pendingTempo=tempo; pendingCount=1; }
            if(pendingCount<2) { Confidence*=.8; return; }
        }
        bool relock=Bpm==0||Math.Abs(tempo-Bpm)/Bpm>.12||!IsLocked;
        Bpm=relock?tempo:Bpm+(tempo-Bpm)*.35;
        Confidence=correlations[bestLag]; pendingCount=0;
        if(relock) nextBeat=lastOnset+60/Bpm;
    }
    double Harmonic(int lag)=>Math.Max(correlations[Math.Max(14,lag-1)],Math.Max(correlations[lag],correlations[Math.Min(51,lag+1)]));
}
