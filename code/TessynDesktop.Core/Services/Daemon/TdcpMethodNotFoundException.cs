using System;

namespace TessynDesktop.Services;

/// <summary>The requested method is unknown (or not callable on this transport); reported as JSON-RPC error -32601.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TdcpMethodNotFoundException(string method) : Exception(method);
