namespace TessynDesktop.Models.Remote;

/// <summary>One node of <c>tree.nodeInfo</c>. Loopback only: it carries local paths.</summary>
/// <param name="NodeId">The node id asked for.</param>
/// <param name="Kind">One of <see cref="RemoteProtocol.TreeMutation.NodeKinds"/>.</param>
/// <param name="Path">The tree path, e.g. <c>Group / Session</c> below the directory; empty for a directory.</param>
/// <param name="WorkingDirectory">The folder on disk: the directory's path, or the working directory of a group or session.</param>
/// <param name="GitOrigin">The git remote origin of the directory that owns the node; omitted when it has none.</param>
/// <param name="Label">The display label of the owning directory.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TreeNodeInfoItem(string NodeId, string Kind, string Path, string WorkingDirectory, string? GitOrigin, string Label);
