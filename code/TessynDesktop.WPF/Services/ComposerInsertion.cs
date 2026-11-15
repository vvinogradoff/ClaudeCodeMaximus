namespace TessynDesktop.WPF.Services;

/// <summary>The input text after an insertion and where the caret goes.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ComposerInsertion(string Text, int Caret);
