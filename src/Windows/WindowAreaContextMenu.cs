using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace Pulsive;

// Transparent layered pixels never receive normal window mouse messages. Handle
// right-clicks inside this window's rectangle and left drags on toolbar gaps only.
internal sealed class WindowAreaContextMenu : IDisposable
{
    readonly Window owner;
    readonly IntPtr hwnd;
    readonly Func<bool> popupOpen;
    readonly Action<Point> open;
    readonly Func<Point,bool> canDrag;
    readonly Action<Point,int> drag;
    readonly HookProc callback;
    IntPtr hook;
    bool pressed;
    DragGesture? gesture;
    internal string DragStatus { get; private set; }="No toolbar press";
    sealed class DragGesture { public Point Latest; public bool UpdatePending; }

    internal WindowAreaContextMenu(Window owner,IntPtr hwnd,Func<bool> popupOpen,Action<Point> open,Func<Point,bool> canDrag,Action<Point,int> drag)
    {
        this.owner=owner; this.hwnd=hwnd; this.popupOpen=popupOpen; this.open=open;
        this.canDrag=canDrag; this.drag=drag;
        callback=OnMouse;
        hook=SetWindowsHookEx(14,callback,GetModuleHandle(null),0);
        if(hook==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not enable right-clicks over the transparent window area.");
    }
    IntPtr OnMouse(int code,IntPtr message,IntPtr data)
    {
        int kind=unchecked((int)message.ToInt64());
        if(code>=0&&(kind==0x0201||kind==0x0204||kind==0x0205||((kind==0x0200||kind==0x0202)&&gesture!=null)))
        {
            var point=Marshal.PtrToStructure<MouseData>(data).Position;
            var position=new Point(point.X,point.Y);
            bool leftTarget=kind==0x0201&&CanHandle(point);
            bool toolbarTarget=leftTarget&&canDrag(position);
            if(kind==0x0201) DragStatus=$"In front: {leftTarget}, toolbar: {toolbarTarget}";
            if(toolbarTarget)
            {
                gesture=new DragGesture { Latest=position };
                owner.Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(()=>drag(position,0)));
                return new IntPtr(1);
            }
            if(kind==0x0200&&gesture is { } moving)
            {
                DragStatus="Dragging";
                moving.Latest=position;
                if(!moving.UpdatePending)
                {
                    moving.UpdatePending=true;
                    owner.Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(()=> { moving.UpdatePending=false; drag(moving.Latest,1); }));
                }
                // Let Windows move the cursor. Suppressing movement would leave
                // the release coordinates at the original position and snap back.
                return CallNextHookEx(hook,code,message,data);
            }
            if(kind==0x0202&&gesture!=null)
            {
                DragStatus="Drag released";
                gesture.Latest=position; gesture=null;
                owner.Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(()=>drag(position,2)));
                return new IntPtr(1);
            }
            if(kind==0x0204&&CanHandle(point)) { pressed=true; return new IntPtr(1); }
            if(kind==0x0205&&pressed)
            {
                pressed=false;
                if(CanHandle(point)) owner.Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(()=>open(new Point(point.X,point.Y))));
                return new IntPtr(1);
            }
        }
        return CallNextHookEx(hook,code,message,data);
    }
    bool CanHandle(NativePoint point)
    {
        if(popupOpen()||!IsWindowVisible(hwnd)||IsIconic(hwnd)||!GetWindowRect(hwnd,out var bounds)) return false;
        if(point.X<bounds.Left||point.X>=bounds.Right||point.Y<bounds.Top||point.Y>=bounds.Bottom) return false;
        var target=GetAncestor(WindowFromPoint(point),2); // GA_ROOT
        if(target==hwnd||target==GetDesktopWindow()) return true;
        // Respect windows in front of Pulsive. A transparent pixel can hit a window
        // below Pulsive, but must not steal right-clicks from a window above it.
        int count=0;
        for(var below=GetWindow(hwnd,2);below!=IntPtr.Zero&&count++<1024;below=GetWindow(below,2))
            if(below==target) return true;
        return false;
    }
    public void Dispose()
    {
        if(hook!=IntPtr.Zero) { UnhookWindowsHookEx(hook); hook=IntPtr.Zero; }
        GC.KeepAlive(callback);
    }
    delegate IntPtr HookProc(int code,IntPtr message,IntPtr data);
    [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MouseData { public NativePoint Position; public uint Data,Flags,Time; public UIntPtr ExtraInfo; }
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int kind,HookProc callback,IntPtr module,uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd,uint command);
    [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
}
