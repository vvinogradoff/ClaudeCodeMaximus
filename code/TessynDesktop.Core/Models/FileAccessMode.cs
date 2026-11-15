namespace TessynDesktop.Models;

/// <summary>What a remote (relay) client may read with <c>file.read</c> and <c>file.preview</c>.</summary>
/// <remarks>Created by Claude</remarks>
public enum FileAccessMode
{
	/// <summary>Only files a Focus item references, and attachments. The strictest setting.</summary>
	ReferencedOnly = 0,

	/// <summary>Additionally every file under the working directory of the session the handle belongs to. The default (decision A11).</summary>
	WorkingDirectory = 1,
}
