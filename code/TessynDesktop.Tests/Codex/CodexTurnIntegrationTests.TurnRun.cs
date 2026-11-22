using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using TessynDesktop.Codex;
using TessynDesktop.Codex.Events;

namespace TessynDesktop.Tests.Codex;

public sealed partial class CodexTurnIntegrationTests
{
	/// <summary>One turn driven through the host: the mapper fed from the host's notifications, and the events it produced.</summary>
	private sealed class TurnRun
	{
		private readonly object _lock = new();

		public TurnRun(string threadId)
		{
			ThreadId = threadId;
			Mapper   = new CodexTurnMapper(threadId);
		}

		public string ThreadId { get; }
		public string TurnId { get; private set; } = string.Empty;
		public CodexTurnMapper Mapper { get; }
		public ConcurrentQueue<CodexTurnEvent> Events { get; } = new();

		// Notifications can arrive before the turn/start answer is processed; the mapper learns the id from turn/started then.
		public void SetTurn(string turnId)
		{
			TurnId = turnId;
			lock (_lock)
				Mapper.SetTurnId(turnId);
		}

		public void OnNotification(CodexNotification notification)
		{
			lock (_lock)
			{
				foreach (var e in Mapper.Map(notification))
					Events.Enqueue(e);
			}
		}

		public async Task<CodexTurnCompletedEvent> CompletedAsync()
		{
			var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
			while (Mapper.Completion is null)
			{
				if (DateTime.UtcNow > deadline)
					throw new TimeoutException("The turn did not complete.");
				await Task.Delay(10);
			}
			return Mapper.Completion;
		}
	}
}
