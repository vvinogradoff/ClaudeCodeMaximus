using System;
using System.Collections.Concurrent;
using System.Linq;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ISessionAuthErrorTracker" />
/// <remarks>Created by Claude</remarks>
public sealed class SessionAuthErrorTracker : ISessionAuthErrorTracker
{
	// Only the sessions in error are kept: the set is empty in the normal case.
	private readonly ConcurrentDictionary<string, byte> _failed = new(StringComparer.Ordinal);

	/// <inheritdoc />
	public event Action<string>? Changed;

	/// <inheritdoc />
	public bool Has(string nodeId) => !string.IsNullOrEmpty(nodeId) && _failed.ContainsKey(nodeId);

	/// <inheritdoc />
	public void Set(string nodeId, bool hasAuthError)
	{
		if (string.IsNullOrEmpty(nodeId))
			return;

		var changed = hasAuthError ? _failed.TryAdd(nodeId, 0) : _failed.TryRemove(nodeId, out _);
		if (changed)
			Changed?.Invoke(nodeId);
	}

	private readonly ConcurrentDictionary<string, string> _limits = new(StringComparer.Ordinal);

	/// <inheritdoc />
	public string? GetLimitError(string nodeId) => !string.IsNullOrEmpty(nodeId) && _limits.TryGetValue(nodeId, out var text) ? text : null;

	/// <inheritdoc />
	public void SetLimitError(string nodeId, string? text)
	{
		if (string.IsNullOrEmpty(nodeId))
			return;

		bool changed;
		if (string.IsNullOrEmpty(text))
			changed = _limits.TryRemove(nodeId, out _);
		else
		{
			changed = !_limits.TryGetValue(nodeId, out var old) || old != text;
			_limits[nodeId] = text;
		}

		if (changed)
			Changed?.Invoke(nodeId);
	}

	/// <inheritdoc />
	public void ClearAll()
	{
		foreach (var nodeId in _failed.Keys.ToArray())
			Set(nodeId, false);
	}
}