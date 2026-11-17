using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TessynDesktop.Services;

/// <summary><see cref="IUiDispatcher"/> over the host's message loop thread (the thread that owns the tray icon); created on that thread.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class WinFormsUiDispatcher : IUiDispatcher
{
	private readonly Control _control = new();

	public WinFormsUiDispatcher()
	{
		_ = _control.Handle; // creates the window handle now, on the message loop thread, so BeginInvoke works from any thread
	}

	public void Post(Action action) => _control.BeginInvoke(action);

	public Task InvokeAsync(Action action) =>
		InvokeAsync<object?>(() =>
		{
			action();
			return null;
		});

	public Task<T> InvokeAsync<T>(Func<T> func)
	{
		var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
		_control.BeginInvoke(() =>
		{
			try
			{
				completion.SetResult(func());
			}
			catch (Exception ex)
			{
				completion.SetException(ex);
			}
		});
		return completion.Task;
	}
}
