using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TessynDesktop.Codex;

/// <summary>A running app-server process, real or in-memory (FR.33.19).</summary>
/// <remarks>Created by Claude</remarks>
public interface ICodexProcess : IDisposable
{
	int Id { get; }

	/// <summary>Tessyn writes requests here (the process's stdin).</summary>
	Stream StandardInput { get; }

	/// <summary>Tessyn reads messages here (the process's stdout).</summary>
	Stream StandardOutput { get; }

	TextReader StandardError { get; }

	/// <summary>The exit code once the process has exited, else null.</summary>
	int? ExitCode { get; }

	Task WaitForExitAsync(CancellationToken cancellationToken);

	/// <summary>Closes stdin; the app-server exits when its stdin closes.</summary>
	void CloseStandardInput();

	void Kill();
}
