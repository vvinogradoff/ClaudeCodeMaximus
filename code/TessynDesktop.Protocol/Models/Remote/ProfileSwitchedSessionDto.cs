namespace TessynDesktop.Models.Remote;

/// <summary>One session touched by <c>profile.switch</c>.</summary>
/// <param name="NodeId">The session.</param>
/// <param name="Name">Its name.</param>
/// <param name="FromProfileId">The profile it was on: <c>"default"</c> or a stored profile's id.</param>
/// <param name="FromProfileName">That profile's display name.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ProfileSwitchedSessionDto(string NodeId, string Name, string FromProfileId, string FromProfileName);
