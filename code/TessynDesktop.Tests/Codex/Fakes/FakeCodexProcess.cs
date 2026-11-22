using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Codex;

namespace TessynDesktop.Tests.Codex.Fakes;

/// <summary>The in-memory process the fake launcher hands the host (FR.33.19): three pipes and an exit code set by the fake app-server.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FakeCodexProcess : ICodexProcess
{
	private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private int? _exitCode;

	public FakeCodexProcess(int id)
	{
		Id             = id;
		StandardError  = new StreamReader(StderrPipe, new UTF8Encoding(false));
	}

	public int Id { get; }

	/// <summary>Client to server.</summary>
	public FakePipeStream StdinPipe { get; } = new();

	/// <summary>Server to client.</summary>
	public FakePipeStream StdoutPipe { get; } = new();

	public FakePipeStream StderrPipe { get; } = new();

	public Stream StandardInput => StdinPipe;

	public Stream StandardOutput => StdoutPipe;

	public TextReader StandardError { get; }

	public int? ExitCode
	{
		get { lock (_exited) return _exitCode; }
	}

	public bool HasExited => _exited.Task.IsCompleted;

	public bool StdinClosedByHost { get; private set; }

	public bool Killed { get; private set; }

	public bool Disposed { get; private set; }

	public Task Exited => _exited.Task;

	public Task WaitForExitAsync(CancellationToken cancellationToken) => _exited.Task.WaitAsync(cancellationToken);

	public void CloseStandardInput()
	{
		StdinClosedByHost = true;
		StdinPipe.CompleteWriting();
	}

	public void Kill()
	{
		if (HasExited)
			return;
		Killed = true;
		Exit(-1);
	}

	/// <summary>Ends the process: sets the exit code, then closes stderr and stdout (so the host sees the code when the stream ends).</summary>
	public void Exit(int exitCode)
	{
		lock (_exited)
			_exitCode ??= exitCode;
		StderrPipe.CompleteWriting();
		StdoutPipe.CompleteWriting();
		_exited.TrySetResult();
	}

	public void Dispose() => Disposed = true;
}
