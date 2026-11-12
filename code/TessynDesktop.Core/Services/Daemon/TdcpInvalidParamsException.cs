using System;

namespace TessynDesktop.Services;

/// <summary>A required parameter is missing or has the wrong shape; reported as JSON-RPC error -32602.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TdcpInvalidParamsException(string message) : Exception(message);
