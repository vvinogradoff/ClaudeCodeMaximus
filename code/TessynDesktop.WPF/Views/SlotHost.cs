using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// A named, initially empty layout slot. On load it shows the views registered for <see cref="SlotId"/> in <see cref="IShellSlots"/>;
/// with none it takes no space. Contributed views inherit the host's DataContext.
/// </summary>
/// <remarks>Created by Claude</remarks>
public class SlotHost : ItemsControl
{
	public static readonly DependencyProperty SlotIdProperty = DependencyProperty.Register(nameof(SlotId), typeof(string), typeof(SlotHost), new PropertyMetadata(string.Empty));

	private bool _populated;

	public SlotHost()
	{
		Focusable = false;
		IsTabStop = false;
		Loaded += (_, _) => Populate();
	}

	public string SlotId
	{
		get => (string)GetValue(SlotIdProperty);
		set => SetValue(SlotIdProperty, value);
	}

	// A contributed element is not its own container: an ItemsControl exposes an item that is its own container to UI Automation only as an
	// "ItemsControlItem" DataItem with the element's text, without the element's own peer, so a contributed Button had no Invoke pattern and
	// could not be pressed by an assistive technology (or the E2E tests). In a ContentControl container the element keeps its own peer.
	protected override bool IsItemItsOwnContainerOverride(object item) => false;

	protected override DependencyObject GetContainerForItemOverride() => new ContentControl { Focusable = false, IsTabStop = false };

	/// <summary>The registry; the running app supplies it, tests set it.</summary>
	public IShellSlots? Slots { get; set; }

	/// <summary>Where a failing contribution is logged; the running app supplies it, tests set it.</summary>
	public ILogger? Logger { get; set; }

	/// <summary>Fills the slot (once); public so tests can do it without a loaded window.</summary>
	public void Populate()
	{
		if (_populated)
			return;

		_populated = true;
		var slots = Slots ?? new ShellServiceProvider().Get<IShellSlots>();
		if (slots == null)
			return;

		var logger = SlotContributions.ResolveLogger(Logger);
		foreach (var contribution in slots.For(SlotId))
		{
			var view = SlotContributions.TryCreate(contribution, logger);
			if (view != null)
				Items.Add(view);
		}
	}
}
