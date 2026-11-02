using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pulsive;

internal sealed record DesktopPixels(Rect Bounds, int Width, int Height, byte[] Pixels)
{
    public BitmapSource Bitmap()
    {
        var bitmap=BitmapSource.Create(Width,Height,96,96,PixelFormats.Bgr32,null,Pixels,Width*4);
        bitmap.Freeze(); return bitmap;
    }
    public Color Sample(double screenX,double screenY)
    {
        int x=Math.Clamp((int)((screenX-Bounds.X)/Bounds.Width*Width),0,Width-1);
        int y=Math.Clamp((int)((screenY-Bounds.Y)/Bounds.Height*Height),0,Height-1);
        int p=(y*Width+x)*4; return Color.FromRgb(Pixels[p+2],Pixels[p+1],Pixels[p]);
    }
}

internal static class DesktopCapture
{
    internal static DesktopPixels? CaptureWindow(IntPtr hwnd)
    {
        if(!GetWindowRect(hwnd,out var rect)) return null;
        int width=rect.Right-rect.Left,height=rect.Bottom-rect.Top;
        if(width<1||height<1||width>16384||height>16384||(long)width*height>33_554_432) return null;
        IntPtr screen=GetDC(IntPtr.Zero); if(screen==IntPtr.Zero) return null;
        IntPtr dc=IntPtr.Zero,bitmap=IntPtr.Zero,old=IntPtr.Zero;
        try
        {
            dc=CreateCompatibleDC(screen);
            var info=new BitmapInfo { Size=40,Width=width,Height=-height,Planes=1,BitCount=32,ImageSize=(uint)(width*height*4) };
            bitmap=CreateDIBSection(screen,ref info,0,out var bits,IntPtr.Zero,0);
            if(dc==IntPtr.Zero||bitmap==IntPtr.Zero||bits==IntPtr.Zero) return null;
            old=SelectObject(dc,bitmap);
            if(!PrintWindow(hwnd,dc,2)) return null;
            var pixels=new byte[width*height*4]; Marshal.Copy(bits,pixels,0,pixels.Length);
            return new DesktopPixels(new Rect(rect.Left,rect.Top,width,height),width,height,pixels);
        }
        finally
        {
            if(old!=IntPtr.Zero) SelectObject(dc,old);
            if(bitmap!=IntPtr.Zero) DeleteObject(bitmap);
            if(dc!=IntPtr.Zero) DeleteDC(dc);
            ReleaseDC(IntPtr.Zero,screen);
        }
    }
    [StructLayout(LayoutKind.Sequential)] struct BitmapInfo { public uint Size; public int Width,Height; public ushort Planes,BitCount; public uint Compression,ImageSize; public int XPels,YPels; public uint Used,Important; }
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd,IntPtr dc,uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd,IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc,ref BitmapInfo info,uint usage,out IntPtr bits,IntPtr section,uint offset);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
}
