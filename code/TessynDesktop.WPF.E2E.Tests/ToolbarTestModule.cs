using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// A test-only daemon side of the W4 controls: <c>session.setOptions</c> (remembers the options and answers a no-change call with them),
/// <c>session.resetConversation</c>, <c>session.compact</c> and <c>localTools.get/set</c> (Streaming Session Mode is the real session module's). Every call is recorded.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ToolbarTestModule : IRemoteRpcModule
{
	private readonly object _gate = new();
	private bool? _autoCommit = false;
	private bool _autoDocument;
	private bool _sound = true;

	public ConcurrentQueue<string> Calls { get; } = new();

	public bool? AutoCommit
	{
		get { lock (_gate) return _autoCommit; }
	}

	public bool AutoDocument
	{
		get { lock (_gate) return _autoDocument; }
	}

	public void Register(TdcpMethodRegistry registry)
	{
		registry.AddSync("session.setOptions", SetOptions, remoteCallable: false);
		registry.AddSync("session.resetConversation", p => Record("reset:" + p!["nodeId"]!.GetValue<string>(), new SessionResetConversationResult(true)), remoteCallable: false);
		registry.AddSync("session.compact", p => Record("compact:" + p!["nodeId"]!.GetValue<string>(), new SessionCompactResult(true, false)), remoteCallable: false);
		registry.AddSync("localTools.get", p => Record("localTools.get", new LocalToolsDto(true, ["Read"], false, [], ["Read", "Bash"], [])), remoteCallable: false);
		registry.AddSync("localTools.set", p => Record("localTools.set", new LocalToolsDto(true, ["Read"], false, [], ["Read", "Bash"], [])), remoteCallable: false);
	}

	private T Record<T>(string call, T answer)
	{
		Calls.Enqueue(call);
		return answer;
	}

	private object? SetOptions(JsonNode? p)
	{
		var change = new List<string>();
		lock (_gate)
		{
			if (p is JsonObject obj && obj.ContainsKey("autoCommit"))
			{
				_autoCommit = p["autoCommit"]?.GetValue<bool>();
				change.Add("autoCommit=" + (_autoCommit?.ToString() ?? "none"));
			}

			if (p?["autoDocument"] is { } doc)
			{
				_autoDocument = doc.GetValue<bool>();
				change.Add("autoDocument=" + _autoDocument);
			}

			if (p?["playCompletionSound"] is { } sound)
			{
				_sound = sound.GetValue<bool>();
				change.Add("sound=" + _sound);
			}

			Calls.Enqueue(change.Count > 0 ? "setOptions:" + string.Join(",", change) : "readOptions");

			return new SessionSetOptionsResult(new SessionOptionsDto(_autoCommit, _autoDocument, _sound), null);
		}
	}
}
