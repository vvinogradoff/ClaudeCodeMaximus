namespace TessynDesktop.WPF.Services;

/// <summary>Progress of a preview download.</summary>
/// <param name="BytesRead">Bytes received so far.</param>
/// <param name="TotalBytes">Size of the file; 0 when not known yet.</param>
/// <remarks>Created by Claude</remarks>
public readonly record struct PreviewProgress(long BytesRead, long TotalBytes)
{
	/// <summary>0..1; 0 while the size is unknown.</summary>
	public double Fraction => TotalBytes <= 0 ? 0 : Math.Min(1, (double)BytesRead / TotalBytes);
}
