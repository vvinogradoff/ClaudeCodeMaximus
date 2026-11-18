using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace TessynDesktop.WPF.Switch;

/// <summary>Runs the git executable from the PATH without a window; kills it (and its children) when it takes longer than the limit.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProcessGitRunner : IGitRunner
{
	public GitResult Run(string workingDirectory, IReadOnlyList<string> arguments, TimeSpan timeout)
	{
		var info = new ProcessStartInfo(Constants.Switch.GitExecutable)
		{
			WorkingDirectory = workingDirectory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = true,
			UseShellExecute = false,
			CreateNoWindow = true,
			StandardOutputEncoding = Encoding.UTF8,
		};
		foreach (var argument in arguments)
			info.ArgumentList.Add(argument);

		try
		{
			using var process = Process.Start(info);
			if (process == null)
				return GitResult.NotAvailable;

			process.StandardInput.Close();
			var output = process.StandardOutput.ReadToEndAsync();
			_ = process.StandardError.ReadToEndAsync();
			if (!process.WaitForExit(timeout))
			{
				Kill(process);
				return GitResult.OutOfTime;
			}

			process.WaitForExit(); // the redirected streams are complete only after this overload
			return new GitResult(true, false, process.ExitCode, output.GetAwaiter().GetResult());
		}
		catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or DirectoryNotFoundException)
		{
			return GitResult.NotAvailable;
		}
	}

	private static void Kill(Process process)
	{
		try
		{
			process.Kill(entireProcessTree: true);
		}
		catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
		{
		}
	}
}
