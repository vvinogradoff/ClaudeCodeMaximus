using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// The programmatic docking test hook (FR.6.22, FR.29.26): a named pipe <c>tessyn-wpf-test-&lt;pid&gt;</c>, open to the current user only, that E2E tests drive with one JSON request per
/// line and one JSON reply per line. Each request runs on the UI thread through <see cref="DockingTestRequestHandler"/>. Only with <c>--test-window</c>: <see cref="Start"/> does nothing
/// otherwise, so the user's app has no pipe. Errors are logged and returned, never thrown into the app.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockingTestHook : IDisposable
{
	private readonly StartupOptions _options;
	private readonly DockingTestRequestHandler _handler;
	private readonly Func<Func<string>, Task<string>> _runOnUi;
	private readonly ILogger _logger;
	private CancellationTokenSource? _stop;
	private Task? _loop;

	/// <param name="runOnUi">Runs the work on the UI thread and returns its result (the main window's dispatcher).</param>
	public DockingTestHook(StartupOptions options, DockingTestRequestHandler handler, Func<Func<string>, Task<string>> runOnUi, ILogger logger)
	{
		_options = options;
		_handler = handler;
		_runOnUi = runOnUi;
		_logger = logger;
	}

	/// <summary>The name of this process's pipe.</summary>
	public string PipeName => Constants.DockingTestHook.PipeNamePrefix + Environment.ProcessId;

	/// <summary>True while the pipe is being served.</summary>
	public bool IsRunning => _loop is { IsCompleted: false };

	/// <summary>Starts serving the pipe; false (and no pipe) without <c>--test-window</c> or when it already runs.</summary>
	public bool Start()
	{
		if (!_options.TestWindow)
		{
			_logger.LogDebug(Constants.DockingTestHook.NotStartedLog, "not a test window");
			return false;
		}

		if (_stop != null)
			return false;

		_stop = new CancellationTokenSource();
		var token = _stop.Token;
		_loop = Task.Run(() => ServeAsync(token), token);
		_logger.LogInformation(Constants.DockingTestHook.StartedLog, PipeName);
		return true;
	}

	/// <summary>Stops serving; the pipe is gone afterwards.</summary>
	public void Stop()
	{
		if (_stop == null)
			return;

		_stop.Cancel();
		try
		{
			_loop?.Wait(TimeSpan.FromSeconds(2));
		}
		catch (Exception)
		{
			// the loop ended by its cancellation
		}

		_stop.Dispose();
		_stop = null;
		_loop = null;
		_logger.LogInformation(Constants.DockingTestHook.StoppedLog);
	}

	public void Dispose() => Stop();

	private async Task ServeAsync(CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			try
			{
				await using var server = CreateServer();
				await server.WaitForConnectionAsync(token);
				await ServeConnectionAsync(server, token);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, Constants.DockingTestHook.ConnectionFailedLog);
				await Task.Delay(TimeSpan.FromMilliseconds(200), CancellationToken.None);
			}
		}
	}

	private async Task ServeConnectionAsync(NamedPipeServerStream server, CancellationToken token)
	{
		using var reader = new StreamReader(server, new UTF8Encoding(false), false, 4096, leaveOpen: true);
		await using var writer = new StreamWriter(server, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
		while (!token.IsCancellationRequested && server.IsConnected)
		{
			var line = await reader.ReadLineAsync(token);
			if (line == null)
				return;

			if (line.Length == 0)
				continue;

			string reply;
			try
			{
				reply = await _runOnUi(() => _handler.Handle(line));
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, Constants.DockingTestHook.RequestFailedLog);
				reply = "{\"ok\":false,\"error\":\"" + ex.Message.Replace("\\", "\\\\").Replace("\"", "\\\"").ReplaceLineEndings(" ") + "\",\"panels\":[]}";
			}

			await writer.WriteLineAsync(reply.AsMemory(), token);
		}
	}

	/// <summary>A one-instance pipe the current user (and nobody else) may open.</summary>
	private NamedPipeServerStream CreateServer()
	{
		var security = new PipeSecurity();
		var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("The current user has no SID.");
		security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
		return NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
	}
}
