using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using TessynDesktop.Codex.Protocol;

namespace TessynDesktop.Tests.Codex;

/// <summary>
/// FR.33.19: every member of every hand-written protocol DTO exists in the schema generated from the pinned Codex
/// (<c>code/TessynDesktop.Core/Codex/Schema/0.171.0/</c>, from <c>codex app-server generate-json-schema</c>). Nested DTOs are checked
/// against the schema of the member that holds them; a union is checked against the members of all its variants. A Codex upgrade starts
/// by regenerating the schema files and running this test.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexProtocolSchemaTests
{
	private const string SchemaVersion = "0.171.0";

	/// <summary>Each top-level DTO and the schema files (relative to the version folder) it stands for.</summary>
	private static readonly (Type Dto, string[] Files)[] Roots =
	[
		(typeof(CodexInitializeParams), [@"v1\InitializeParams.json"]),
		(typeof(CodexInitializeResult), [@"v1\InitializeResponse.json"]),
		(typeof(CodexGetAccountParams), [@"v2\GetAccountParams.json"]),
		(typeof(CodexGetAccountResult), [@"v2\GetAccountResponse.json"]),
		(typeof(CodexLoginStartParams), [@"v2\LoginAccountParams.json"]),
		(typeof(CodexLoginStartResult), [@"v2\LoginAccountResponse.json"]),
		(typeof(CodexLoginCancelParams), [@"v2\CancelLoginAccountParams.json"]),
		(typeof(CodexLoginCancelResult), [@"v2\CancelLoginAccountResponse.json"]),
		(typeof(CodexLoginCompletedNotification), [@"v2\AccountLoginCompletedNotification.json"]),
		(typeof(CodexAccountUpdatedNotification), [@"v2\AccountUpdatedNotification.json"]),
		(typeof(CodexRateLimitsResult), [@"v2\GetAccountRateLimitsResponse.json"]),
		(typeof(CodexRateLimitsUpdatedNotification), [@"v2\AccountRateLimitsUpdatedNotification.json"]),
		(typeof(CodexModelListParams), [@"v2\ModelListParams.json"]),
		(typeof(CodexModelListResult), [@"v2\ModelListResponse.json"]),
		(typeof(CodexThreadStartParams), [@"v2\ThreadStartParams.json"]),
		(typeof(CodexThreadStartResult), [@"v2\ThreadStartResponse.json"]),
		(typeof(CodexThreadResumeParams), [@"v2\ThreadResumeParams.json"]),
		(typeof(CodexThreadResumeResult), [@"v2\ThreadResumeResponse.json"]),
		(typeof(CodexThreadSetNameParams), [@"v2\ThreadSetNameParams.json"]),
		(typeof(CodexThreadNameUpdatedNotification), [@"v2\ThreadNameUpdatedNotification.json"]),
		(typeof(CodexThreadUnsubscribeParams), [@"v2\ThreadUnsubscribeParams.json"]),
		(typeof(CodexThreadUnsubscribeResult), [@"v2\ThreadUnsubscribeResponse.json"]),
		(typeof(CodexThreadCompactStartParams), [@"v2\ThreadCompactStartParams.json"]),
		(typeof(CodexThreadStartedNotification), [@"v2\ThreadStartedNotification.json"]),
		(typeof(CodexEmptyResult), [@"v2\LogoutAccountResponse.json", @"v2\ThreadSetNameResponse.json", @"v2\ThreadCompactStartResponse.json", @"v2\TurnInterruptResponse.json"]),
		(typeof(CodexTurnStartParams), [@"v2\TurnStartParams.json"]),
		(typeof(CodexTurnStartResult), [@"v2\TurnStartResponse.json"]),
		(typeof(CodexTurnInterruptParams), [@"v2\TurnInterruptParams.json"]),
		(typeof(CodexTurnNotification), [@"v2\TurnStartedNotification.json", @"v2\TurnCompletedNotification.json"]),
		(typeof(CodexItemStartedNotification), [@"v2\ItemStartedNotification.json"]),
		(typeof(CodexItemCompletedNotification), [@"v2\ItemCompletedNotification.json"]),
		(typeof(CodexAgentMessageDeltaNotification), [@"v2\AgentMessageDeltaNotification.json"]),
		(typeof(CodexReasoningSummaryTextDeltaNotification), [@"v2\ReasoningSummaryTextDeltaNotification.json"]),
		(typeof(CodexTurnPlanUpdatedNotification), [@"v2\TurnPlanUpdatedNotification.json"]),
		(typeof(CodexTokenUsageUpdatedNotification), [@"v2\ThreadTokenUsageUpdatedNotification.json"]),
		(typeof(CodexErrorNotification), [@"v2\ErrorNotification.json"]),
		(typeof(CodexMcpServerStartupStatusNotification), [@"v2\McpServerStatusUpdatedNotification.json"]),
		(typeof(CodexCommandApprovalParams), ["CommandExecutionRequestApprovalParams.json"]),
		(typeof(CodexFileChangeApprovalParams), ["FileChangeRequestApprovalParams.json"]),
		(typeof(CodexApprovalDecisionResult), ["CommandExecutionRequestApprovalResponse.json", "FileChangeRequestApprovalResponse.json"]),
		(typeof(CodexPermissionsApprovalParams), ["PermissionsRequestApprovalParams.json"]),
		(typeof(CodexPermissionsApprovalResult), ["PermissionsRequestApprovalResponse.json"]),
		(typeof(CodexElicitationParams), ["McpServerElicitationRequestParams.json"]),
		(typeof(CodexElicitationResult), ["McpServerElicitationRequestResponse.json"]),
	];

	[Fact]
	public void EveryDtoMember_ExistsInTheSchema()
	{
		var problems = new List<string>();
		var visited  = new HashSet<Type>();

		foreach (var (dto, files) in Roots)
		{
			foreach (var file in files)
			{
				using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(SchemaFolder(), file)));
				var root = document.RootElement;
				CheckObject(dto, [root], root, $"{file}", problems, visited, depth: 0);
			}
		}

		Assert.True(problems.Count == 0, "DTO members missing from the Codex " + SchemaVersion + " schema:\n" + string.Join("\n", problems));
	}

	[Fact]
	public void EveryProtocolDto_IsReachedFromARoot()
	{
		var problems = new List<string>();
		var visited  = new HashSet<Type>();
		foreach (var (dto, files) in Roots)
		{
			using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(SchemaFolder(), files[0])));
			CheckObject(dto, [document.RootElement], document.RootElement, files[0], problems, visited, depth: 0);
		}

		var dtos = typeof(CodexThreadItem).Assembly.GetTypes()
			.Where(t => t.Namespace == typeof(CodexThreadItem).Namespace && t.IsClass && t.IsPublic)
			.ToList();
		var unchecked_ = dtos.Where(t => !visited.Contains(t)).Select(t => t.Name).ToList();

		Assert.True(unchecked_.Count == 0, "Protocol DTOs not checked against the schema: " + string.Join(", ", unchecked_));
	}

	[Fact]
	public void EveryStoredSchemaFile_IsUsedByADto()
	{
		var folder = SchemaFolder();
		var stored = Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(folder, f)).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var used   = Roots.SelectMany(r => r.Files).ToHashSet(StringComparer.OrdinalIgnoreCase);

		Assert.Empty(stored.Except(used));
		Assert.Empty(used.Except(stored));
	}

	[Fact]
	public void TheSchemaCheck_CatchesAMemberThatDoesNotExist()
	{
		var problems = new List<string>();
		using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(SchemaFolder(), @"v2\TurnInterruptParams.json")));
		CheckObject(typeof(WrongTurnInterruptParams), [document.RootElement], document.RootElement, "test", problems, [], depth: 0);

		Assert.Equal(["test: WrongTurnInterruptParams.turnIdentifier is not in the schema"], problems);
	}

	private void CheckObject(Type dto, IReadOnlyList<JsonElement> schemas, JsonElement root, string path, List<string> problems, HashSet<Type> visited, int depth)
	{
		visited.Add(dto);
		if (depth > 8)
			return;

		var members = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal);
		foreach (var schema in schemas)
			CollectProperties(schema, root, members);

		foreach (var property in dto.GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			if (property.GetCustomAttribute<JsonIgnoreAttribute>() is not null || property.GetCustomAttribute<JsonExtensionDataAttribute>() is not null)
				continue;

			var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
			if (!members.TryGetValue(name, out var memberSchemas))
			{
				problems.Add($"{path}: {dto.Name}.{name} is not in the schema");
				continue;
			}

			var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
			if (IsDto(type))
			{
				CheckObject(type, memberSchemas, root, $"{path}/{name}", problems, visited, depth + 1);
				continue;
			}

			if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) && IsDto(type.GetGenericArguments()[0]))
			{
				var items = memberSchemas.SelectMany(s => Expand(s, root)).Where(s => s.TryGetProperty("items", out _)).Select(s => s.GetProperty("items")).ToList();
				CheckObject(type.GetGenericArguments()[0], items, root, $"{path}/{name}[]", problems, visited, depth + 1);
				continue;
			}

			if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>) && IsDto(type.GetGenericArguments()[1]))
			{
				var values = memberSchemas.SelectMany(s => Expand(s, root))
					.Where(s => s.TryGetProperty("additionalProperties", out var a) && a.ValueKind == JsonValueKind.Object)
					.Select(s => s.GetProperty("additionalProperties")).ToList();
				CheckObject(type.GetGenericArguments()[1], values, root, $"{path}/{name}{{}}", problems, visited, depth + 1);
			}
		}
	}

	/// <summary>The properties of a schema node and of every node it reaches through <c>$ref</c>, <c>allOf</c>, <c>anyOf</c> and <c>oneOf</c>.</summary>
	private void CollectProperties(JsonElement schema, JsonElement root, Dictionary<string, List<JsonElement>> members)
	{
		foreach (var node in Expand(schema, root))
		{
			if (!node.TryGetProperty("properties", out var properties))
				continue;
			foreach (var property in properties.EnumerateObject())
			{
				if (!members.TryGetValue(property.Name, out var list))
					members[property.Name] = list = [];
				list.Add(property.Value);
			}
		}
	}

	private IEnumerable<JsonElement> Expand(JsonElement schema, JsonElement root, int depth = 0)
	{
		if (schema.ValueKind != JsonValueKind.Object || depth > 16)
			yield break;

		yield return schema;

		if (schema.TryGetProperty("$ref", out var reference))
		{
			var name = reference.GetString()!.Split('/').Last();
			if (root.TryGetProperty("definitions", out var definitions) && definitions.TryGetProperty(name, out var target))
				foreach (var node in Expand(target, root, depth + 1))
					yield return node;
		}

		foreach (var combinator in new[] { "allOf", "anyOf", "oneOf" })
		{
			if (!schema.TryGetProperty(combinator, out var branches))
				continue;
			foreach (var branch in branches.EnumerateArray())
				foreach (var node in Expand(branch, root, depth + 1))
					yield return node;
		}
	}

	private bool IsDto(Type type) => type.Namespace == typeof(CodexThreadItem).Namespace && type.IsClass;

	private string SchemaFolder()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "code", "TessynDesktop.sln")))
			dir = dir.Parent;

		Assert.NotNull(dir);
		return Path.Combine(dir.FullName, "code", "TessynDesktop.Core", "Codex", "Schema", SchemaVersion);
	}

	/// <summary>A DTO with a wrong member, to prove the check fails when it should.</summary>
	public sealed class WrongTurnInterruptParams
	{
		public string ThreadId { get; set; } = "";
		public string TurnIdentifier { get; set; } = "";
	}
}
