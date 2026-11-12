using System;

namespace TessynDesktop.Services;

/// <summary>A daemon-level failure whose message is safe to show the caller; reported as JSON-RPC error -32000.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TdcpException(string message) : Exception(message);
