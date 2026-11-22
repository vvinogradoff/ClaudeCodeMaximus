using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// The tools <c>request_elevated_run</c>, <c>cancel_elevated_run</c> and <c>list_elevated_runs</c> (FR.15.16, FR.15.17). A Worker Session may call all three for its own
/// requests; the rules, the limits and the texts are in <see cref="IElevatedRunService"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class AgentMcpServer
{
	private async Task<JsonNode> RequestElevatedRunAsync(JsonNode? args, SessionNodeModel callerNode)
	{
		var outcome = await RequireElevatedRuns().RequestAsync(callerNode, new ElevatedRunRequestModel
		{
			Command        = GetOptionalString(args, "command"),
			WorkingDir     = GetOptionalString(args, "workingDir"),
			Reason         = GetOptionalString(args, "reason"),
			TimeoutMinutes = args?["timeoutMinutes"],
		});

		return BuildTextContent(string.Format(
			outcome.IsCreated ? Constants.ElevatedRuns.RequestedFormat : Constants.ElevatedRuns.AlreadyPendingFormat, outcome.Run.Id));
	}

	private JsonNode CancelElevatedRun(JsonNode? args, SessionNodeModel callerNode) =>
		BuildTextContent(RequireElevatedRuns().Cancel(callerNode, GetOptionalString(args, "requestId") ?? string.Empty));

	private JsonNode ListElevatedRuns(JsonNode? args, SessionNodeModel callerNode) =>
		BuildTextContent(RequireElevatedRuns().ListText(callerNode, GetOptionalString(args, "status")));

	private IElevatedRunService RequireElevatedRuns() =>
		_elevatedRuns ?? throw new McpToolException("Elevated runs are not available in this host.");

	private static JsonNode RequestElevatedRunSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"command":        { "type": "string", "description": "The full command line, one line, at most 4,000 characters. It is handed unchanged to cmd.exe /d /s /c, so batch syntax, redirection and && work; for PowerShell write powershell -NoProfile -ExecutionPolicy Bypass -File ... or -Command ..." },
				"workingDir":     { "type": "string", "description": "The folder the command starts in. Your own working directory (or below it), a registered project directory in the Tessyn tree (or below it), or a sibling worktree named '<project>-wt-*'. A relative path is resolved against your working directory." },
				"reason":         { "type": "string", "description": "One line saying why the run is needed, shown on the user's card (at most 300 characters). If the command runs a script, say what the script does." },
				"timeoutMinutes": { "type": "integer", "minimum": 1, "maximum": 240, "description": "Whole minutes the process may run, counted from the moment it starts. Default 60." }
			},
			"required": ["command", "workingDir", "reason"]
		}
		""")!;

	private static JsonNode CancelElevatedRunSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"requestId": { "type": "string", "description": "The request id (8 hex characters) or a unique prefix of at least 4 characters." }
			},
			"required": ["requestId"]
		}
		""")!;

	private static JsonNode ListElevatedRunsSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"status": { "type": "string", "enum": ["active", "all"], "description": "active (default): pending, launching and running. all: also the finished ones." }
			}
		}
		""")!;
}
