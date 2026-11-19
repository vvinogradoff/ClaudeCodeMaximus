namespace TessynDesktop.WPF.ViewModels;

/// <summary>A path written in the text of a Focus card was activated (FR.23.30): the session, the item and the path as written; <see cref="IsClosed"/> items give a preview no source item (FR.23.29).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record FocusPathRequest(string NodeId, string ItemId, string Path, bool IsClosed);
