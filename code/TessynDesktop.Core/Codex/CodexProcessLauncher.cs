using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Serilog;

namespace TessynDesktop.Codex;

/// <summary>
/// Starts the real <c>codex app-server</c> (FR.33.6): the native exe, hidden, with stdin, stdout and stderr redirected as UTF-8 without a
/// BOM, <c>CODEX_HOME</c> set for a stored profile (whose folder is created first, FR.33.5) and removed for Codex Default.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexProcessLauncher : ICodexProcessLauncher
{
	private static readonly ILogger _log = Log.ForContext<CodexProcessLauncher>();

	// A BOM on stdin would be read as part of the first JSON line.
	private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

	public ICodexProcess Launch(CodexLaunchSpec spec)
	{
		var extension = Path.GetExtension(spec.ExecutablePath);
		if (!string.Equals(extension, Constants.Codex.Launch.ExecutableExtension, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException($"Codex must be launched from its native exe, not '{spec.ExecutablePath}' (FR.33.17).");

		if (!string.IsNullOrEmpty(spec.CodexHome))
			Directory.CreateDirectory(spec.CodexHome);

		var psi = new ProcessStartInfo(spec.ExecutablePath)
		{
			WorkingDirectory       = spec.WorkingDirectory,
			RedirectStandardInput  = true,
			RedirectStandardOutput = true,
			RedirectStandardError  = true,
			UseShellExecute        = false,
			CreateNoWindow         = true,
			StandardInputEncoding  = Utf8NoBom,
			StandardOutputEncoding = Utf8NoBom,
			StandardErrorEncoding  = Utf8NoBom,
		};
		foreach (var argument in spec.Arguments)
			psi.ArgumentList.Add(argument);

		if (string.IsNullOrEmpty(spec.CodexHome))
			psi.Environment.Remove(Constants.Codex.Launch.CodexHomeVariable);
		else
			psi.Environment[Constants.Codex.Launch.CodexHomeVariable] = spec.CodexHome;

		var process = Process.Start(psi)
			?? throw new InvalidOperationException($"Codex did not start from '{spec.ExecutablePath}'.");

		_log.Information("Started Codex app-server {Pid}: {Exe} {Arguments} (CODEX_HOME {CodexHome})",
			process.Id, spec.ExecutablePath, string.Join(' ', spec.Arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)), spec.CodexHome ?? "(default)");

		return new CodexProcess(process);
	}
}
