using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TessynDesktop.Models;
using TessynDesktop.ViewModels;

namespace TessynDesktop.Views;

/// <summary>
/// Small rounded pill coloured by an <see cref="AttentionKind"/> (FR.23.1): the Focus badge next to the
/// "Focus" switch (FR.23.13) and, with <see cref="IsChip"/>, the kind chip on a card (FR.23.14).
/// Todo and Alert take their colours from the theme resources, so they follow theme switches.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttentionKindPill : Border
{
	public static readonly StyledProperty<AttentionKind?> KindProperty =
		AvaloniaProperty.Register<AttentionKindPill, AttentionKind?>(nameof(Kind));

	public static readonly StyledProperty<string?> TextProperty =
		AvaloniaProperty.Register<AttentionKindPill, string?>(nameof(Text));

	public static readonly StyledProperty<bool> IsChipProperty =
		AvaloniaProperty.Register<AttentionKindPill, bool>(nameof(IsChip));

	private const string ThemeForegroundKey = "SystemControlForegroundBaseHighBrush";
	private const string ThemeBackgroundKey = "SystemControlBackgroundAltHighBrush";
	private const string ThemeOutlineKey = "SystemControlForegroundBaseLowBrush";

	private static readonly IBrush CriticalBackground = Brush.Parse(Constants.Attention.BadgeCriticalBackground);
	private static readonly IBrush CriticalForeground = Brush.Parse(Constants.Attention.BadgeCriticalForeground);
	private static readonly IBrush AskForeground = Brush.Parse(Constants.Attention.BadgeAskForeground);
	private static readonly IBrush AskBackground = Brush.Parse(Constants.Attention.BadgeAskBackground);
	private static readonly IBrush DecidedBackground = Brush.Parse(Constants.Focus.ChipDecidedBackground);

	private readonly TextBlock _label = new()
	{
		HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
		VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
		FontWeight = FontWeight.SemiBold,
	};

	private readonly List<IDisposable> _resourceSubscriptions = [];

	public AttentionKindPill()
	{
		CornerRadius = new CornerRadius(8);
		MinWidth = Constants.Focus.PillMinWidth;
		Padding = new Thickness(5, 0);
		Child = _label;
		Apply();
	}

	public AttentionKind? Kind
	{
		get => GetValue(KindProperty);
		set => SetValue(KindProperty, value);
	}

	public string? Text
	{
		get => GetValue(TextProperty);
		set => SetValue(TextProperty, value);
	}

	/// <summary>Chip mode (card header): larger text and an outline on the otherwise transparent Alert kind.</summary>
	public bool IsChip
	{
		get => GetValue(IsChipProperty);
		set => SetValue(IsChipProperty, value);
	}

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);

		if (change.Property == TextProperty)
			_label.Text = Text;
		else if (change.Property == KindProperty || change.Property == IsChipProperty)
			Apply();
	}

	protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
	{
		base.OnDetachedFromVisualTree(e);
		ClearResourceSubscriptions();
	}

	protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		Apply();
	}

	private void Apply()
	{
		ClearResourceSubscriptions();

		_label.FontSize = IsChip ? Constants.Focus.ChipFontSize : Constants.Focus.BadgeFontSize;
		BorderThickness = new Thickness(0);
		BorderBrush = null;

		switch (Kind)
		{
			case AttentionKind.Critical:
				Background = CriticalBackground;
				_label.Foreground = CriticalForeground;
				break;
			case AttentionKind.Ask:
				Background = AskBackground;
				_label.Foreground = AskForeground;
				break;
			case AttentionKind.Todo:
				// Theme foreground as background, theme background as the text colour.
				BindToResource(this, BackgroundProperty, ThemeForegroundKey);
				BindToResource(_label, TextBlock.ForegroundProperty, ThemeBackgroundKey);
				break;
			case AttentionKind.Alert:
				Background = Brushes.Transparent;
				BindToResource(_label, TextBlock.ForegroundProperty, ThemeForegroundKey);
				if (IsChip)
				{
					BorderThickness = new Thickness(1);
					BindToResource(this, BorderBrushProperty, ThemeOutlineKey);
				}
				break;
			case AttentionKind.Decided:
				Background = DecidedBackground;
				_label.Foreground = Brushes.White;
				break;
			default:
				Background = Brushes.Transparent;
				BindToResource(_label, TextBlock.ForegroundProperty, ThemeForegroundKey);
				break;
		}
	}

	private void BindToResource(AvaloniaObject target, StyledProperty<IBrush?> property, string resourceKey)
	{
		var subscription = this.GetResourceObservable(resourceKey)
			.Subscribe(value => target.SetValue(property, value as IBrush));
		_resourceSubscriptions.Add(subscription);
	}

	private void ClearResourceSubscriptions()
	{
		foreach (var subscription in _resourceSubscriptions)
			subscription.Dispose();

		_resourceSubscriptions.Clear();
	}
}
