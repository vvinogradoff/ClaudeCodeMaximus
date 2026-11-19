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

		_stop.Cancel();   // the loop never waits for the UI thread past this (WaitAsync(token) in ServeConnectionAsync), so this may run on it
		try
		{
			_loop?.Wait(TimeSpan.FromMilliseconds(Constants.DockingTestHook.StopWaitMilliseconds));
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
			var (line, tooLong) = await ReadRequestLineAsync(reader, token);
			if (tooLong)
			{
				_logger.LogWarning(Constants.DockingTestHook.RequestTooLongLog, Constants.DockingTestHook.MaxRequestLineChars);
				await writer.WriteLineAsync(_handler.Failure(string.Format(Constants.DockingTestHook.RequestTooLongError, Constants.DockingTestHook.MaxRequestLineChars)).AsMemory(), token);
				return;   // the rest of the line cannot be told from a new request: the connection ends
			}

			if (line == null)
				return;

			if (line.Length == 0)
				continue;

			string reply;
			try
			{
				reply = await _runOnUi(() => _handler.Handle(line)).WaitAsync(token);
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, Constants.DockingTestHook.RequestFailedLog);
				reply = _handler.Failure(ex.Message);
			}

			await writer.WriteLineAsync(reply.AsMemory(), token);
		}
	}

	/// <summary>One request line, read one character at a time up to <see cref="Constants.DockingTestHook.MaxRequestLineChars"/>: null at the end of the stream, or <c>TooLong</c> when the limit is passed.</summary>
	private async Task<(string? Line, bool TooLong)> ReadRequestLineAsync(StreamReader reader, CancellationToken token)
	{
		var line = new StringBuilder();
		var one = new char[1];
		while (true)
		{
			if (await reader.ReadAsync(one.AsMemory(), token) == 0)
				return (line.Length == 0 ? null : line.ToString(), false);

			if (one[0] == '\n')
				return (line.ToString().TrimEnd('\r'), false);

			if (line.Length >= Constants.DockingTestHook.MaxRequestLineChars)
				return (null, true);

			line.Append(one[0]);
		}
	}

	/// <summary>
	/// A one-instance pipe the current user (and nobody else) may open: <see cref="PipeOptions.CurrentUserOnly"/> gives it the current user's access (the library refuses an explicit ACL with that
	/// option, so the ACL is completed after the pipe exists) and every network logon is denied on top of it.
	/// </summary>
	private NamedPipeServerStream CreateServer()
	{
		var server = NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096, null, HandleInheritability.None, PipeAccessRights.ChangePermissions);
		try
		{
			var security = server.GetAccessControl();
			security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
			server.SetAccessControl(security);
			return server;
		}
		catch (Exception)
		{
			server.Dispose();
			throw;
		}
	}
}
