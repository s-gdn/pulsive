using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Pulsive;

// A separate click-through surface lets light extend beyond the overlay's HWND,
// without adding invisible padding to its drag, resize, or context-menu bounds.
internal sealed class OuterGlowWindow : Window
{
    readonly Window owner;
    readonly OverlayBorder glow=new() { ExternalGlow=true };
    IntPtr handle;
    (int X,int Y,int Width,int Height) placement;
    internal OuterGlowWindow(Window owner)
    {
        this.owner=owner; Owner=owner;
        WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
        AllowsTransparency=true; Background=Brushes.Transparent;
        ShowInTaskbar=false; ShowActivated=false; Focusable=false; IsHitTestVisible=false;
        Content=glow;
        SourceInitialized+=(_,_)=>
        {
            handle=new WindowInteropHelper(this).Handle;
            SetWindowLong(handle,-20,GetWindowLong(handle,-20)|0x20|0x08000000|0x80);
            ((HwndSource)PresentationSource.FromVisual(this)).AddHook(Hook);
            WindowFrame.Suppress(handle);
        };
        owner.LocationChanged+=Sync; owner.SizeChanged+=Resized; owner.StateChanged+=Sync;
        owner.IsVisibleChanged+=VisibilityChanged;
        Closed+=(_,_)=> { owner.LocationChanged-=Sync; owner.SizeChanged-=Resized; owner.StateChanged-=Sync; owner.IsVisibleChanged-=VisibilityChanged; };
    }
    internal void Update(int type,Color color,int palette,double level,double bass,double dt)
    {
        glow.SetAppearance(type,color,palette); glow.Update(level,bass,dt);
        if(type<3||!owner.IsVisible||owner.WindowState==WindowState.Minimized) { Hide(); return; }
        if(!IsVisible) { Show(); placement=default; }
        Sync(null,EventArgs.Empty);
    }
    void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs e) { if(!owner.IsVisible) Hide(); }
    void Resized(object sender,SizeChangedEventArgs e)=>Sync(sender,e);
    void Sync(object? sender,EventArgs e)
    {
        if(owner.WindowState==WindowState.Minimized||!owner.IsVisible) { Hide(); return; }
        if(!IsVisible||handle==IntPtr.Zero) return;
        var ownerHandle=new WindowInteropHelper(owner).Handle;
        if(!GetWindowRect(ownerHandle,out var bounds)) return;
        double scale=PresentationSource.FromVisual(owner).CompositionTarget.TransformToDevice.M11;
        int padding=(int)Math.Ceiling(OverlayBorder.GlowPadding*scale);
        var next=(bounds.Left-padding,bounds.Top-padding,bounds.Right-bounds.Left+padding*2,bounds.Bottom-bounds.Top+padding*2);
        if(placement==next) return;
        placement=next;
        SetWindowPos(handle,ownerHandle,next.Item1,next.Item2,next.Item3,next.Item4,0x0010);
    }
    static IntPtr Hook(IntPtr hwnd,int message,IntPtr wp,IntPtr lp,ref bool handled)
    {
        if(message==0x0084) { handled=true; return new IntPtr(-1); }
        if(message==0x0021) { handled=true; return new IntPtr(3); }
        return IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)] struct Bounds { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out Bounds bounds);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW")] static extern int SetWindowLong(IntPtr hwnd,int index,int value);
}
