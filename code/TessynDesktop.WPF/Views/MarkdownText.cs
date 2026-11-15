using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// <c>MarkdownText.Source</c>: fills a <see cref="StackPanel"/> with native elements built from markdown by
/// <see cref="MarkdownBlockBuilder"/> (no HTML, http(s) links only, opened through <see cref="IUrlLauncher"/>).
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class MarkdownText
{
	public static readonly DependencyProperty SourceProperty =
		DependencyProperty.RegisterAttached("Source", typeof(string), typeof(MarkdownText), new PropertyMetadata(null, OnSourceChanged));

	public static string? GetSource(DependencyObject element) => (string?)element.GetValue(SourceProperty);

	public static void SetSource(DependencyObject element, string? value) => element.SetValue(SourceProperty, value);

	private static void OnSourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
	{
		if (sender is not StackPanel panel)
			return;

		panel.Children.Clear();
		var builder = new MarkdownBlockBuilder(new UrlPolicy(), OpenLink);
		foreach (var element in builder.Build(e.NewValue as string))
			panel.Children.Add(element);
	}

	private static void OpenLink(string url) =>
		(Application.Current as App)?.Services.GetService<IUrlLauncher>()?.TryOpen(url);
}
