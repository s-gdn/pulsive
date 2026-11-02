using System.Windows;
using System.IO;

namespace Pulsive;

public sealed class App : Application
{
    public App() => ShutdownMode=ShutdownMode.OnMainWindowClose;
    internal static bool IsUiTest
    {
        get
        {
#if DEVELOPMENT_TESTS
            return Environment.GetCommandLineArgs().Any(arg=>arg is "--ui-test" or "--readme-demo");
#else
            return false;
#endif
        }
    }
    internal static string SettingsFile
    {
        get
        {
#if DEVELOPMENT_TESTS
            if(IsUiTest) return Path.Combine(TestLauncher.ArtifactsPath,"test-settings.json");
#endif
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"pulsive","settings.json");
        }
    }
    [STAThread]
    public static void Main(string[] args)
    {
        bool? toolbarLock = args.Contains("--lock-toolbar") ? true : args.Contains("--unlock-toolbar") ? false : null;
        if (toolbarLock.HasValue && ToolbarMenus.TrySetExisting(toolbarLock.Value)) return;
        bool? backgroundBlur=args.Contains("--enable-background-blur")?true:args.Contains("--disable-background-blur")?false:null;
        if(backgroundBlur.HasValue&&ToolbarMenus.TrySetExistingBlur(backgroundBlur.Value)) return;
#if DEVELOPMENT_TESTS
        if(TestLauncher.TryRun(args)) return;
#endif
        ToolbarMenus.InitializeAppId();
        var app = new App();
        app.DispatcherUnhandledException += (_, e) => { MessageBox.Show(e.Exception.Message, "pulsive"); e.Handled = true; };
        var window = new MainWindow(args.Contains("--preview"));
        app.MainWindow=window;
        if(toolbarLock.HasValue) window.Loaded += (_,_) => window.SetToolbarLocked(toolbarLock.Value);
        if(backgroundBlur.HasValue) window.Loaded += (_,_) => window.SetBackgroundBlur(backgroundBlur.Value);
        app.Run(window);
    }
}
