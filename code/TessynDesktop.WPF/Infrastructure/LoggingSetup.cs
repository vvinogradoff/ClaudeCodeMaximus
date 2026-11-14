using Serilog;
using Serilog.Events;

namespace TessynDesktop.WPF.Infrastructure;

/// <summary>Builds the Serilog logger: daily file under <c>%AppData%\TessynDesktop\logs\wpf-.txt</c>, Information, size-capped.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class LoggingSetup
{
	public Serilog.ILogger CreateLogger(AppPaths paths)
	{
		Directory.CreateDirectory(paths.LogsDirectory);
		return new LoggerConfiguration()
			.MinimumLevel.Information()
			.MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
			.Enrich.FromLogContext()
			.WriteTo.File(
				paths.LogFilePath,
				rollingInterval: RollingInterval.Day,
				fileSizeLimitBytes: Constants.Limits.LogFileSizeBytes,
				rollOnFileSizeLimit: true,
				retainedFileCountLimit: Constants.Limits.LogRetainedFiles,
				shared: true,
				outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
			.CreateLogger();
	}
}
