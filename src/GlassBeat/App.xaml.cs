using Microsoft.UI.Xaml;

namespace GlassBeat;
public partial class App : Application
{
    private OverlayWindow? window;
    private Mutex? singleton;
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => DiagnosticLog.Write("Unhandled: " + e.Exception);
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        DiagnosticLog.Write("Launching");
        // --demo has its own prefs and WebView folders, so it may run beside the real overlay.
        var demo = Environment.GetCommandLineArgs().Contains("--demo");
        singleton = new Mutex(true, demo ? "Local\\GlassBeat.Overlay.Demo" : "Local\\GlassBeat.Overlay", out var first);
        if (!first) { Exit(); return; }
        window = new OverlayWindow(demo);
        DiagnosticLog.Write("Window constructed");
        window.Activate();
        DiagnosticLog.Write("Window activated");
        window.KeepOnTop();
    }
}
