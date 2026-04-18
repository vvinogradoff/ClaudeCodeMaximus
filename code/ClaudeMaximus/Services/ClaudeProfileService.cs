using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;

namespace ClaudeMaximus.Services;

/// <remarks>Created by Claude</remarks>
public sealed class ClaudeProfileService : IClaudeProfileService
{
	private static readonly ILogger _log = Log.ForContext<ClaudeProfileService>();

	public string ProfilesRootDirectory { get; }

	public ClaudeProfileService()
	{
		var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		ProfilesRootDirectory = Path.Combine(appData, Constants.AppDataFolderName, Constants.ProfilesFolderName);
	}

	public async Task<string?> GetAccountEmailAsync(string claudePath, string? configDir)
	{
		var output = await RunClaudeCommandAsync(claudePath, "auth status", configDir);
		if (string.IsNullOrWhiteSpace(output))
			return null;

		try
		{
			using var doc = JsonDocument.Parse(output);
			var root = doc.RootElement;

			if (root.TryGetProperty("loggedIn", out var loggedIn) && loggedIn.GetBoolean()
			    && root.TryGetProperty("email", out var email))
				return email.GetString();
		}
		catch (JsonException ex)
		{
			_log.Warning(ex, "Failed to parse claude auth status output for configDir {ConfigDir}", configDir);
		}

		return null;
	}

	public async Task LaunchAuthLoginAsync(string claudePath, string configDir)
	{
		_log.Information("Launching interactive auth login with configDir {ConfigDir}", configDir);

		// Ensure the config directory exists
		Directory.CreateDirectory(configDir);

		Process? process;

		if (OperatingSystem.IsWindows())
		{
			// On Windows, 'claude' is a .cmd wrapper around node.js. Nested quoting
			// in cmd.exe /c and .cmd wrapper interactions cause the process to exit
			// before the OAuth browser callback arrives. Writing a temporary .bat file
			// avoids all quoting issues and ensures cmd.exe properly waits for the
			// entire auth flow to complete.
			var batPath = Path.Combine(configDir, "_auth_login.bat");
			var batContent = $"""
				@echo off
				set "CLAUDE_CONFIG_DIR={configDir}"
				call "{claudePath}" auth login
				echo.
				echo Authentication complete. Press any key to close.
				pause >nul
				""";
			await File.WriteAllTextAsync(batPath, batContent);

			process = TryStartVisibleProcess(batPath, string.Empty);
		}
		else
		{
			// On macOS/Linux, UseShellExecute=true can't set env vars.
			// Use UseShellExecute=false so we can pass CLAUDE_CONFIG_DIR.
			// claude auth login opens the browser itself, so we don't need
			// a visible terminal window.
			var psi = new ProcessStartInfo(claudePath, "auth login")
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};
			psi.Environment["CLAUDE_CONFIG_DIR"] = configDir;
			try { process = Process.Start(psi); }
			catch (System.ComponentModel.Win32Exception ex)
			{
				_log.Warning("Failed to start claude auth login: {Message}", ex.Message);
				process = null;
			}
		}

		if (process == null)
		{
			_log.Error("Failed to start claude auth login for configDir {ConfigDir}", configDir);
			return;
		}

		using (process)
		{
			// Drain output to prevent deadlocks when using RedirectStandardOutput
			if (process.StartInfo.RedirectStandardOutput)
			{
				_ = process.StandardOutput.ReadToEndAsync();
				_ = process.StandardError.ReadToEndAsync();
			}
			await process.WaitForExitAsync();
			_log.Information("Auth login for configDir {ConfigDir} exited with code {ExitCode}", configDir, process.ExitCode);
		}

		// Clean up temp bat file
		if (OperatingSystem.IsWindows())
		{
			var batPath = Path.Combine(configDir, "_auth_login.bat");
			try { File.Delete(batPath); }
			catch { /* best-effort cleanup */ }
		}
	}

	private async Task<string?> RunClaudeCommandAsync(string claudePath, string args, string? configDir)
	{
		Process? process = TryStartHiddenProcess(claudePath, args, configDir);

		if (process == null && OperatingSystem.IsWindows())
		{
			var cmdArgs = $"/c \"{claudePath}\" {args}";
			process = TryStartHiddenProcess("cmd.exe", cmdArgs, configDir);
		}

		if (process == null)
			return null;

		using (process)
		{
			var output = await process.StandardOutput.ReadToEndAsync();
			await process.WaitForExitAsync();
			return output;
		}
	}

	private static Process? TryStartHiddenProcess(string fileName, string arguments, string? configDir)
	{
		var psi = new ProcessStartInfo(fileName, arguments)
		{
			RedirectStandardOutput = true,
			RedirectStandardError  = true,
			UseShellExecute        = false,
			CreateNoWindow         = true,
			StandardOutputEncoding = Encoding.UTF8,
		};

		if (!string.IsNullOrEmpty(configDir))
			psi.Environment["CLAUDE_CONFIG_DIR"] = configDir;

		try
		{
			return Process.Start(psi);
		}
		catch (Win32Exception ex)
		{
			_log.Warning("Failed to start {FileName}: {Message}", fileName, ex.Message);
			return null;
		}
	}

	private static Process? TryStartVisibleProcess(string fileName, string arguments, string? configDir = null)
	{
		var psi = new ProcessStartInfo(fileName, arguments)
		{
			UseShellExecute = true,
			CreateNoWindow  = false,
		};

		// Note: UseShellExecute=true does not support psi.Environment modifications.
		// For visible processes on Windows, env vars are set in the .bat file.
		// On other platforms, the caller handles env vars differently.

		try
		{
			return Process.Start(psi);
		}
		catch (Win32Exception ex)
		{
			_log.Warning("Failed to start visible {FileName}: {Message}", fileName, ex.Message);
			return null;
		}
	}
}
