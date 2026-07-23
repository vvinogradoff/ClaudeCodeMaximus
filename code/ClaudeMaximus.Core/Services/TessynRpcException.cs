using System;

namespace ClaudeMaximus.Services;

/// <summary>
/// Exception thrown when a Tessyn daemon RPC call returns a JSON-RPC error.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynRpcException : Exception
{
    public int Code { get; }

    public TessynRpcException(int code, string message) : base(message)
    {
        Code = code;
    }
}
