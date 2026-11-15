using System;
using System.Threading.Tasks;

namespace TessynDesktop.Services;

/// <summary>Ordering helper for heavy startup file work, so two full-file passes never compete (ISSUE.016).</summary>
/// <remarks>Created by Claude</remarks>
public static class StartupWork
{
	/// <summary>
	/// Runs <paramref name="work"/> on the thread pool once <paramref name="prerequisite"/> has finished, however it
	/// finished (completed, faulted or cancelled). Never runs it on the caller's thread and never throws.
	/// </summary>
	public static Task RunAfter(Task prerequisite, Action work)
		=> prerequisite.ContinueWith(_ => work(), TaskScheduler.Default);
}
