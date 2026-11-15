namespace TessynDesktop.WPF.Services;

/// <summary>A remote image fetched by the host: its bytes and the (checked) content type.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record FetchedMedia(byte[] Bytes, string ContentType);
