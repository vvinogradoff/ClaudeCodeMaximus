using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Transcript;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>The turns of one session that are in flight (the running one first, then the queued ones) and the last turn's error.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTurnsState
{
	public List<TurnLiveState> Turns { get; } = [];

	/// <summary>The error of the last turn, kept until the next send.</summary>
	public string? Error { get; set; }

	public bool HasActive => Turns.Any(t => t.IsActive);

	public bool IsEmpty => Turns.Count == 0 && Error == null;

	public TurnLiveState GetOrAdd(string turnId)
	{
		var turn = Turns.Find(t => t.TurnId == turnId);
		if (turn != null)
			return turn;

		turn = new TurnLiveState(turnId);
		Turns.Add(turn);
		return turn;
	}

	/// <summary>
	/// The view of this session's live row; null when there is nothing to show. <paramref name="daemonRunning"/> is the daemon's running flag:
	/// when it is set and no turn of this client is known (another client's turn, or a reconnect that lost the stream) a generic working row shows.
	/// </summary>
	public TranscriptLiveView? Build(bool daemonRunning, long? genericSinceUnixMs)
	{
		var running = Turns.Find(t => t.IsActive);
		var queued = Turns.Where(t => !t.IsActive).Select((_, i) => string.Format(Constants.TurnTexts.QueuedRow, i + 1)).ToList();
		if (running == null && Turns.Count == 0 && daemonRunning)
			return new TranscriptLiveView(Constants.TurnTexts.Working, string.Empty, string.Empty, [], [], genericSinceUnixMs, Error, null);

		if (running == null)
			return queued.Count == 0 && Error == null
				? null
				: new TranscriptLiveView(queued.Count == 0 ? null : Constants.TurnTexts.Waiting, string.Empty, string.Empty, [], queued, null, Error, null);

		return new TranscriptLiveView(StatusOf(running), running.Text, running.Thinking, running.Tools.ToList(), queued, running.StartedAtUnixMs, Error, running.Usage);
	}

	private static string StatusOf(TurnLiveState turn)
	{
		if (turn.State == TurnState.Retrying)
			return turn.Detail switch
			{
				RemoteProtocol.TurnStream.RetryContext => Constants.TurnTexts.ResumingSession,
				RemoteProtocol.TurnStream.RetryAuth => Constants.TurnTexts.RetryingAuth,
				_ => Constants.TurnTexts.Retrying,
			};

		return turn.TextLength == 0 ? Constants.TurnTexts.Thinking : Constants.TurnTexts.Responding;
	}
}
