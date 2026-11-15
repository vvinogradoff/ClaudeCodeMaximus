namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>
/// Deletes a test's temp folder. A debounced <c>JsonClientStateStore</c> write (<c>wpf.json.tmp</c>, then an atomic replace), a WebView2 helper or a virus scanner can still
/// hold a file for a moment when <c>Dispose</c> runs; a delete that fails with "being used by another process" is retried until the holder is done, and a folder that a
/// late write recreated is deleted again, instead of failing a test that already passed.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class TestDirectory
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);
	private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(25);

	public static void Delete(string path)
	{
		DeleteWithRetry(path);

		// A write that was already in flight when the folder went can bring it back; let it land, then clean up once more.
		Thread.Sleep(Step * 2);
		if (Directory.Exists(path))
			DeleteWithRetry(path);
	}

	private static void DeleteWithRetry(string path)
	{
		var deadline = DateTime.UtcNow + Patience;
		while (true)
		{
			try
			{
				if (Directory.Exists(path))
					Directory.Delete(path, recursive: true);
				return;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				if (DateTime.UtcNow >= deadline)
					throw;

				Thread.Sleep(Step);
			}
		}
	}
}
