namespace TessynDesktop.WPF.Services;

/// <summary>What the host does with a request a WebView2 page makes.</summary>
/// <remarks>Created by Claude</remarks>
public enum RequestDecision
{
	/// <summary>Let WebView2 handle it (a page of this app, or a virtual host mapped to a local folder).</summary>
	Allow,

	/// <summary>Answer it from the media proxy (<see cref="TranscriptMediaBroker"/>).</summary>
	ServeMedia,

	/// <summary>Refuse it (403): the page may not reach anything else.</summary>
	Block,
}
