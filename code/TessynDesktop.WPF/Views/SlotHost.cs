using System.Windows;
using System.Windows.Controls;
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

	/// <summary>The registry; the running app supplies it, tests set it.</summary>
	public IShellSlots? Slots { get; set; }

	/// <summary>Fills the slot (once); public so tests can do it without a loaded window.</summary>
	public void Populate()
	{
		if (_populated)
			return;

		_populated = true;
		var slots = Slots ?? new ShellServiceProvider().Get<IShellSlots>();
		if (slots == null)
			return;

		foreach (var contribution in slots.For(SlotId))
			Items.Add(contribution.Create());
	}
}
