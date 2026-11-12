using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace TessynDesktop.Services;

/// <summary>One row of the dispatcher's method table.</summary>
/// <param name="Name">The JSON-RPC method name.</param>
/// <param name="Handler">Runs the method; the returned object is serialized with <see cref="TdcpJsonOptions.Default"/>.</param>
/// <param name="RemoteCallable">True when the relay transport may call the method.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TdcpMethodEntry(
	string Name,
	Func<JsonNode?, CancellationToken, Task<object?>> Handler,
	bool RemoteCallable);
