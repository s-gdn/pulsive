using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Threading;

namespace Pulsive;

internal sealed class BackdropService : IDisposable
{
    readonly Window owner;
    readonly HashSet<BackdropSurface> surfaces=[];
    readonly DispatcherTimer timer=new(DispatcherPriority.Background) { Interval=TimeSpan.FromMilliseconds(100) };
    readonly Stopwatch clock=Stopwatch.StartNew();
    double lastCaptureStart=-1000;
    int unchangedFrames;
    readonly Dictionary<int,BitmapSource> images=[];
    readonly NativeBackdropManager native=new();
    bool busy,disposed,geometryDirty=true;
    TimeSpan lastRender;
    internal long ProjectionCount { get; private set; }
    internal long CaptureCount { get; private set; }
    internal long CachedMotionFrames { get; private set; }
    internal bool NativeAvailable => native.Supported;
    internal long NativeSyncCount => native.SyncCount;
    internal DesktopPixels? LastFrame { get; private set; }
    internal string Status { get; private set; }="Waiting for desktop";
    internal Func<BackdropSurface,Color?>? ContrastBackground { get; set; }
    internal void RefreshContrast()
    {
        var visible=surfaces.Where(s=>s.IsLoaded&&s.ActualWidth>0&&s.ActualHeight>0&&PresentationSource.FromVisual(s)!=null).ToArray();
        if(LastFrame!=null) ApplyCached(visible);
        foreach(var surface in visible) surface.SetContrastBackground(ContrastBackground?.Invoke(surface));
    }
    internal BackdropService(Window window)
    {
        owner=window; BackdropSurface.Service=this;
        timer.Tick+=async (_,_)=>await Update(); timer.Start();
        owner.LocationChanged+=Moved; owner.SizeChanged+=Resized;
        owner.IsVisibleChanged+=VisibilityChanged; owner.StateChanged+=StateChanged;
        CompositionTarget.Rendering+=Render;
    }
    void Moved(object? sender,EventArgs e) { geometryDirty=true; }
    void Resized(object sender,SizeChangedEventArgs e) { geometryDirty=true; RefreshNative(); }
    void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs e) { if(!owner.IsVisible) native.Hide(); else RefreshNative(); }
    void StateChanged(object? sender,EventArgs e) { if(owner.WindowState==WindowState.Minimized) native.Hide(); else RefreshNative(); }
    internal void RefreshNative() { if(!disposed&&owner.IsLoaded&&owner.IsVisible&&owner.WindowState!=WindowState.Minimized) native.Sync(VisibleSurfaces()); }
    internal void RefreshEffectsPolicy() { native.RefreshPolicy(); RefreshNative(); }
    void Render(object? sender,EventArgs e)
    {
        if(e is RenderingEventArgs frame) { if(frame.RenderingTime==lastRender) return; lastRender=frame.RenderingTime; }
        if(disposed||!owner.IsVisible||owner.WindowState==WindowState.Minimized) { native.Hide(); return; }
        if(native.Sync(VisibleSurfaces()))
        {
            geometryDirty=false;
            if(!busy&&clock.Elapsed.TotalMilliseconds-lastCaptureStart>=200) _=Update();
            return;
        }
        if(geometryDirty&&LastFrame!=null) { ApplyCached(VisibleSurfaces()); CachedMotionFrames++; }
        if(!busy&&clock.Elapsed.TotalMilliseconds-lastCaptureStart>=(unchangedFrames>=4?100:33)) _=Update();
    }
    BackdropSurface[] VisibleSurfaces() => surfaces.Where(s=>s.IsVisible&&(!s.RequireFrame||s.BlurRadius>0)&&s.ActualWidth>1&&s.ActualHeight>1&&s.Opacity>0&&PresentationSource.FromVisual(s)!=null&&s.AncestorsVisible()).ToArray();
    void ApplyCached(BackdropSurface[] visible)
    {
        if(LastFrame is null) return;
        foreach(var surface in visible)
            if(surface.IsLoaded&&PresentationSource.FromVisual(surface)!=null&&images.TryGetValue(BlurKey(surface),out var image)) surface.SetFrame(LastFrame,image);
        ProjectionCount++; geometryDirty=false;
    }
    internal void Register(BackdropSurface surface) { surfaces.Add(surface); geometryDirty=true; surface.SetContrastBackground(ContrastBackground?.Invoke(surface)); }
    internal void Unregister(BackdropSurface surface) => surfaces.Remove(surface);
    static int BlurKey(BackdropSurface surface) => surface.RequireFrame?(int)Math.Round(BlurStrength.Radius(surface.BlurRadius)):16;
    internal async Task Update()
    {
        if(disposed||busy||!owner.IsLoaded||!owner.IsVisible||owner.WindowState==WindowState.Minimized) return;
        var visible=VisibleSurfaces();
        if(visible.Length==0) return;
        bool live=native.Sync(visible);
        if(live)
        {
            Status="Live Windows compositor blur";
            // Native blur never waits for these small, low-frequency colour samples.
            if(clock.Elapsed.TotalMilliseconds-lastCaptureStart<200) return;
            visible=visible.Where(s=>s.Adaptive).ToArray();
            if(visible.Length==0) return;
        }
        Rect bounds=Rect.Empty;
        foreach(var surface in visible) bounds.Union(surface.ScreenBounds());
        // Overscan the containing displays so a drag can reproject the cached
        // blurred atlas at render cadence instead of waiting for another capture.
        if(!live) bounds=DesktopBackground.DisplayBounds(bounds);
        else bounds.Inflate(8,8);
        var radii=visible.Select(BlurKey).Distinct().ToArray();
        var previous=LastFrame;
        var previousImages=new Dictionary<int,BitmapSource>(images);
        var hwnd=new WindowInteropHelper(owner).Handle;
        busy=true;
        lastCaptureStart=clock.Elapsed.TotalMilliseconds;
        try
        {
            var result=await Task.Run(()=>
            {
                var raw=DesktopBackground.Capture(hwnd,bounds,live?256:960);
                bool unchanged=previous!=null&&previous.Bounds==raw.Bounds&&previous.Width==raw.Width&&previous.Height==raw.Height&&raw.Pixels.AsSpan().SequenceEqual(previous.Pixels);
                var images=radii.ToDictionary(r=>r,r=>unchanged&&previousImages.TryGetValue(r,out var cached)?cached:(live?raw:BackgroundBlur.Apply(raw,r)).Bitmap());
                return (raw,images,unchanged);
            });
            if(disposed) return;
            LastFrame=result.raw; CaptureCount++; Status=live?"Live Windows compositor blur":"Live desktop background";
            unchangedFrames=result.unchanged?unchangedFrames+1:0;
            images.Clear(); foreach(var pair in result.images) images.Add(pair.Key,pair.Value);
            ApplyCached(VisibleSurfaces());
        }
        catch(Exception e) { Status="Desktop sampling unavailable"; Debug.WriteLine(e); }
        finally { busy=false; }
    }
    public void Dispose() { disposed=true; timer.Stop(); owner.LocationChanged-=Moved; owner.SizeChanged-=Resized; owner.IsVisibleChanged-=VisibilityChanged; owner.StateChanged-=StateChanged; CompositionTarget.Rendering-=Render; native.Dispose(); surfaces.Clear(); images.Clear(); LastFrame=null; if(BackdropSurface.Service==this) BackdropSurface.Service=null; }
}

internal static class BackdropVisibility
{
    internal static bool AncestorsVisible(this FrameworkElement element)
    {
        for(DependencyObject? p=element;p!=null;p=System.Windows.Media.VisualTreeHelper.GetParent(p))
        {
            if(p is System.Windows.Controls.ContextMenu menu&&!menu.IsOpen) return false;
            if(p is UIElement ui&&(!ui.IsVisible||ui.Opacity<.01)) return false;
        }
        return true;
    }
}
