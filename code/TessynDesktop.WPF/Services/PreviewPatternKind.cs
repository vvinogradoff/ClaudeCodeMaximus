namespace TessynDesktop.WPF.Services;

/// <summary>What a preview pattern matches.</summary>
/// <remarks>Created by Claude</remarks>
public enum PreviewPatternKind
{
	/// <summary>A local file's last extension.</summary>
	Extension,

	/// <summary>A web address, with <c>*</c> wildcards.</summary>
	Url,
}
