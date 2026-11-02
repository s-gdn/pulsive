using System.Numerics;
using System.Runtime.InteropServices;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;
using Microsoft.Graphics.Canvas.Effects;

namespace Pulsive;

// This HWND has no GDI redirection bitmap. Only its clipped compositor visuals
// draw pixels; the rest remains transparent, rather than an acrylic rectangle.
internal sealed class NativeCompositionBackdrop : IDisposable
{
    const string ClassName="Pulsive.NativeBackdrop.v1";
    static readonly WndProc procedure=WindowProcedure;
    readonly Compositor compositor=null!;
    readonly DesktopWindowTarget target=null!;
    readonly ContainerVisual root=null!;
    readonly CompositionBackdropBrush brush=null!;
    readonly CompositionEffectFactory effectFactory=null!;
    readonly List<(SpriteVisual Sprite,CompositionRoundedRectangleGeometry Shape,CompositionEffectBrush Effect)> shapes=[];
    internal IntPtr Handle { get; }
    [ThreadStatic] static IntPtr sharedQueue;
    [ThreadStatic] static int queueUsers;
    bool queueUser;
    (float,float,float,float,float,float,float)[] previousRegions=[];
    (IntPtr Owner,int X,int Y,int Width,int Height) previousPlacement;
    bool shown;
    bool disposed;
    internal NativeCompositionBackdrop()
    {
        if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,22000)) throw new PlatformNotSupportedException("Live desktop composition requires Windows 11.");
        try
        {
        if(Windows.System.DispatcherQueue.GetForCurrentThread()==null)
        {
            var options=new QueueOptions { Size=Marshal.SizeOf<QueueOptions>(),ThreadType=2,Apartment=2 };
            Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options,out sharedQueue));
        }
        queueUsers++; queueUser=true;
        var module=GetModuleHandle(null);
        var windowClass=new WindowClass { Size=(uint)Marshal.SizeOf<WindowClass>(),Instance=module,Procedure=procedure,Name=ClassName };
        RegisterClassEx(ref windowClass);
        Handle=CreateWindowEx(0x00200000|0x08000000|0x00000080|0x00000020,ClassName,"Pulsive live blur",0x80000000|0x08000000,0,0,1,1,IntPtr.Zero,IntPtr.Zero,module,IntPtr.Zero);
        if(Handle==IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        int enabled=1,noBorder=unchecked((int)0xFFFFFFFE);
        Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(Handle,17,ref enabled,4));
        DwmSetWindowAttribute(Handle,34,ref noBorder,4);
        compositor=new Compositor();
        var interop=compositor.As<ICompositorDesktopInterop>();
        interop.CreateDesktopWindowTarget(Handle,true,out var pointer);
        try { target=MarshalInspectable<DesktopWindowTarget>.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
        root=compositor.CreateContainerVisual(); target.Root=root;
        brush=compositor.CreateHostBackdropBrush();
        using var blur=new GaussianBlurEffect { Name="Blur",BlurAmount=16,BorderMode=EffectBorderMode.Hard,Optimization=EffectOptimization.Speed,Source=new CompositionEffectSourceParameter("Desktop") };
        using var material=new SaturationEffect { Saturation=1.12f,Source=blur };
        effectFactory=compositor.CreateEffectFactory(material,["Blur.BlurAmount"]);
        }
        catch { Dispose(); throw; }
    }
    internal void SetRegions(IEnumerable<(float X,float Y,float Width,float Height,float Radius,float Opacity)> regions)
        => SetRegions(regions.Select(r=>(r.X,r.Y,r.Width,r.Height,r.Radius,r.Opacity,16f)));
    internal void SetRegions(IEnumerable<(float X,float Y,float Width,float Height,float Radius,float Opacity,float Blur)> regions)
    {
        var list=regions.ToArray();
        if(previousRegions.SequenceEqual(list)) return;
        previousRegions=list;
        // The native HWND must not hit-test as an invisible full-window rectangle.
        // Restrict its window region to exactly the visible rounded blur shapes.
        var regionHandle=CreateRectRgn(0,0,0,0);
        foreach(var region in list.Where(r=>r.Opacity>.001f))
        {
            var piece=CreateRoundRectRgn((int)Math.Floor(region.X),(int)Math.Floor(region.Y),(int)Math.Ceiling(region.X+region.Width),(int)Math.Ceiling(region.Y+region.Height),(int)(region.Radius*2),(int)(region.Radius*2));
            CombineRgn(regionHandle,regionHandle,piece,2); DeleteObject(piece);
        }
        if(SetWindowRgn(Handle,regionHandle,false)==0) DeleteObject(regionHandle);
        while(shapes.Count<list.Length)
        {
            var effect=effectFactory.CreateBrush(); effect.SetSourceParameter("Desktop",brush);
            var sprite=compositor.CreateSpriteVisual(); sprite.Brush=effect;
            var geometry=compositor.CreateRoundedRectangleGeometry();
            sprite.Clip=compositor.CreateGeometricClip(geometry); root.Children.InsertAtTop(sprite); shapes.Add((sprite,geometry,effect));
        }
        for(int i=0;i<shapes.Count;i++)
        {
            var (sprite,geometry,effect)=shapes[i];
            if(i>=list.Length) { sprite.IsVisible=false; continue; }
            var region=list[i]; sprite.IsVisible=true;
            sprite.Offset=new Vector3(region.X,region.Y,0); sprite.Size=new Vector2(region.Width,region.Height);
            sprite.Opacity=region.Opacity;
            effect.Properties.InsertScalar("Blur.BlurAmount",Math.Clamp(region.Blur,0,48));
            geometry.Size=sprite.Size; geometry.CornerRadius=new Vector2(region.Radius);
        }
    }
    internal void ShowBehind(IntPtr owner,int x,int y,int width,int height)
    {
        var placement=(owner,x,y,width,height);
        if(shown&&previousPlacement==placement&&GetWindow(Handle,3)==owner) return;
        root.Size=new Vector2(width,height); previousPlacement=placement; shown=true;
        SetWindowPos(Handle,owner,x,y,width,height,0x0010|0x0040);
    }
    internal void Hide() { if(shown) ShowWindow(Handle,0); shown=false; }
    public void Dispose()
    {
        if(disposed) return; disposed=true;
        foreach(var (sprite,geometry,effect) in shapes) { sprite.Dispose(); geometry.Dispose(); effect.Dispose(); }
        effectFactory?.Dispose(); root?.Dispose(); brush?.Dispose(); target?.Dispose(); compositor?.Dispose(); if(Handle!=IntPtr.Zero) DestroyWindow(Handle);
        if(queueUser) { queueUser=false; if(--queueUsers==0&&sharedQueue!=IntPtr.Zero) { Marshal.Release(sharedQueue); sharedQueue=IntPtr.Zero; } }
    }
    static IntPtr WindowProcedure(IntPtr hwnd,uint message,IntPtr wp,IntPtr lp)
    {
        if(message==0x0084) return new IntPtr(-1);
        if(message==0x0014) return new IntPtr(1);
        return DefWindowProc(hwnd,message,wp,lp);
    }
    [ComImport,Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICompositorDesktopInterop { void CreateDesktopWindowTarget(IntPtr hwnd,[MarshalAs(UnmanagedType.Bool)] bool topmost,out IntPtr target); }
    [StructLayout(LayoutKind.Sequential)] struct QueueOptions { public int Size,ThreadType,Apartment; }
    delegate IntPtr WndProc(IntPtr hwnd,uint message,IntPtr wp,IntPtr lp);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct WindowClass
    {
        public uint Size,Style; public WndProc Procedure; public int ClassExtra,WindowExtra; public IntPtr Instance,Icon,Cursor,Background; public string? Menu; public string Name; public IntPtr SmallIcon;
    }
    [DllImport("coremessaging.dll")] static extern int CreateDispatcherQueueController(QueueOptions options,out IntPtr controller);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern ushort RegisterClassEx(ref WindowClass windowClass);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateWindowEx(uint extended,string className,string title,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr hwnd,uint message,IntPtr wp,IntPtr lp);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd,int command);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd,uint command);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int width,int height);
    [DllImport("gdi32.dll")] static extern int CombineRgn(IntPtr destination,IntPtr first,IntPtr second,int mode);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd,IntPtr region,bool redraw);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
}
