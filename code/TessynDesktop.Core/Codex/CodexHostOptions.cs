using System;
using System.Collections.Generic;

namespace TessynDesktop.Codex;

/// <summary>How one <see cref="CodexAppServerHost"/> launches its app-server (FR.33.6). One host per <c>CODEX_HOME</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexHostOptions
{
	/// <summary>The native Codex exe (from <see cref="CodexExecutableLocator"/>; never a <c>.cmd</c> or Node shim).</summary>
	public required string ExecutablePath { get; init; }

	/// <summary>The profile id, for logs and crash reports (for example <c>codex-default</c> or <c>codex_1</c>).</summary>
	public required string ProfileId { get; init; }

	/// <summary>
	/// The <c>CODEX_HOME</c> of a stored profile; null for Codex Default, which leaves <c>CODEX_HOME</c> unset. A stored profile is also
	/// started with <c>-c cli_auth_credentials_store="file"</c> (FR.33.5).
	/// </summary>
	public string? CodexHome { get; init; }

	/// <summary>The process's working directory: the profile folder. Null means <see cref="CodexHome"/>, else <c>%USERPROFILE%\.codex</c>.</summary>
	public string? WorkingDirectory { get; init; }

	/// <summary>Tessyn's version, sent as <c>clientInfo.version</c>.</summary>
	public string ClientVersion { get; init; } = "0.0.0";

	/// <summary>More <c>-c key=value</c> overrides for every thread of the process.</summary>
	public IReadOnlyList<string> ConfigOverrides { get; init; } = [];

	public TimeSpan IdleShutdown { get; init; } = Constants.Codex.HostIdleShutdown;

	public TimeSpan RequestTimeout { get; init; } = Constants.Codex.RequestTimeout;

	public TimeSpan StopGrace { get; init; } = Constants.Codex.StopGrace;
}
