using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// Builds the <see cref="ProcessStartInfo"/> and spawns a claude CLI process, including the
/// Windows .cmd-file retry-via-cmd.exe fallback and the profile/Ollama/proxy environment-variable
/// setup. Shared between <see cref="ClaudeProcessManager"/> (One-shot Mode, one process per turn)
/// and the Streaming Session Mode process manager (FR.19, one persistent process across turns) —
/// spawning is identical between the two; only what happens to stdin/stdout afterward differs.
/// Extracted from <c>ClaudeProcessManager.TryStartProcess</c> (Stage 2 of FR.19).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClaudeProcessLauncher
{
	private static readonly ILogger _log = Log.ForContext<ClaudeProcessLauncher>();

	// Encoding.UTF8 writes a byte-order-mark preamble on the first write to a redirected stream.
	// One-shot Mode's --input-format text tolerates this on stdin; a stream-json turn does not —
	// the CLI's strict per-line JSON parser rejects a leading BOM with "Unrecognized token" (found
	// while verifying Streaming Session Mode, FR.19, against the real CLI). Use the BOM-less
	// variant for all three redirected streams so neither mode is affected by it.
	private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

	private readonly IAppSettingsService _appSettings;

	// ISSUE.010: keys the per-CLAUDE_CONFIG_DIR spawn lock below. All "Default profile" spawns
	// (profileConfigDir null/empty — no CLAUDE_CONFIG_DIR override) share one key since they all
	// hit the same literal ~/.claude credential store regardless of caller.
	private const string DefaultProfileLockKey = "(default-profile)";
	private readonly ConcurrentDictionary<string, object> _spawnLocks = new(StringComparer.OrdinalIgnoreCase);

	public ClaudeProcessLauncher(IAppSettingsService appSettings)
	{
		_appSettings = appSettings;
	}

	/// <summary>
	/// Starts a claude process with the given arguments. On Windows, retries via <c>cmd.exe /c</c>
	/// if the direct spawn fails — 'claude' is often a .cmd file which requires cmd.exe to launch
	/// when UseShellExecute=false.
	/// </summary>
	/// <remarks>
	/// ISSUE.010: the spawn itself is serialized per <paramref name="profileConfigDir"/> — the CLI
	/// checks/refreshes its OAuth token at startup, and two processes launched at the same instant
	/// against the same <c>CLAUDE_CONFIG_DIR</c> can race a refresh-token rotation, leaving whichever
	/// loses unable to authenticate ("OAuth session expired and could not be refreshed") even though
	/// the account itself is fine. The lock is held only around the launch call, not the process's
	/// subsequent lifetime, so turns sharing a profile still run fully in parallel once started.
	/// </remarks>
	public Process? Start(
		string claudePath,
		string arguments,
		string workingDirectory,
		string? profileConfigDir = null,
		string? ollamaBaseUrl = null)
	{
		var spawnLock = _spawnLocks.GetOrAdd(
			string.IsNullOrEmpty(profileConfigDir) ? DefaultProfileLockKey : profileConfigDir,
			_ => new object());

		lock (spawnLock)
		{
			var process = TryStartProcess(claudePath, arguments, workingDirectory, profileConfigDir, ollamaBaseUrl);

			if (process == null && OperatingSystem.IsWindows())
			{
				var cmdArgs = $"/c \"{claudePath}\" {arguments}";
				_log.Debug("Direct spawn failed — retrying via cmd.exe /c. Args={CmdArgs}", cmdArgs);
				process = TryStartProcess("cmd.exe", cmdArgs, workingDirectory, profileConfigDir, ollamaBaseUrl);
			}

			return process;
		}
	}

	private Process? TryStartProcess(
		string fileName,
		string arguments,
		string workingDirectory,
		string? profileConfigDir = null,
		string? ollamaBaseUrl = null)
	{
		var psi = new ProcessStartInfo(fileName, arguments)
		{
			WorkingDirectory       = workingDirectory,
			RedirectStandardInput  = true,
			RedirectStandardOutput = true,
			RedirectStandardError  = true,
			UseShellExecute        = false,
			CreateNoWindow         = true,
			StandardInputEncoding  = Utf8NoBom,
			StandardOutputEncoding = Utf8NoBom,
			StandardErrorEncoding  = Utf8NoBom,
		};

		// Remove CLAUDECODE so claude doesn't refuse to run inside another claude session.
		psi.Environment.Remove("CLAUDECODE");

		if (!string.IsNullOrEmpty(ollamaBaseUrl))
		{
			// Local model routing (FR.12.14): redirect Claude SDK to the Ollama endpoint.
			// Profile auth and proxy are not used — billing is local hardware.
			// ANTHROPIC_BASE_URL must be the bare host without /v1 — the Anthropic SDK
			// appends /v1/messages internally, so including /v1 creates a double path.
			// ANTHROPIC_API_KEY must be non-empty; the SDK rejects an empty string before
			// sending the request, so we use the "ollama" placeholder for both.
			psi.Environment["ANTHROPIC_BASE_URL"]   = ollamaBaseUrl.TrimEnd('/');
			psi.Environment["ANTHROPIC_AUTH_TOKEN"] = Constants.Ollama.AuthToken;
			psi.Environment["ANTHROPIC_API_KEY"]    = Constants.Ollama.AuthToken;
		}
		else
		{
			// Anthropic routing: set profile isolation and optional proxy.
			if (!string.IsNullOrEmpty(profileConfigDir))
				psi.Environment["CLAUDE_CONFIG_DIR"] = profileConfigDir;

				// FR.12.20: offload the CLI's internal background/small-task calls onto Haiku so
				// they don't compete with the session's primary (possibly Opus/Sonnet) model. Not
				// set for Ollama routing above — that would force a second, distinct local model to
				// stay resident in memory alongside the primary one for no benefit.
				psi.Environment["ANTHROPIC_SMALL_FAST_MODEL"] = Constants.ClaudeAssist.PreferredModelId;

			var httpsProxy = _appSettings.Settings.HttpsProxy;
			if (!string.IsNullOrWhiteSpace(httpsProxy))
			{
				psi.Environment["HTTPS_PROXY"] = httpsProxy;
				psi.Environment["HTTP_PROXY"]  = httpsProxy;
				psi.Environment["NODE_TLS_REJECT_UNAUTHORIZED"] = "0";
				_log.Information("HTTPS proxy configured: {Proxy}", httpsProxy);
			}
		}

		try
		{
			return Process.Start(psi);
		}
		catch (Win32Exception ex)
		{
			_log.Warning("Win32Exception starting {FileName}: {Message}", fileName, ex.Message);
			return null;
		}
	}
}
