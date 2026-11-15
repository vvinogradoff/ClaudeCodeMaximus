using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>Scripted transcript of one session of the <see cref="FakeTessynClient"/>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeSession
{
	public string Name { get; set; } = "session";

	public int Epoch { get; set; } = 1;

	public bool Running { get; set; }

	/// <summary>What <c>session.get</c> reports as the settings the session was last used with.</summary>
	public LastUsedSettingsDto LastUsed { get; set; } = new("default", "default", "default", "[Sys] Default");

	public List<SessionEntryDto> Entries { get; set; } = [];

	public Dictionary<int, SessionEntryDto> FullEntries { get; } = [];

	public List<SessionTagDto> Tags { get; set; } = [];

	/// <summary>The daemon's auth-error flag of the session (<c>SessionStateDto.authError</c>).</summary>
	public bool AuthError { get; set; }

	public SessionStateDto State(string nodeId) => new(nodeId, Running, false, null, false, null, null, false, null, AuthError);

	public static FakeSession WithEntries(int count, string name = "session")
	{
		var session = new FakeSession { Name = name };
		for (var i = 0; i < count; i++)
			session.Entries.Add(MakeEntry(i));
		return session;
	}

	public static SessionEntryDto MakeEntry(int index) => new()
	{
		Index = index,
		Timestamp = new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero).AddSeconds(index),
		Role = index % 2 == 0 ? "user" : "assistant",
		Content = $"entry {index}",
	};
}
