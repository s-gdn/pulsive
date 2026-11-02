using System.Runtime.InteropServices;

namespace Pulsive;

internal static class WindowFrame
{
    public static void Suppress(IntPtr hwnd)
    {
        int disabled = 1; // DWMNCRP_DISABLED: no OS frame or frame shadow.
        DwmSetWindowAttribute(hwnd, 2, ref disabled, sizeof(int));
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            int noBorder = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
            DwmSetWindowAttribute(hwnd, 34, ref noBorder, sizeof(int));
        }
    }
    internal static bool HasNativeFrame(IntPtr hwnd) => (GetWindowLong(hwnd, -16) & (0x00C00000 | 0x00040000)) != 0;
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll", EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr hwnd, int index);
}
