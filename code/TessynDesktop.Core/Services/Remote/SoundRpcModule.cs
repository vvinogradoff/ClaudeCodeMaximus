using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// <c>sound.list</c> and <c>sound.test</c> (package J3): the .wav files of the Windows sound library and a test play of one, for the Settings sound
/// picker. Loopback only: the files are paths on this machine and the test plays through its speakers.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SoundRpcModule : IRemoteRpcModule
{
	private readonly ISessionSoundService _sounds;

	public SoundRpcModule(ISessionSoundService sounds)
	{
		_sounds = sounds;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.AddSync(RemoteProtocol.SoundApi.ListMethod, _ => new SoundListResult([.. _sounds.GetAvailableSounds().Select(f => new SoundDto(f))]), remoteCallable: false);
		registry.AddSync(RemoteProtocol.SoundApi.TestMethod, Test, remoteCallable: false);
	}

	private object? Test(JsonNode? @params)
	{
		var file = SettingsPatchReader.WavFile("file", TdcpParams.RequireString(@params, "file"));
		if (!File.Exists(file))
			return new SoundTestResult(Played: false);

		_sounds.PlayTestSound(file);
		return new SoundTestResult(Played: true);
	}
}
