namespace TessynDesktop.Models;

/// <summary>Result of a heap histogram or dump capture; <see cref="Message"/> explains a failure or refusal.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record InspectionOutcomeModel(bool Success, string Message);
