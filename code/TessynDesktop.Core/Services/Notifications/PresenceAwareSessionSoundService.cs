using System.Collections.Generic;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// Wraps the host's <see cref="ISessionSoundService"/> (package J2): while a connected client has registered the <c>sound</c> channel the host does
/// not play the completion sound (the client plays its own on <c>turn.completed</c>). The Settings "Test" button always plays.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PresenceAwareSessionSoundService : ISessionSoundService
{
	private readonly ISessionSoundService _host;
	private readonly IClientPresenceRegistry _presence;

	public PresenceAwareSessionSoundService(ISessionSoundService host, IClientPresenceRegistry presence)
	{
		_host     = host;
		_presence = presence;
	}

	/// <inheritdoc />
	public void PlayCompletionSound(string nodeId)
	{
		if (_presence.IsPresented(RemoteProtocol.Notifications.SoundChannel))
			return;

		_host.PlayCompletionSound(nodeId);
	}

	/// <inheritdoc />
	public void PlayTestSound(string filePath) => _host.PlayTestSound(filePath);

	/// <inheritdoc />
	public IReadOnlyList<string> GetAvailableSounds() => _host.GetAvailableSounds();
}
