using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Pulsive;

internal sealed class NativeBackdropManager : IDisposable
{
    readonly Dictionary<IntPtr,NativeCompositionBackdrop> hosts=[];
    bool failed;
    bool effectsAllowed=ReadEffectsPolicy();
    internal bool Supported => OperatingSystem.IsWindowsVersionAtLeast(10,0,22000)&&!failed&&effectsAllowed;
    internal long SyncCount { get; private set; }
    internal string Failure { get; private set; }="";
    internal bool Sync(BackdropSurface[] visible)
    {
        if(!Supported) { Hide(); foreach(var surface in visible) surface.SetNative(false); return false; }
        try
        {
            var active=new HashSet<IntPtr>();
            foreach(var group in visible.GroupBy(s=>((HwndSource)PresentationSource.FromVisual(s)).Handle))
            {
                var hwnd=group.Key;
                if(!IsWindowVisible(hwnd)||!GetWindowRect(hwnd,out var rect)) continue;
                if(!hosts.TryGetValue(hwnd,out var host)) { host=new NativeCompositionBackdrop(); hosts.Add(hwnd,host); }
                var regions=group.Select(surface=>
                {
                    var bounds=surface.ScreenBounds();
                    double opacity=1;
                    for(DependencyObject? p=surface;p!=null;p=VisualTreeHelper.GetParent(p)) if(p is UIElement ui) opacity*=ui.Opacity;
                    double scale=bounds.Width/Math.Max(1,surface.ActualWidth);
                    if(surface.RequireFrame) opacity*=BlurStrength.Amount(surface.BlurRadius);
                    double blur=surface.RequireFrame?BlurStrength.Radius(surface.BlurRadius):surface.BlurRadius;
                    return ((float)(bounds.X-rect.Left),(float)(bounds.Y-rect.Top),(float)bounds.Width,(float)bounds.Height,(float)(surface.CornerRadius.TopLeft*scale),(float)opacity,(float)(blur*scale));
                }).ToArray();
                host.SetRegions(regions);
                host.ShowBehind(hwnd,rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top);
                foreach(var surface in group) surface.SetNative(true);
                active.Add(hwnd);
            }
            foreach(var pair in hosts.ToArray())
            {
                if(!IsWindow(pair.Key)) { pair.Value.Dispose(); hosts.Remove(pair.Key); }
                else if(!active.Contains(pair.Key)) pair.Value.Hide();
            }
            SyncCount++; return true;
        }
        catch(Exception e)
        {
            Failure=e.Message; failed=true; Debug.WriteLine("Native backdrop unavailable: "+e);
            foreach(var surface in visible) surface.SetNative(false);
            DisposeHosts(); return false;
        }
    }
    internal void Hide() { foreach(var host in hosts.Values) host.Hide(); }
    internal void RefreshPolicy() { effectsAllowed=ReadEffectsPolicy(); if(!effectsAllowed) Hide(); }
    static bool ReadEffectsPolicy()
    {
        if(SystemParameters.HighContrast) return false;
        try { return Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","EnableTransparency",1) is not int value||value!=0; }
        catch { return true; }
    }
    void DisposeHosts() { foreach(var host in hosts.Values) host.Dispose(); hosts.Clear(); }
    public void Dispose() => DisposeHosts();
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
}
