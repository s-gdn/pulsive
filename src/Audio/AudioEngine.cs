using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace Pulsive;

public sealed class AudioEngine : IDisposable
{
    const int Size = 2048;
    readonly object gate = new();
    readonly float[] samples = new float[Size];
    int position;
    WasapiLoopbackCapture? capture;
    MMDevice? device;
    readonly MMDeviceEnumerator enumerator = new();
    string? selectedId;
    DateTime lastPacket;
    volatile bool demo;
    public string Status { get; private set; } = "Starting audio…";
    public string DeviceName { get; private set; } = "Default output";
    public float[] Bands { get; } = new float[96];
    public float[] Wave { get; } = new float[256];
    internal float[] Pcm { get; } = new float[Size];
    internal long PcmVersion { get; private set; }
    public float Level { get; private set; }
    public float Bass { get; private set; }
    internal BeatTracker Beats { get; }=new();
    public float Gain { get; set; } = 1.5f;
    public bool Demo
    {
        get=>demo;
        set { lock(gate) { if(demo==value) return; demo=value; Beats.Reset(); } }
    }
    public bool HasAudio => DateTime.UtcNow - lastPacket < TimeSpan.FromSeconds(1) && Level > .002f;
    public List<(string Id, string Name)> Devices() => enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).Select(d => { using(d) return (d.ID, d.FriendlyName); }).ToList();

    public void Start(string? id = null)
    {
        selectedId = id;
        Stop();
        try
        {
            device = id is null ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : enumerator.GetDevice(id);
            DeviceName = device.FriendlyName;
            capture = new WasapiLoopbackCapture(device);
            capture.DataAvailable += OnData;
            capture.RecordingStopped += (_, e) => { if (e.Exception != null) Status = "Output disconnected · choose another device"; };
            capture.StartRecording();
            Status = "Listening";
        }
        catch (Exception e) { Status = "Audio unavailable · " + e.Message; Stop(); }
    }

    public void CheckDefaultDevice()
    {
        if (selectedId != null) return;
        try { using var current = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); if (current.ID != device?.ID || capture is null || Status.StartsWith("Output disconnected")) Start(); }
        catch { Status = "No output device · connect speakers or headphones"; }
    }

    void OnData(object? sender, WaveInEventArgs e)
    {
        if(e.BytesRecorded==0) return;
        var format = capture?.WaveFormat;
        if (format is null) return;
        int bytes = format.BitsPerSample / 8;
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat || (format is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71"));
        lock (gate)
        {
            int beatStart=position;
            for (int i = 0; i + format.BlockAlign <= e.BytesRecorded; i += format.BlockAlign)
            {
                float sample = 0;
                for (int ch = 0; ch < format.Channels; ch++)
                {
                    int p = i + ch * bytes;
                    sample += isFloat && bytes == 4 ? BitConverter.ToSingle(e.Buffer, p) : bytes switch
                    {
                        2 => BitConverter.ToInt16(e.Buffer, p) / 32768f,
                        3 => ((e.Buffer[p] | e.Buffer[p + 1] << 8 | e.Buffer[p + 2] << 16) << 8) / 2147483648f,
                        4 => BitConverter.ToInt32(e.Buffer, p) / 2147483648f,
                        _ => 0
                    };
                }
                samples[position] = float.IsFinite(sample) ? sample / format.Channels : 0;
                position = (position + 1) % Size;
                if(position==0)
                {
                    if(!demo) Beats.ProcessSamples(samples.AsSpan(beatStart,Size-beatStart),format.SampleRate);
                    beatStart=0;
                }
            }
            if(!demo&&position>beatStart) Beats.ProcessSamples(samples.AsSpan(beatStart,position-beatStart),format.SampleRate);
            lastPacket = DateTime.UtcNow;
        }
    }

    public void Update(double time, double dt)
    {
        float[] snapshot = new float[Size];
        lock(gate)
        {
            for(int i=0;i<Size;i++) snapshot[i]=samples[(position+i)%Size];
            if(!demo&&DateTime.UtcNow-lastPacket>TimeSpan.FromSeconds(2)) Beats.MarkSilent();
        }
        if (Demo)
        {
            double envelope=.3*Math.Exp(-(time%.5)*20);
            for(int i=0;i<Size;i++) snapshot[i]=(float)(envelope*Math.Sin(i*Math.Tau*70/48000d)+.02*Math.Sin(i*Math.Tau*440/48000d));
            Beats.AdvanceRms(Math.Sqrt(envelope*envelope/2+.0002),dt);
        }
        else if (DateTime.UtcNow - lastPacket > TimeSpan.FromMilliseconds(180)) Array.Clear(snapshot);
        Analyze(snapshot, capture?.WaveFormat.SampleRate ?? 48000, dt);
    }

    internal void Analyze(float[] snapshot, int sampleRate, double dt)
    {
        for(int i=0;i<Pcm.Length;i++) Pcm[i]=i<snapshot.Length?Math.Clamp(snapshot[i]*Gain,-1,1):0;
        PcmVersion++;
        var fft = new Complex[Size];
        double energy = 0;
        for (int i = 0; i < Size; i++) { energy += snapshot[i] * snapshot[i]; fft[i].X = snapshot[i] * (float)(.5 - .5 * Math.Cos(2 * Math.PI * i / (Size - 1))); }
        FastFourierTransform.FFT(true, 11, fft);
        float blend = (float)(1 - Math.Exp(-dt * 18));
        Level += ((float)Math.Min(1, Math.Sqrt(energy / Size) * Gain * 3) - Level) * blend;
        for (int i = 0; i < Wave.Length; i++) Wave[i] += (snapshot[i * Size / Wave.Length] * Gain - Wave[i]) * blend;
        for (int b = 0; b < Bands.Length; b++)
        {
            int lo = Math.Clamp((int)(35 * Math.Pow(18000d / 35, b / (double)Bands.Length) * Size / sampleRate), 1, Size / 2 - 1);
            int hi = Math.Clamp((int)(35 * Math.Pow(18000d / 35, (b + 1d) / Bands.Length) * Size / sampleRate), lo + 1, Size / 2);
            double peak = 0;
            for (int k = lo; k < hi; k++) peak = Math.Max(peak, Math.Sqrt(fft[k].X * fft[k].X + fft[k].Y * fft[k].Y));
            float target = (float)Math.Clamp((20 * Math.Log10(Math.Max(1e-7, peak * Gain)) + 70) / 62, 0, 1);
            Bands[b] += (target - Bands[b]) * (float)(1 - Math.Exp(-dt * (target > Bands[b] ? 30 : 7)));
        }
        Bass = Bands.Take(22).Average();
    }
    void Stop() { if(capture != null) { capture.DataAvailable -= OnData; capture.StopRecording(); capture.Dispose(); capture = null; } device?.Dispose(); device = null; lock(gate) Array.Clear(samples); Beats.Reset(); }
    public void Dispose() { Stop(); enumerator.Dispose(); }
}
