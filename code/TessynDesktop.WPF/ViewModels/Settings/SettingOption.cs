namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>A value of a drop-down with the text shown for it.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SettingOption<T>(T Value, string Label);
