using Microsoft.UI.Xaml;

namespace PrismPlay;
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
        // --demo has its own prefs and WebView folders, so it may run beside the real overlay.
        var demo = Environment.GetCommandLineArgs().Contains("--demo");
        singleton = new Mutex(true, demo ? "Local\\PrismPlay.Overlay.Demo" : "Local\\PrismPlay.Overlay", out var first);
        if (!first) { Exit(); return; }
        DiagnosticLog.Write("Launching");
        window = new OverlayWindow(demo);
        DiagnosticLog.Write("Window constructed");
        window.Activate();
        DiagnosticLog.Write("Window activated");
        window.KeepOnTop();
    }
}
