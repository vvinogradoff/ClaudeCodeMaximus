using System;
using System.Threading;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class UiThreadTracker : IUiThreadTracker
{
	private int _uiThreadId;

	public int? UiThreadId
	{
		get
		{
			var id = Volatile.Read(ref _uiThreadId);
			return id == 0 ? null : id;
		}
	}

	public void CaptureCurrentThread() => Volatile.Write(ref _uiThreadId, Environment.CurrentManagedThreadId);

	public bool IsUiThread() => UiThreadId == Environment.CurrentManagedThreadId;
}
