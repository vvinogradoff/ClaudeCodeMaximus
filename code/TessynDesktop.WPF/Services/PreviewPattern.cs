namespace TessynDesktop.WPF.Services;

/// <summary>One parsed pattern. <paramref name="Text"/> is normalized (extension: lower case, no dot); <paramref name="Specificity"/> counts the characters other than <c>*</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record PreviewPattern(PreviewPatternKind Kind, string Text, int Specificity);
