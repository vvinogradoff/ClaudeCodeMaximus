using System.Threading;

namespace TessynDesktop.Services;

public sealed partial class CodexSessionEngine
{
	/// <summary>
	/// The node's running turn, for <see cref="Stop"/> and <see cref="InterruptAsync"/>: cancelling it starts the FR.33.8 interrupt in the turn's
	/// own flow (the turn's cancellation token is linked to the caller's).
	/// </summary>
	/// <remarks>Created by Claude</remarks>
	private sealed class RunningTurn
	{
		private readonly CancellationTokenSource _cancel;

		public RunningTurn(CancellationTokenSource cancel, string profileId)
		{
			_cancel   = cancel;
			ProfileId = profileId;
		}

		/// <summary>The ChatGPT profile the turn runs on (FR.33.13: its sessions get the profile's auth error).</summary>
		public string ProfileId { get; }

		/// <summary>The thread, once it is known (the idle unload skips a thread with a turn running).</summary>
		public string? ThreadId { get; set; }

		public void Cancel()
		{
			try
			{
				_cancel.Cancel();
			}
			catch (System.ObjectDisposedException)
			{
				// The turn ended meanwhile.
			}
		}
	}
}
