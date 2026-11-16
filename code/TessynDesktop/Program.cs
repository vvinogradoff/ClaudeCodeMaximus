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
            // A background host never takes over from anything. A visible window takes over from a BACKGROUND host only (side-by-side, WPF started first):
            // it asks that host to exit through the named exit event, waits for the guard and starts normally; the WPF client reconnects by itself.
            // A holder that does not listen on the event (another visible window, an older host) is left alone.
            if (options.IsBackground)
                return 0;

            var outcome = BackgroundHostTakeover.TryTakeOver(
                guard, TessynDesktop.Models.Remote.RemoteProtocol.Discovery.HostExitEventName,
                TakeoverSignalWindow, TakeoverExitTimeout, TimeSpan.FromMilliseconds(200));
            if (outcome != TakeoverOutcome.Acquired)
            {
                ShowAlreadyRunning(outcome);
                return 0;
            }
        }

        App.IsBackgroundHost = options.IsBackground;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static readonly TimeSpan TakeoverSignalWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TakeoverExitTimeout = TimeSpan.FromSeconds(40);

    private static void ShowAlreadyRunning(TakeoverOutcome outcome)
    {
        var text = outcome == TakeoverOutcome.DidNotExit
            ? "Tessyn's background daemon was asked to exit but is still running. Exit it from its tray icon, then start this window again."
            : "Tessyn is already running in another window (or in an older background host). Use that one, or exit it first, then start this window.";
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
