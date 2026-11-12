using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace TessynDesktop.Services;

/// <summary>
/// The method table the <see cref="TdcpDispatcher"/> is built from. Each <see cref="IRemoteRpcModule"/> adds its
/// methods here; a method is reachable over the relay only when added with <c>remoteCallable: true</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TdcpMethodRegistry
{
	private readonly Dictionary<string, TdcpMethodEntry> _entries = new(StringComparer.Ordinal);

	/// <summary>Every registered method.</summary>
	public IReadOnlyCollection<TdcpMethodEntry> Entries => _entries.Values;

	/// <summary>Adds an asynchronous method. Throws <see cref="InvalidOperationException"/> for a blank or duplicate name.</summary>
	public void Add(string name, Func<JsonNode?, CancellationToken, Task<object?>> handler, bool remoteCallable)
	{
		if (string.IsNullOrWhiteSpace(name))
			throw new InvalidOperationException("A method name is required.");

		if (!_entries.TryAdd(name, new TdcpMethodEntry(name, handler, remoteCallable)))
			throw new InvalidOperationException($"Method '{name}' is already registered.");
	}

	/// <summary>Adds a method that completes synchronously.</summary>
	public void AddSync(string name, Func<JsonNode?, object?> handler, bool remoteCallable) =>
		Add(name, (@params, _) => Task.FromResult(handler(@params)), remoteCallable);

	/// <summary>Looks a method up by exact name.</summary>
	public bool TryGet(string name, out TdcpMethodEntry entry) => _entries.TryGetValue(name, out entry!);
}
