namespace TessynDesktop.Models;

/// <summary>Outcome of a <c>diag_*</c> MCP tool: text for the caller, flagged when it is an error or refusal.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record DiagnosticToolResultModel(bool IsError, string Text);
