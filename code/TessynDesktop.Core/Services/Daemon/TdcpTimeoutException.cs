using System;

namespace TessynDesktop.Services;

/// <summary>A daemon-level operation that ran out of time; reported as JSON-RPC error -32002 (the web server turns it into a 504).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TdcpTimeoutException(string message) : Exception(message);
