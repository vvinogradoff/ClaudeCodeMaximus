using System.Collections.Concurrent;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>
/// Runs an async test body on one dedicated thread with a <see cref="SynchronizationContext"/> that queues continuations to it, as the WPF dispatcher does: code after an
/// <c>await</c> never runs concurrently with the code that awaited. Tests of view-model logic that is only correct on the UI thread (a file copy finishing while the pane
/// switches session) use it instead of the thread pool, where such continuations race the caller.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class UiThread
{
	public static Task RunAsync(Func<Task> body)
	{
		var done = new TaskCompletionSource();
		var thread = new Thread(() =>
		{
			var context = new QueueContext();
			SynchronizationContext.SetSynchronizationContext(context);
			Task task;
			try
			{
				task = body();
			}
			catch (Exception ex)
			{
				done.SetException(ex);
				return;
			}

			task.ContinueWith(_ => context.Complete(), TaskScheduler.Default);
			context.Pump();
			if (task.IsFaulted)
				done.SetException(task.Exception!.InnerExceptions);
			else if (task.IsCanceled)
				done.SetCanceled();
			else
				done.SetResult();
		}) { IsBackground = true, Name = "test-ui-thread" };
		thread.Start();
		return done.Task;
	}

	private sealed class QueueContext : SynchronizationContext
	{
		private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

		public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

		public override void Send(SendOrPostCallback d, object? state) => Post(d, state);

		public void Complete() => _queue.CompleteAdding();

		public void Pump()
		{
			foreach (var (callback, state) in _queue.GetConsumingEnumerable())
				callback(state);
		}
	}
}
