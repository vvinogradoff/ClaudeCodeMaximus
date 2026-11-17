using System;
using System.Collections.Generic;
using System.Linq;

namespace TessynDesktop.Services;

/// <summary>
/// The start mode of the daemon host process. <c>--background</c> (decision A23 Q6) hosts the daemon with no main window and no taskbar entry; only a tray icon
/// (Exit) remains. Since A47 it is the only mode: a start without it behaves the same (the host logs a note). The WPF client starts the host this way when no daemon answers.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class BackgroundHostOptions
{
	public const string BackgroundArgument = "--background";

	/// <summary>The folder for all of the host's data instead of <c>%AppData%\TessynDesktop</c> (logs, settings, sessions, tokens, <c>daemon.json</c>); for tests and side-by-side runs.</summary>
	public const string DataRootArgument = "--data-root";

	/// <summary>The path of <c>daemon.json</c> instead of the one in the data root.</summary>
	public const string DaemonFileArgument = "--daemon-file";

	/// <summary>A suffix for the guard mutex and the exit event, so an isolated host never meets the real one.</summary>
	public const string InstanceArgument = "--instance";

	/// <summary>True for a windowless daemon host.</summary>
	public bool IsBackground { get; init; }

	public string? DataRoot { get; init; }

	public string? DaemonFile { get; init; }

	public string? Instance { get; init; }

	/// <summary>The guard mutex name: the real one, or the real one with <c>.&lt;instance&gt;</c>.</summary>
	public string GuardName => WithInstance(TessynDesktop.Models.Remote.RemoteProtocol.Discovery.HostMutexName, Instance);

	/// <summary>The exit event name: the real one, or the real one with <c>.&lt;instance&gt;</c>.</summary>
	public string ExitEventName => WithInstance(TessynDesktop.Models.Remote.RemoteProtocol.Discovery.HostExitEventName, Instance);

	/// <summary>The mutex or event name for <paramref name="instance"/> (null or empty: the real host's name).</summary>
	public static string WithInstance(string name, string? instance) => string.IsNullOrEmpty(instance) ? name : name + "." + instance;

	public static BackgroundHostOptions Parse(IEnumerable<string> args)
	{
		var list = args.ToList();
		return new BackgroundHostOptions
		{
			IsBackground = list.Any(a => string.Equals(a, BackgroundArgument, StringComparison.OrdinalIgnoreCase)),
			DataRoot = ValueOf(list, DataRootArgument),
			DaemonFile = ValueOf(list, DaemonFileArgument),
			Instance = ValueOf(list, InstanceArgument),
		};
	}

	private static string? ValueOf(List<string> args, string name)
	{
		var index = args.FindIndex(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
		return index >= 0 && index + 1 < args.Count && !string.IsNullOrWhiteSpace(args[index + 1]) ? args[index + 1] : null;
	}
}
