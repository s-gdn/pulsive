using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace Pulsive;

internal static class DesktopBackground
{
    static BitmapSource? wallpaper;
    static string wallpaperPath="";
    static DateTime wallpaperChecked;
    static byte[]? wallpaperPixels;
    static int wallpaperWidth,wallpaperHeight;
    internal static Rect DisplayBounds(Rect area)
    {
        Rect result=area;
        MonitorCallback callback=(IntPtr monitor,IntPtr dc,ref NativeRect bounds,IntPtr data)=>
        {
            var rect=new Rect(bounds.Left,bounds.Top,bounds.Right-bounds.Left,bounds.Bottom-bounds.Top);
            if(rect.IntersectsWith(area)) result.Union(rect);
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,callback,IntPtr.Zero); return result;
    }

    // Rebuild only the pixels behind this HWND. Capturing those windows separately
    // avoids sampling Pulsive itself, and does not hide Pulsive from screen recorders.
    internal static DesktopPixels Capture(IntPtr owner,Rect bounds,int maximum=640)
    {
        double scale=Math.Min(1,maximum/Math.Max(bounds.Width,bounds.Height));
        int width=Math.Max(1,(int)Math.Ceiling(bounds.Width*scale)),height=Math.Max(1,(int)Math.Ceiling(bounds.Height*scale));
        var output=new byte[width*height*4];
        var uncovered=new List<Rect> { bounds };
        int inspected=0;
        for(var hwnd=GetWindow(owner,2);hwnd!=IntPtr.Zero&&uncovered.Count>0&&inspected++<250;hwnd=GetWindow(hwnd,2))
        {
            if(!IsWindowVisible(hwnd)||IsIconic(hwnd)||IsHungAppWindow(hwnd)) continue;
            if(hwnd==owner||GetWindow(hwnd,4)==owner) continue;
            var name=new StringBuilder(64); GetClassName(hwnd,name,name.Capacity);
            if(name.ToString() is "Progman" or "WorkerW"||name.ToString().StartsWith("Pulsive.NativeBackdrop",StringComparison.Ordinal)) continue;
            if(DwmGetWindowAttribute(hwnd,14,out int cloaked,4)==0&&cloaked!=0) continue;
            if(!GetWindowRect(hwnd,out var r)) continue;
            if(r.Right<=r.Left||r.Bottom<=r.Top) continue;
            var windowBounds=new Rect(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top);
            var pieces=uncovered.Select(u=>Rect.Intersect(u,windowBounds)).Where(p=>!p.IsEmpty&&p.Width>0&&p.Height>0).ToList();
            if(pieces.Count==0) continue;
            var source=DesktopCapture.CaptureWindow(hwnd);
            if(source is null) continue;
            foreach(var piece in pieces)
            {
                int x0=Math.Clamp((int)Math.Floor((piece.Left-bounds.Left)/bounds.Width*width),0,width);
                int x1=Math.Clamp((int)Math.Ceiling((piece.Right-bounds.Left)/bounds.Width*width),0,width);
                int y0=Math.Clamp((int)Math.Floor((piece.Top-bounds.Top)/bounds.Height*height),0,height);
                int y1=Math.Clamp((int)Math.Ceiling((piece.Bottom-bounds.Top)/bounds.Height*height),0,height);
                var columns=new int[x1-x0];
                for(int x=x0;x<x1;x++) columns[x-x0]=Math.Clamp((int)((bounds.X+(x+.5)*bounds.Width/width-source.Bounds.X)/source.Bounds.Width*source.Width),0,source.Width-1)*4;
                for(int y=y0;y<y1;y++)
                {
                    int sourceRow=Math.Clamp((int)((bounds.Y+(y+.5)*bounds.Height/height-source.Bounds.Y)/source.Bounds.Height*source.Height),0,source.Height-1)*source.Width*4;
                    for(int x=x0;x<x1;x++)
                    {
                        int a=sourceRow+columns[x-x0],p=(y*width+x)*4;
                        output[p]=source.Pixels[a]; output[p+1]=source.Pixels[a+1]; output[p+2]=source.Pixels[a+2];
                    }
                }
            }
            uncovered=uncovered.SelectMany(u=>Subtract(u,windowBounds)).ToList();
        }
        if(uncovered.Count>0)
        {
            var background=Wallpaper(bounds,width,height);
            foreach(var piece in uncovered)
            {
                int x0=Math.Clamp((int)Math.Ceiling((piece.Left-bounds.Left)/bounds.Width*width),0,width),x1=Math.Clamp((int)Math.Ceiling((piece.Right-bounds.Left)/bounds.Width*width),0,width);
                int y0=Math.Clamp((int)Math.Ceiling((piece.Top-bounds.Top)/bounds.Height*height),0,height),y1=Math.Clamp((int)Math.Ceiling((piece.Bottom-bounds.Top)/bounds.Height*height),0,height);
                for(int y=y0;y<y1;y++) Buffer.BlockCopy(background,(y*width+x0)*4,output,(y*width+x0)*4,(x1-x0)*4);
            }
        }
        return new DesktopPixels(bounds,width,height,output);
    }

