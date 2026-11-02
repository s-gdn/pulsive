using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Shell;

namespace Pulsive;

internal static class ToolbarMenus
{
    internal const string WindowTitle = "pulsive · Audio visualiser";
    internal const int SystemCommand = 0x1F10;
    internal const int BlurCommand = 0x1F20;
    internal static readonly uint SetLockMessage = RegisterWindowMessage("PulsiveOverlay.SetToolbarHiddenLock.v1");
    internal static readonly uint SetBlurMessage = RegisterWindowMessage("PulsiveOverlay.SetBackgroundBlur.v1");
    internal static string JumpListStatus { get; private set; } = "Not applied";
    internal static string Label(bool locked) => locked ? "Unlock toolbar" : "Lock toolbar hidden";

    internal static void InitializeAppId(string appId="Pulsive.AudioVisualiser") => SetCurrentProcessExplicitAppUserModelID(appId);

    internal static bool TrySetExisting(bool locked) => TrySendExisting(SetLockMessage,locked);
    internal static bool TrySetExistingBlur(bool enabled) => TrySendExisting(SetBlurMessage,enabled);
    static bool TrySendExisting(uint message,bool enabled)
    {
        IntPtr hwnd=IntPtr.Zero;
        while((hwnd=FindWindowEx(IntPtr.Zero,hwnd,null,WindowTitle))!=IntPtr.Zero)
        {
            GetWindowThreadProcessId(hwnd, out uint processId);
            try
            {
                using var process = Process.GetProcessById((int)processId);
                if (!string.Equals(process.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) continue;
                return PostMessage(hwnd, message, new IntPtr(enabled ? 1 : 0), IntPtr.Zero);
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException) { }
        }
        return false;
    }

    internal static void UpdateSystemMenu(IntPtr hwnd, bool locked,bool blur=false)
    {
        var menu = GetSystemMenu(hwnd, false);
        if (menu == IntPtr.Zero) return;
        if (GetMenuState(menu, SystemCommand, 0) == uint.MaxValue)
        {
            InsertMenu(menu, 0, 0x400, new UIntPtr(SystemCommand), Label(locked)); // MF_BYPOSITION
            InsertMenu(menu, 1, 0x400 | 0x800, UIntPtr.Zero, null); // separator
        }
        else ModifyMenu(menu, SystemCommand, 0, new UIntPtr(SystemCommand), Label(locked));
        string blurLabel=blur?"Disable visualiser background blur":"Enable visualiser background blur";
        if(GetMenuState(menu,BlurCommand,0)==uint.MaxValue) InsertMenu(menu,1,0x400,new UIntPtr(BlurCommand),blurLabel);
        else ModifyMenu(menu,BlurCommand,0,new UIntPtr(BlurCommand),blurLabel);
    }

    internal static JumpTask BuildTask(bool locked)
        => BuildAction(Label(locked),locked ? "--unlock-toolbar" : "--lock-toolbar",locked ? "Allow the toolbar to appear on hover again." : "Keep the toolbar hidden, even when hovering.");
    internal static JumpTask BuildBlurTask(bool enabled)
        => BuildAction(enabled?"Disable visualiser background blur":"Enable visualiser background blur",enabled?"--disable-background-blur":"--enable-background-blur","Toggle the blurred backdrop behind the visualiser.");
    static JumpTask BuildAction(string title,string argument,string description)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate Pulsive.");
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            argument = $"\"{Path.Combine(AppContext.BaseDirectory, typeof(App).Assembly.GetName().Name + ".dll")}\" {argument}";
        return new JumpTask
        {
            Title = title, Description = description,
            ApplicationPath = executable, Arguments = argument, WorkingDirectory = AppContext.BaseDirectory
        };
    }

    internal static void UpdateJumpList(bool locked,bool blur=false)
    {
        var list = new JumpList { ShowFrequentCategory = false, ShowRecentCategory = false };
        list.JumpItems.Add(BuildTask(locked));
        list.JumpItems.Add(BuildBlurTask(blur));
        JumpListStatus="Applied";
        list.JumpItemsRejected+=(_,e)=>JumpListStatus=string.Join(", ",e.RejectionReasons);
        try { JumpList.SetJumpList(Application.Current, list); }
        catch (Exception e) when (e is COMException or InvalidOperationException) { JumpListStatus=e.Message; Debug.WriteLine("Taskbar menu unavailable: " + e.Message); }
    }

    [DllImport("shell32.dll", CharSet=CharSet.Unicode)] static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string title);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] static extern IntPtr GetSystemMenu(IntPtr hwnd, bool revert);
    [DllImport("user32.dll")] static extern uint GetMenuState(IntPtr menu, uint id, uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern bool InsertMenu(IntPtr menu, uint position, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern bool ModifyMenu(IntPtr menu, uint position, uint flags, UIntPtr id, string text);
}
