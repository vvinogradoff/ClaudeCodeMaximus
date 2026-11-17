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

	/// <summary>True for a windowless daemon host.</summary>
	public bool IsBackground { get; init; }

	public static BackgroundHostOptions Parse(IEnumerable<string> args) =>
		new() { IsBackground = args.Any(a => string.Equals(a, BackgroundArgument, StringComparison.OrdinalIgnoreCase)) };
}
