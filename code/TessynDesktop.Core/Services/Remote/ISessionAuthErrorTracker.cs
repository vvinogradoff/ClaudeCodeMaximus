using System;

namespace TessynDesktop.Services;

/// <summary>
/// Remembers, per session, that the profile's authentication failed (the result or the stderr of a turn, or of an Auto-Compact, said the
/// credentials are invalid) until a turn succeeds again. The remote clients read it as <c>SessionStateDto.AuthError</c>. A profile
/// re-authentication ends the error for the sessions it affects: it calls <see cref="ClearAll"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionAuthErrorTracker
{
	/// <summary>Raised, with the session's NodeId, when the flag of a session changed (never for a repeated value). May be raised on any thread.</summary>
	event Action<string>? Changed;

	/// <summary>True when the session's last auth outcome was a failure.</summary>
	bool Has(string nodeId);

	/// <summary>Records the outcome: true on an authentication failure, false when a result succeeded.</summary>
	void Set(string nodeId, bool hasAuthError);

	/// <summary>Clears the flag of every session (a profile was re-authenticated).</summary>
	void ClearAll();
}