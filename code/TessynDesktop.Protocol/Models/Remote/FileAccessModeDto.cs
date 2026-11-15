namespace TessynDesktop.Models.Remote;

/// <summary>What a remote client may read with <c>file.read</c> (wire form of the daemon's setting).</summary>
/// <remarks>Created by Claude</remarks>
public enum FileAccessModeDto
{
	ReferencedOnly = 0,
	WorkingDirectory = 1,
}
