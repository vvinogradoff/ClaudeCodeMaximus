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

		public RunningTurn(CancellationTokenSource cancel)
		{
			_cancel = cancel;
		}

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
