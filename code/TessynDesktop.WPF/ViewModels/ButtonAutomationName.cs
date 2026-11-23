namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The accessible name of a button built from data (a profile id such as <c>codex_1</c>, an email): WPF's button automation peer treats an underscore in
/// <c>AutomationProperties.Name</c> as an access-key marker and drops it (<c>codex_1</c> is read as <c>codex1</c>), so every underscore is doubled, which
/// the peer turns back into one.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class ButtonAutomationName
{
	public static string Escape(string name) => name.Replace("_", "__", StringComparison.Ordinal);
}
