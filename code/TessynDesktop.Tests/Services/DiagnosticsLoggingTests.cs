using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>D1.1 global exception logging and D1.2 thread-id/UI-marker enrichment through the real output template.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DiagnosticsLoggingTests
{
	private static string RenderLine(UiThreadTracker tracker, Action log)
	{
		var sink = new CapturingSink();
		var logger = new LoggerConfiguration()
			.Enrich.With(new UiThreadLogEventEnricher(tracker))
			.WriteTo.Sink(sink)
			.CreateLogger();
		var formatter = new MessageTemplateTextFormatter(Constants.Diagnostics.LogOutputTemplate);

		logger.ForContext("SourceContext", "Test.Source").Information("hello");
		log();

		var writer = new StringWriter();
		formatter.Format(sink.Events[0], writer);
		return writer.ToString().TrimEnd();
	}

	[Fact]
	public void Template_OnUiThread_ShowsThreadIdAndUiMarker()
	{
		var tracker = new UiThreadTracker();
		tracker.CaptureCurrentThread();

		var line = RenderLine(tracker, () => { });

		Assert.Contains($"[INF] [{Environment.CurrentManagedThreadId}|UI] Test.Source: hello", line);
	}

	[Fact]
	public void Template_OnOtherThread_ShowsThreadIdWithoutMarker()
	{
		var tracker = new UiThreadTracker();
		tracker.CaptureCurrentThread();
		string? line = null;
		int otherId = 0;

		var thread = new Thread(() =>
		{
			otherId = Environment.CurrentManagedThreadId;
			line = RenderLine(tracker, () => { });
		});
		thread.Start();
		thread.Join();

		Assert.Contains($"[INF] [{otherId}] Test.Source: hello", line);
		Assert.DoesNotContain("|UI", line);
	}

	[Fact]
	public void Template_UiThreadNotCaptured_NeverShowsMarker()
	{
		var line = RenderLine(new UiThreadTracker(), () => { });

		Assert.DoesNotContain("|UI", line);
	}

	[Fact]
	public void UnobservedTaskException_IsLoggedAsErrorAndMarkedObserved()
	{
		var sink = new CapturingSink();
		var handler = new GlobalExceptionLogger(sink.CreateLogger());
		var args = new UnobservedTaskExceptionEventArgs(new AggregateException(new InvalidOperationException("boom")));

		handler.OnUnobservedTaskException(this, args);

		Assert.True(args.Observed);
		var evt = Assert.Single(sink.Events);
		Assert.Equal(LogEventLevel.Error, evt.Level);
		Assert.NotNull(evt.Exception);
	}

	[Fact]
	public void UiThreadException_IsLoggedAsError()
	{
		var sink = new CapturingSink();
		var handler = new GlobalExceptionLogger(sink.CreateLogger());

		handler.OnUiThreadException(new InvalidOperationException("ui boom"));

		var evt = Assert.Single(sink.Events);
		Assert.Equal(LogEventLevel.Error, evt.Level);
		Assert.Equal("ui boom", evt.Exception!.Message);
	}

	[Fact]
	public void AppDomainUnhandled_IsLoggedAsFatal()
	{
		var sink = new CapturingSink();
		var handler = new GlobalExceptionLogger(sink.CreateLogger());

		handler.OnAppDomainUnhandled(this, new UnhandledExceptionEventArgs(new InvalidOperationException("fatal boom"), true));

		var evt = Assert.Single(sink.Events);
		Assert.Equal(LogEventLevel.Fatal, evt.Level);
		Assert.Equal("fatal boom", evt.Exception!.Message);
	}
}
