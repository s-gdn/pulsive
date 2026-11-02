namespace Pulsive;

// Keep motion independent of instantaneous FFT values. Fixed steps make the
// same sound feel consistent at different frame rates and on repeated repaints.
internal sealed class StardustField
{
    internal sealed class Star
    {
        internal double X,Y,Vx,Vy,HomeX,HomeY,Depth,Phase,Hue,Size,Power;
        internal readonly (double X,double Y)[] Trail=new (double,double)[8];
        internal int Head;
    }
    const double Step=1d/120;
    internal const double BaseGlow=.08;
    const double BassResponseGain=4;
    internal Star[] Stars { get; }
    double lastTime=double.NaN,accumulator,clock,bass;
    internal double BassDrive => bass;
    int trailStep;

    internal StardustField()
    {
        var random=new Random(0x5A17);
        Stars=Enumerable.Range(0,220).Select(_=>
        {
            double angle=random.NextDouble()*Math.Tau,radius=Math.Sqrt(random.NextDouble());
            var star=new Star
            {
                X=Math.Cos(angle)*radius*.44,Y=Math.Sin(angle)*radius*.33,
                Depth=.15+random.NextDouble()*.85,Phase=random.NextDouble()*Math.Tau,
                Hue=random.NextDouble(),Size=.45+random.NextDouble()*.8,Power=BaseGlow
            };
            star.HomeX=star.X; star.HomeY=star.Y;
            Array.Fill(star.Trail,(star.X,star.Y));
            return star;
        }).OrderBy(s=>s.Depth).ToArray();
    }

    internal void Advance(double time,double inputLevel,double inputBass,float[] spectrum)
    {
        if(!double.IsFinite(time)) return;
        double elapsed=double.IsNaN(lastTime)?Step:time-lastTime;
        lastTime=time;
        if(elapsed<=0) return;
        // Do not fast-forward or teleport when returning from a different mode.
        if(elapsed>.25) elapsed=Step;
        double lowPeak=0,lowMean=0;
        // The first 16 logarithmic bins cover sub/kick energy, roughly 35–100 Hz.
        int count=Math.Min(16,spectrum.Length);
        for(int i=0;i<count;i++) { double value=Safe(spectrum[i]); lowPeak=Math.Max(lowPeak,value); lowMean+=value; }
        double lowEnergy = .65 * lowPeak
                 + .35 * lowMean / Math.Max(1, count);
        // Keep the noise gate, but give moderate kicks enough drive to be visible.
        // Apply the boost after shaping so quiet background bass stays quiet.
        double targetBass = Math.Clamp(BassResponseGain * Math.Pow(
            Math.Clamp((lowEnergy - .35) / .55, 0, 1),
            2), 0, 1);
        accumulator+=elapsed;
        while(accumulator+1e-10>=Step)
        {
            accumulator-=Step; clock+=Step;
            // Fast kick attack, relaxed release; broadband loudness and treble
            // deliberately do not control Stardust speed or glow.
            bass=Smooth(bass,targetBass,.018,.20);
            bool record=++trailStep%4==0;
            double inertia=1-Math.Exp(-Step*8);
            foreach(var star in Stars)
            {
                double power=BaseGlow+(1-BaseGlow)*bass*(.85+.15*star.Depth);
                star.Power=Smooth(star.Power,power,.025,.10);
                double flowX=Math.Sin(star.Y*8+clock*.27+star.Phase)+.45*Math.Cos(star.X*9-clock*.19);
                double flowY=Math.Cos(star.X*7-clock*.23+star.Phase*.6)+.4*Math.Sin(star.Y*11+clock*.16);
                double speed=(.004+star.Depth*.009)*(.09+bass*7);
                // Bass supplies a gentle outward pressure through velocity, never a position jump.
                double push=bass*.08*star.Depth;
                double targetX=flowX*speed+star.HomeX*push-(star.X-star.HomeX)*.30-Math.Sign(star.X)*Math.Max(0,Math.Abs(star.X)-.42)*2;
                double targetY=flowY*speed+star.HomeY*push-(star.Y-star.HomeY)*.30-Math.Sign(star.Y)*Math.Max(0,Math.Abs(star.Y)-.32)*2;
                star.Vx+=(targetX-star.Vx)*inertia; star.Vy+=(targetY-star.Vy)*inertia;
                star.X+=star.Vx*Step; star.Y+=star.Vy*Step;
                if(record)
                {
                    star.Head=(star.Head+1)%star.Trail.Length;
                    star.Trail[star.Head]=(star.X,star.Y);
                }
            }
        }
    }
    internal double Twinkle(Star star) => .82+.18*Math.Sin(clock*(.55+star.Depth*.45)+star.Phase);
    static double Safe(double value) => double.IsFinite(value)?Math.Clamp(value,0,1):0;
    static double Smooth(double current,double target,double attack,double release)
        => current+(target-current)*(1-Math.Exp(-Step/(target>current?attack:release)));
}
