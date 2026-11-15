using System.Windows.Input;

namespace TessynDesktop.WPF.Services;

/// <summary>Several commands claim one key combination; <see cref="Winner"/> is the one that runs.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record GestureConflict(Key Key, ModifierKeys Modifiers, string Winner, IReadOnlyList<string> Losers);
