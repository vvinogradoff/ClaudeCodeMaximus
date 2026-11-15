using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>How the background host ends: the tray's Exit asks when sessions are active and terminates them first; a client's request through the named event does not ask again (review 459 M1, M2).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DaemonHostExitTests
{
	private sealed class Recorder
	{
		public List<string> Calls { get; } = [];

		public int Active { get; set; }

		public bool Answer { get; set; } = true;

		public string? Asked { get; private set; }

		public DaemonHostExit Create() => new(
			() => Active,
			message => { Asked = message; Calls.Add("confirm"); return Task.FromResult(Answer); },
			() => Calls.Add("terminate"),
			() => Calls.Add("shutdown"));
	}

	[Fact]
	public async Task Tray_NoActiveSessions_ExitsAtOnceWithoutAsking()
	{
		var recorder = new Recorder();

		Assert.True(await recorder.Create().RequestFromTrayAsync());

		Assert.Equal(["terminate", "shutdown"], recorder.Calls);
	}

	[Fact]
	public async Task Tray_ActiveSessions_AsksFirstThenTerminatesThemThenShutsDown()
	{
		var recorder = new Recorder { Active = 3 };

		Assert.True(await recorder.Create().RequestFromTrayAsync());

		Assert.Equal(["confirm", "terminate", "shutdown"], recorder.Calls);
		Assert.Contains("3 Claude Code sessions are currently active", recorder.Asked);
	}

	[Fact]
	public async Task Tray_ARefusal_ChangesNothing()
	{
		var recorder = new Recorder { Active = 2, Answer = false };

		Assert.False(await recorder.Create().RequestFromTrayAsync());

		Assert.Equal(["confirm"], recorder.Calls);
	}

	[Fact]
	public void AThrowingTerminate_StillShutsDownAndIsLogged_AndLaterRequestsAreNoOps()
	{
		// N2: a session in an odd state must never leave the host impossible to exit.
		var calls = new List<string>();
		var errors = new List<Exception>();
		var exit = new DaemonHostExit(() => 1, _ => Task.FromResult(true), () => throw new InvalidOperationException("odd session"), () => calls.Add("shutdown"), errors.Add);

		exit.Exit();
		exit.Exit();

		Assert.Equal(["shutdown"], calls);
		Assert.Equal("odd session", Assert.Single(errors).Message);
	}

	[Fact]
	public async Task TheTrayExit_WithAThrowingTerminate_StillEndsTheHost()
	{
		var calls = new List<string>();
		var exit = new DaemonHostExit(() => 2, _ => Task.FromResult(true), () => throw new InvalidOperationException("odd"), () => calls.Add("shutdown"));

		Assert.True(await exit.RequestFromTrayAsync());

		Assert.Equal(["shutdown"], calls);
	}

	[Fact]
	public async Task ASecondTrayClickWhileTheQuestionIsOpen_DoesNotOpenAnotherWindow()
	{
		// L-d.
		var answer = new TaskCompletionSource<bool>();
		var asked = 0;
		var exit = new DaemonHostExit(() => 2, _ => { asked++; return answer.Task; }, () => { }, () => { });

		var first = exit.RequestFromTrayAsync();
		var second = await exit.RequestFromTrayAsync();
		answer.SetResult(true);

		Assert.False(second);
		Assert.True(await first);
		Assert.Equal(1, asked);
	}

	[Fact]
	public void ConfirmMessage_UsesTheSingularForOneSession()
	{
		Assert.Contains("1 Claude Code session is currently active", DaemonHostExit.ConfirmMessage(1));
	}

	[Fact]
	public void AClientsRequest_TerminatesAndShutsDownWithoutAsking_AndOnlyOnce()
	{
		var recorder = new Recorder { Active = 5 };
		var exit = recorder.Create();

		exit.Exit();
		exit.Exit();

		Assert.Equal(["terminate", "shutdown"], recorder.Calls);
	}

	[Fact]
	public async Task ATrayExitAfterAClientsRequest_DoesNothingTwice()
	{
		var recorder = new Recorder();
		var exit = recorder.Create();
		exit.Exit();

		await exit.RequestFromTrayAsync();

		Assert.Equal(["terminate", "shutdown"], recorder.Calls);
	}

	[Fact]
	public async Task TheExitEvent_ReachesTheListener_AndIsFalseWithoutOne()
	{
		var name = @"Local\TessynDesktop.Test.Exit." + Guid.NewGuid().ToString("N");
		Assert.False(DaemonHostExitListener.Signal(name));

		var received = new TaskCompletionSource();
		using var listener = new DaemonHostExitListener(name);
		listener.Start(() => received.TrySetResult());

		Assert.True(DaemonHostExitListener.Signal(name));
		Assert.Equal(received.Task, await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(10))));
	}

	[Fact]
	public void TheDefaultEventName_IsTheOneTheWpfClientSets()
	{
		Assert.Equal(@"Local\TessynDesktop.DaemonHost.Exit", RemoteProtocol.Discovery.HostExitEventName);
	}
}
