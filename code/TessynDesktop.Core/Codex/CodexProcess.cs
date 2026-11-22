using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TessynDesktop.Codex;

/// <summary>A real <c>codex app-server</c> process.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexProcess : ICodexProcess
{
	private readonly Process _process;

	public CodexProcess(Process process)
	{
		_process = process;
	}

	public int Id => _process.Id;

	public Stream StandardInput => _process.StandardInput.BaseStream;

	public Stream StandardOutput => _process.StandardOutput.BaseStream;

	public TextReader StandardError => _process.StandardError;

	public int? ExitCode => _process.HasExited ? _process.ExitCode : null;

	public Task WaitForExitAsync(CancellationToken cancellationToken) => _process.WaitForExitAsync(cancellationToken);

	public void CloseStandardInput() => _process.StandardInput.Close();

	public void Kill()
	{
		try
		{
			if (!_process.HasExited)
				_process.Kill(entireProcessTree: true);
		}
		catch (InvalidOperationException)
		{
			// It exited between the check and the kill.
		}
	}

	public void Dispose() => _process.Dispose();
}
