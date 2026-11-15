using System;

namespace TessynDesktop.Services;

/// <summary>A profile administration request that cannot be done (an unknown profile); the message is safe to show the caller.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProfileAdminException(string message) : Exception(message);
