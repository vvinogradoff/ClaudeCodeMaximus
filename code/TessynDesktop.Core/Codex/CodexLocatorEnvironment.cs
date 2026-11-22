using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace TessynDesktop.Codex;

/// <summary>The real environment of <see cref="CodexExecutableLocator"/>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexLocatorEnvironment : ICodexLocatorEnvironment
{
	private static readonly ILogger _log = Log.ForContext<CodexLocatorEnvironment>();

	public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

	public bool FileExists(string path) => File.Exists(path);

	public bool DirectoryExists(string path) => Directory.Exists(path);

	public string? ResolveLinkTarget(string path)
	{
		try
		{
			return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName;
		}
		catch (IOException ex)
		{
			_log.Warning(ex, "Could not resolve the link {Path}", path);
			return null;
		}
	}

	public IEnumerable<string> FindFiles(string directory, string fileName)
	{
		try
		{
			return Directory.GetFiles(directory, fileName, SearchOption.AllDirectories);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			_log.Debug(ex, "Could not search {Directory} for {File}", directory, fileName);
			return [];
		}
	}

	public async Task<string?> ReadVersionOutputAsync(string executablePath, CancellationToken cancellationToken)
	{
		var psi = new ProcessStartInfo(executablePath)
		{
			RedirectStandardOutput = true,
			RedirectStandardError  = true,
			UseShellExecute        = false,
			CreateNoWindow         = true,
		};
		psi.ArgumentList.Add(Constants.Codex.Launch.VersionArgument);

		try
		{
			using var process = Process.Start(psi);
			if (process is null)
				return null;

			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(Constants.Codex.Discovery.VersionTimeout);
			try
			{
				var output = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
				await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
				return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is [var first, ..] ? first : null;
			}
			catch (OperationCanceledException)
			{
				process.Kill(entireProcessTree: true);
				_log.Warning("{Exe} --version did not answer within {Timeout}", executablePath, Constants.Codex.Discovery.VersionTimeout);
				return null;
			}
		}
		catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
		{
			_log.Warning(ex, "Could not run {Exe} --version", executablePath);
			return null;
		}
	}
}
