namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>A <c>turn.send</c> as the fake client saw it.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record FakeTurnSend(string NodeId, string Text, string? ModelId, string? Effort, string? ProfileId, string? ClientRequestId, bool NewBranch = false, bool AutoCompact = false);
