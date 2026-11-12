using System;
using System.Collections.Concurrent;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ISessionEpochTracker" />
/// <remarks>Created by Claude</remarks>
public sealed class SessionEpochTracker : ISessionEpochTracker, IDisposable
{
	private readonly ISessionFileService _files;
	private readonly ConcurrentDictionary<string, int> _epochs = new(StringComparer.OrdinalIgnoreCase);

	public SessionEpochTracker(ISessionFileService files)
	{
		_files = files;
		_files.SessionFileRewritten += OnSessionFileRewritten;
	}

	/// <inheritdoc />
	public int GetEpoch(string fileName) => _epochs.TryGetValue(fileName, out var epoch) ? epoch : 0;

	public void Dispose() => _files.SessionFileRewritten -= OnSessionFileRewritten;

	private void OnSessionFileRewritten(object? sender, SessionFileRewrittenEventArgs e) =>
		_epochs.AddOrUpdate(e.FileName, 1, (_, epoch) => epoch + 1);
}
