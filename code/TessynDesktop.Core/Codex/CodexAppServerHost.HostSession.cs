using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TessynDesktop.Codex;

public sealed partial class CodexAppServerHost
{
	/// <summary>One launched app-server process and its connection. A restart makes a new session (and a new <see cref="Generation"/>).</summary>
	private sealed class HostSession
	{
		private readonly Queue<string> _stderrTail = new();
		private readonly TaskCompletionSource<CodexHostCrashInfo?> _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public HostSession(ICodexProcess process, CodexJsonRpcConnection connection, int generation)
		{
			Process    = process;
			Connection = connection;
			Generation = generation;
		}

		public ICodexProcess          Process    { get; }
		public CodexJsonRpcConnection Connection { get; }
		public int                    Generation { get; }

		/// <summary>Set before Tessyn closes the process on purpose, so its end is not reported as a crash.</summary>
		public bool StopRequested { get; set; }

		public Task StderrPump { get; set; } = Task.CompletedTask;

		/// <summary>Completes when the session has ended: with the crash report, or null for a stop Tessyn asked for.</summary>
		public Task<CodexHostCrashInfo?> Ended => _ended.Task;

		public void AddStderrLine(string line)
		{
			lock (_stderrTail)
			{
				_stderrTail.Enqueue(line);
				while (_stderrTail.Count > Constants.Codex.StderrTailLines)
					_stderrTail.Dequeue();
			}
		}

		public IReadOnlyList<string> StderrTail()
		{
			lock (_stderrTail)
				return _stderrTail.ToArray();
		}

		public void End(CodexHostCrashInfo? crash) => _ended.TrySetResult(crash);
	}
}
