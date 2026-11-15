using System.Windows;
using System.Windows.Controls;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// A tab control whose first tabs come from XAML and the rest from <see cref="IShellSlots"/> (the contributions' <see cref="ShellSlotContribution.Title"/>
/// is the header). While nothing is contributed the tab strip is hidden, so the control looks like its single XAML tab's content.
/// </summary>
/// <remarks>Created by Claude</remarks>
public class SlotTabControl : TabControl
{
	public static readonly DependencyProperty SlotIdProperty = DependencyProperty.Register(nameof(SlotId), typeof(string), typeof(SlotTabControl), new PropertyMetadata(string.Empty));

	private bool _populated;

	public SlotTabControl()
	{
		Loaded += (_, _) => Populate();
	}

	public string SlotId
	{
		get => (string)GetValue(SlotIdProperty);
		set => SetValue(SlotIdProperty, value);
	}

	/// <summary>The registry; the running app supplies it, tests set it.</summary>
	public IShellSlots? Slots { get; set; }

	/// <summary>Adds the contributed tabs (once); public so tests can do it without a loaded window.</summary>
	public void Populate()
	{
		if (_populated)
			return;

		_populated = true;
		var slots = Slots ?? new ShellServiceProvider().Get<IShellSlots>();
		var contributions = slots?.For(SlotId) ?? [];
		foreach (var contribution in contributions)
			Items.Add(new TabItem { Header = contribution.Title ?? contribution.SlotId, Content = contribution.Create() });

		if (contributions.Count > 0)
			return;

		foreach (var tab in Items.OfType<TabItem>())
			tab.Visibility = Visibility.Collapsed;
	}
}
