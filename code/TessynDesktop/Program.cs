using System;
using System.Runtime.InteropServices;
using Avalonia;
using TessynDesktop.Services;
using Avalonia.ReactiveUI;

namespace TessynDesktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        var options = BackgroundHostOptions.Parse(args);

        // One daemon host per user session (A23 Q6): a WPF-started --background host, a visible Avalonia window and a rollback start of
        // [Publish]\TessynDesktop.exe all share this guard, so two of them never run side by side and fight over the port and daemon.json.
        using var guard = new DaemonHostGuard();
        if (!guard.TryAcquire())
        {
            if (!options.IsBackground)
                ShowAlreadyRunning();
            return 0;
        }

        App.IsBackgroundHost = options.IsBackground;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void ShowAlreadyRunning()
    {
        const string text = "Tessyn is already running (the daemon host is started). Open the Tessyn WPF client, or exit the running host from its tray icon before starting this window.";
        if (OperatingSystem.IsWindows())
            MessageBoxW(IntPtr.Zero, text, "Tessyn", 0x40);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();

        // Force OpenGL on macOS to avoid Metal memory leak (Avalonia issue #17025).
        // Metal leaks ~10MB/s during resize/drag, ballooning to 9+ GB before crash.
        // OpenGL is deprecated by Apple but functional on all current macOS versions.
        // This option is ignored on Windows/Linux.
        if (OperatingSystem.IsMacOS())
        {
            builder = builder.With(new AvaloniaNativePlatformOptions
            {
                RenderingMode = new[]
                {
                    AvaloniaNativeRenderingMode.OpenGl,
                    AvaloniaNativeRenderingMode.Software,
                }
            });
        }

        return builder;
    }
}