    internal static IEnumerable<Rect> Subtract(Rect from,Rect cover)
    {
        var hit=Rect.Intersect(from,cover);
        if(hit.IsEmpty) { yield return from; yield break; }
        if(hit.Top>from.Top) yield return new Rect(from.Left,from.Top,from.Width,hit.Top-from.Top);
        if(hit.Bottom<from.Bottom) yield return new Rect(from.Left,hit.Bottom,from.Width,from.Bottom-hit.Bottom);
        if(hit.Left>from.Left) yield return new Rect(from.Left,hit.Top,hit.Left-from.Left,hit.Height);
        if(hit.Right<from.Right) yield return new Rect(hit.Right,hit.Top,from.Right-hit.Right,hit.Height);
    }

    static byte[] Wallpaper(Rect bounds,int width,int height)
    {
        var bytes=new byte[width*height*4];
        if(DateTime.UtcNow-wallpaperChecked>TimeSpan.FromSeconds(15))
        {
            wallpaperChecked=DateTime.UtcNow;
            var path=new StringBuilder(1024);
            SystemParametersInfo(0x73,path.Capacity,path,0);
            if(path.ToString()!=wallpaperPath)
            {
                wallpaperPath=path.ToString(); wallpaper=null; wallpaperPixels=null;
                try
                {
                    if(File.Exists(wallpaperPath))
                    {
                        var image=new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad;
                        image.UriSource=new Uri(wallpaperPath); image.DecodePixelWidth=1920; image.EndInit(); image.Freeze(); wallpaper=image;
                    }
                }
                catch { wallpaper=null; }
            }
        }
        if(wallpaper is null) return bytes;
        if(wallpaperPixels is null)
        {
            var pixels=new FormatConvertedBitmap(wallpaper,PixelFormats.Bgr32,null,0);
            wallpaperWidth=pixels.PixelWidth; wallpaperHeight=pixels.PixelHeight;
            wallpaperPixels=new byte[wallpaperWidth*wallpaperHeight*4]; pixels.CopyPixels(wallpaperPixels,wallpaperWidth*4,0);
        }
        var raw=wallpaperPixels;
        int sw=GetSystemMetrics(0),sh=GetSystemMetrics(1);
        double fill=Math.Max(sw/(double)wallpaperWidth,sh/(double)wallpaperHeight);
        double left=(sw-wallpaperWidth*fill)/2,top=(sh-wallpaperHeight*fill)/2;
        for(int y=0;y<height;y++) for(int x=0;x<width;x++)
        {
            int sx=Math.Clamp((int)((bounds.X+(x+.5)*bounds.Width/width-left)/fill),0,wallpaperWidth-1);
            int sy=Math.Clamp((int)((bounds.Y+(y+.5)*bounds.Height/height-top)/fill),0,wallpaperHeight-1);
            int a=(sy*wallpaperWidth+sx)*4,b=(y*width+x)*4;
            bytes[b]=raw[a]; bytes[b+1]=raw[a+1]; bytes[b+2]=raw[a+2];
        }
        return bytes;
    }
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left,Top,Right,Bottom; }
    delegate bool MonitorCallback(IntPtr monitor,IntPtr dc,ref NativeRect bounds,IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorCallback callback,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder name,int maximum);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd,uint command);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsHungAppWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool SystemParametersInfo(uint action,int count,StringBuilder value,uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd,int attribute,out int value,int size);
}

internal static class BackgroundBlur
{
    internal static DesktopPixels Apply(DesktopPixels source,double radius)
    {
        float sigma=(float)(Math.Max(0,radius)*source.Width/source.Bounds.Width*.6);
        if(sigma<.1f) return source;
        var pin=GCHandle.Alloc(source.Pixels,GCHandleType.Pinned);
        try
        {
            var info=new SKImageInfo(source.Width,source.Height,SKColorType.Bgra8888,SKAlphaType.Opaque);
            using var input=new SKBitmap();
            if(!input.InstallPixels(info,pin.AddrOfPinnedObject(),source.Width*4)) throw new InvalidOperationException("Cannot prepare background pixels.");
            using var output=new SKBitmap(info);
            using var canvas=new SKCanvas(output);
            using var filter=SKImageFilter.CreateBlur(sigma,sigma,SKShaderTileMode.Clamp,null);
            using var paint=new SKPaint { ImageFilter=filter,BlendMode=SKBlendMode.Src };
            canvas.DrawBitmap(input,0,0,paint); canvas.Flush();
            var pixels=new byte[source.Pixels.Length]; Marshal.Copy(output.GetPixels(),pixels,0,pixels.Length);
            return new DesktopPixels(source.Bounds,source.Width,source.Height,pixels);
        }
        finally { pin.Free(); }
    }
    internal static double Luminance(Color color)
    {
        double Linear(byte b) { double s=b/255d; return s<=.04045?s/12.92:Math.Pow((s+.055)/1.055,2.4); }
        return .2126*Linear(color.R)+.7152*Linear(color.G)+.0722*Linear(color.B);
    }
    internal static bool UseBlack(double luminance,bool? previous=null)
        => previous.HasValue&&Math.Abs(luminance-.179)<.02?previous.Value:luminance>.179;
}
