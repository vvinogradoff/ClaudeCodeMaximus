using System.Windows.Controls;

namespace TessynDesktop.WPF.Views.Settings;

/// <summary>The Telegram tab (DataContext: <c>TelegramSectionViewModel</c>). The token box is a <c>PasswordBox</c> bound through <see cref="PasswordBinder"/>: it never shows a stored token.</summary>
/// <remarks>Created by Claude</remarks>
public partial class TelegramSettingsView : UserControl
{
	public TelegramSettingsView()
	{
		InitializeComponent();
	}
}
