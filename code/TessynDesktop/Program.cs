using System;
using Avalonia;
using Avalonia.ReactiveUI;

namespace TessynDesktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

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
