using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Codex.Protocol;

namespace TessynDesktop.Codex;

/// <summary>
/// Answers every server request of the app-server (FR.33.8), so no turn hangs in <c>waitingOnApproval</c>:
/// command and file approvals are accepted and logged at Information level; a permissions request is granted for the turn; an MCP
/// elicitation is declined; anything else (for example the experimental <c>item/tool/requestUserInput</c>) gets <c>-32601</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexServerRequestResponder
{
	private static readonly ILogger _log = Log.ForContext<CodexServerRequestResponder>();

	private readonly string                _profileId;
	private readonly JsonSerializerOptions _options;
	private readonly JsonElement           _emptyObject;

	public CodexServerRequestResponder(string profileId, JsonSerializerOptions? options = null)
	{
		_profileId = profileId;
		_options   = options ?? Constants.Codex.SerializerOptions;
		using var empty = JsonDocument.Parse("{}");
		_emptyObject    = empty.RootElement.Clone();
	}

	public Task<CodexServerReply> HandleAsync(CodexServerRequest request, CancellationToken cancellationToken)
	{
		var reply = request.Method switch
		{
			Constants.Codex.Methods.CommandExecutionRequestApproval => AcceptCommand(request),
			Constants.Codex.Methods.FileChangeRequestApproval       => AcceptFileChange(request),
			Constants.Codex.Methods.PermissionsRequestApproval      => GrantPermissions(request),
			Constants.Codex.Methods.McpServerElicitationRequest     => DeclineElicitation(request),
			_                                                       => Unsupported(request),
		};
		return Task.FromResult(reply);
	}

	private CodexServerReply AcceptCommand(CodexServerRequest request)
	{
		var p = request.Params.Deserialize<CodexCommandApprovalParams>(_options);
		_log.Information("Codex ({Profile}) asked to approve a command on thread {ThreadId}; accepted: {Command} in {Cwd} ({Reason})",
			_profileId, p?.ThreadId, p?.Command, p?.Cwd, p?.Reason);
		return CodexServerReply.Success(new CodexApprovalDecisionResult { Decision = Constants.Codex.Values.DecisionAccept });
	}

	private CodexServerReply AcceptFileChange(CodexServerRequest request)
	{
		var p = request.Params.Deserialize<CodexFileChangeApprovalParams>(_options);
		_log.Information("Codex ({Profile}) asked to approve a file change on thread {ThreadId}; accepted: item {ItemId}, root {GrantRoot} ({Reason})",
			_profileId, p?.ThreadId, p?.ItemId, p?.GrantRoot, p?.Reason);
		return CodexServerReply.Success(new CodexApprovalDecisionResult { Decision = Constants.Codex.Values.DecisionAccept });
	}

	private CodexServerReply GrantPermissions(CodexServerRequest request)
	{
		var p         = request.Params.Deserialize<CodexPermissionsApprovalParams>(_options);
		var requested = p is not null && p.Permissions.ValueKind == JsonValueKind.Object ? p.Permissions : _emptyObject;
		_log.Information("Codex ({Profile}) asked for permissions on thread {ThreadId}; granted for the turn: {Permissions} ({Reason})",
			_profileId, p?.ThreadId, requested.GetRawText(), p?.Reason);
		return CodexServerReply.Success(new CodexPermissionsApprovalResult { Permissions = requested, Scope = Constants.Codex.Values.PermissionScopeTurn });
	}

	private CodexServerReply DeclineElicitation(CodexServerRequest request)
	{
		var p = request.Params.Deserialize<CodexElicitationParams>(_options);
		_log.Information("Codex ({Profile}) MCP server {Server} asked for input on thread {ThreadId}; declined: {Message}",
			_profileId, p?.ServerName, p?.ThreadId, p?.Message);
		return CodexServerReply.Success(new CodexElicitationResult { Action = Constants.Codex.Values.ElicitationDecline });
	}

	private CodexServerReply Unsupported(CodexServerRequest request)
	{
		_log.Warning("Codex ({Profile}) sent server request {Method}, which Tessyn does not support; answered -32601", _profileId, request.Method);
		return CodexServerReply.Failure(Constants.Codex.Rpc.MethodNotFound, string.Format(Constants.Codex.Texts.UnsupportedServerRequestFormat, request.Method));
	}
}
