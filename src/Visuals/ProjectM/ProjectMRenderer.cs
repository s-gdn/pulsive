using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pulsive;

internal sealed class ProjectMRenderer : IDisposable
{
    internal static readonly string[] Presets=["Nebula","Acid Tunnel","Prism Flow"];
    OpenGlSurface? surface;
    IntPtr instance;
    WriteableBitmap? bitmap;
    byte[] nativePixels=[],pixels=[];
    float[] pcm=[];
    string? preset;
    string? textureDirectory;
    bool texturePathsSet;
    long audioVersion=-1;
    double lastFrame=double.NegativeInfinity;
    (int Palette,Color Start,Color End)? colours;
    readonly Color[] gradient=new Color[256];
    readonly ProjectMNative.LoadFailed loadFailed;
    internal string? Error { get; private set; }
    internal string Driver=>surface?.Version??"";
    internal BitmapSource? Frame=>bitmap;
    internal bool CompatibilityRepairApplied { get; private set; }

    internal ProjectMRenderer() => loadFailed=(_,message,_)=>Error="projectM preset: "+message;

    internal void Draw(DrawingContext dc,int selected,double width,double height,double time,AudioEngine audio,int palette,Color start,Color end)
        => Draw(dc,Path.Combine(AppContext.BaseDirectory,"projectm","presets",Presets[selected]+".milk"),width,height,time,audio,palette,start,end);

    internal void Draw(DrawingContext dc,string file,double width,double height,double time,AudioEngine audio,int palette,Color start,Color end,string? textures=null)
    {
        if(Error==null)
        {
            try
            {
                double ratio=Math.Min(1,Math.Min(960/width,640/height));
                int w=Math.Max(64,(int)(width*ratio)),h=Math.Max(64,(int)(height*ratio));
                if(surface==null)
                {
                    surface=new OpenGlSurface(w,h);
                    ProjectMNative.InitialiseOpenGl();
                    instance=ProjectMNative.projectm_create();
                    if(instance==IntPtr.Zero) throw new InvalidOperationException("The projectM engine could not start on this graphics driver.");
                    ProjectMNative.projectm_set_fps(instance,30);
                    ProjectMNative.projectm_set_mesh_size(instance,48,32);
                    ProjectMNative.projectm_set_preset_locked(instance,true);
                    ProjectMNative.projectm_set_soft_cut_duration(instance,1.2);
                    ProjectMNative.projectm_set_preset_switch_failed_event_callback(instance,loadFailed,IntPtr.Zero);
                    pcm=new float[Math.Min(2048,(int)ProjectMNative.projectm_pcm_get_max_samples())];
                }
                surface.MakeCurrent();
                bool resized=bitmap==null||bitmap.PixelWidth!=w||bitmap.PixelHeight!=h;
                if(resized)
                {
                    surface.Resize(w,h);
                    ProjectMNative.projectm_set_window_size(instance,(nuint)w,(nuint)h);
                    bitmap=new WriteableBitmap(w,h,96,96,PixelFormats.Pbgra32,null);
                    nativePixels=new byte[w*h*4]; pixels=new byte[w*h*4];
                }
                bool texturesChanged=!texturePathsSet||textureDirectory!=textures;
                if(texturesChanged)
                {
                    ProjectMNative.SetTextureDirectory(instance,textures);
                    textureDirectory=textures; texturePathsSet=true;
                }
                bool changed=preset!=file||texturesChanged;
                if(changed)
                {
                    if(!File.Exists(file)) throw new FileNotFoundException("A projectM preset is missing. Keep the projectm folder beside Pulsive.exe.");
                    ProjectMNative.projectm_load_preset_file(instance,file,preset!=null&&!texturesChanged);
                    CompatibilityRepairApplied=false;
                    if(Error!=null)
                    {
                        string repaired=MilkdropPresetCompatibility.RepairEquations(File.ReadAllText(file),out int repairs);
                        if(repairs>0)
                        {
                            Error=null;
                            ProjectMNative.projectm_load_preset_data(instance,repaired,false);
                            CompatibilityRepairApplied=Error==null;
                        }
                        if(Error?.Contains("Unknown exception",StringComparison.OrdinalIgnoreCase)==true)
                            Error="projectM could not load this preset. The engine supplied no diagnostic details.";
                    }
                    preset=file;
                }
                var nextColours=(palette,start,end);
                if(colours!=nextColours&&palette>=0)
                    for(int i=0;i<gradient.Length;i++) gradient[i]=Visualizer.ColorForPalette(palette,i/255d,start,end);
                if(Error==null&&(resized||changed||colours!=nextColours||time-lastFrame>=1d/30-0.001))
                {
                    if(audioVersion!=audio.PcmVersion)
                    {
                        Array.Copy(audio.Pcm,audio.Pcm.Length-pcm.Length,pcm,0,pcm.Length);
                        ProjectMNative.projectm_pcm_add_float(instance,pcm,(uint)pcm.Length,1);
                        audioVersion=audio.PcmVersion;
                    }
                    ProjectMNative.projectm_opengl_render_frame(instance);
                    surface.ReadPixels(nativePixels);
                    // MilkDrop renders against black. Use max-channel alpha to
                    // preserve its light while letting the desktop show through.
                    for(int y=0;y<h;y++)
                    for(int x=0;x<w;x++)
                    {
                        int src=((h-1-y)*w+x)*4,dst=(y*w+x)*4;
                        byte b=nativePixels[src],g=nativePixels[src+1],r=nativePixels[src+2];
                        byte alpha=Math.Max(r,Math.Max(g,b));
                        if(palette>=0)
                        {
                            var colour=gradient[(r*54+g*183+b*19)>>8];
                            r=(byte)(colour.R*alpha/255); g=(byte)(colour.G*alpha/255); b=(byte)(colour.B*alpha/255);
                        }
                        pixels[dst]=b; pixels[dst+1]=g; pixels[dst+2]=r; pixels[dst+3]=alpha;
                    }
                    bitmap!.WritePixels(new Int32Rect(0,0,w,h),pixels,w*4,0);
                    lastFrame=time;
                    colours=nextColours;
                }
            }
            catch(Exception e) when(e is InvalidOperationException or System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or IOException or TypeInitializationException)
            {
                Error="projectM unavailable: "+e.GetBaseException().Message;
                Dispose();
            }
        }
        if(Error!=null)
        {
            var text=new FormattedText(Error,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,Brushes.LightGray,1) { MaxTextWidth=Math.Max(100,width*.7) };
            dc.DrawText(text,new Point((width-text.Width)/2,(height-text.Height)/2));
        }
        else if(bitmap!=null) dc.DrawImage(bitmap,new Rect(0,0,width,height));
    }
    public void Dispose()
    {
        if(instance!=IntPtr.Zero)
        {
            if(surface?.TryMakeCurrent()==true) ProjectMNative.projectm_destroy(instance);
            instance=IntPtr.Zero;
        }
        surface?.Dispose(); surface=null; bitmap=null;
        nativePixels=[]; pixels=[]; pcm=[];
    }
}
