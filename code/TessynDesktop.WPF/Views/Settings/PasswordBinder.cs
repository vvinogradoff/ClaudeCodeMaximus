using System.Windows;
using System.Windows.Controls;

namespace TessynDesktop.WPF.Views.Settings;

/// <summary>
/// Lets a <see cref="PasswordBox"/> take part in a binding (<c>PasswordBinder.Secret</c>), one way into the view model and back to empty when the view model clears it. Used for the
/// write-only secrets (Telegram token, link token, MCP header value, OAuth client secret): the box never shows a stored value, only what is being typed.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class PasswordBinder
{
	public static readonly DependencyProperty SecretProperty = DependencyProperty.RegisterAttached(
		"Secret", typeof(string), typeof(PasswordBinder),
		new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSecretChanged));

	public static string? GetSecret(DependencyObject element) => (string?)element.GetValue(SecretProperty);

	public static void SetSecret(DependencyObject element, string? value) => element.SetValue(SecretProperty, value);

	private static void OnSecretChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is not PasswordBox box)
			return;

		box.PasswordChanged -= OnPasswordChanged;
		var text = e.NewValue as string ?? string.Empty;
		if (box.Password != text)
			box.Password = text;

		box.PasswordChanged += OnPasswordChanged;
	}

	private static void OnPasswordChanged(object sender, RoutedEventArgs e)
	{
		if (sender is PasswordBox box)
			SetSecret(box, box.Password);
	}
}
