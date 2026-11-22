using System;
using System.Threading.Tasks;
using TessynDesktop.Codex.Protocol;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Codex;

public sealed partial class CodexUsageService
{
	/// <summary>One profile's reading and fetch schedule.</summary>
	/// <remarks>Created by Claude</remarks>
	private sealed class Entry
	{
		public CodexRateLimitSnapshot? Snapshot { get; set; }
		public DateTimeOffset? FetchedAt { get; set; }
		public UsageState LastState { get; set; } = UsageState.Unavailable;
		public DateTimeOffset NextFetchAt { get; set; } = DateTimeOffset.MinValue;
		public Task? InFlight { get; set; }
	}
}
