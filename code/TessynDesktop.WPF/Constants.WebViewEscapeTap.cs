namespace TessynDesktop.WPF;

/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>The Esc tap over a WebView2 page (FR.6.13, FR.29 decision 4): the member names it reads and its log texts.</summary>
	public static class WebViewEscapeTap
	{
		/// <summary>The WPF control keeps its implementation in a private field of this name; that object has a public <c>CoreWebView2Controller</c> property.</summary>
		public const string BaseField = "m_webview2Base";

		public const string ControllerProperty = "CoreWebView2Controller";

		/// <summary>The virtual-key code of Esc.</summary>
		public const uint EscapeKey = 0x1B;

		public const string ControllerMissingLog = "Browser: the WebView2 controller was not found ({Field}.{Property}); Esc in a page does not close the slide-out";
		public const string HookFailedLog = "Browser: hooking Esc in the WebView2 failed; Esc in a page does not close the slide-out";
	}
}
