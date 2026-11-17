using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>The Tags tab of the left panel (W3): tag search, selectable chips (OR filter) and the sessions of the selected tags.</summary>
/// <remarks>Created by Claude</remarks>
public partial class TagsPanel : UserControl
{
	private TagSearchViewModel? _observed;

	public TagsPanel()
	{
		InitializeComponent();
		DataContextChanged += (_, e) =>
		{
			if (_observed != null)
				_observed.PropertyChanged -= OnViewModelChanged;

			_observed = e.NewValue as TagSearchViewModel;
			if (_observed != null)
				_observed.PropertyChanged += OnViewModelChanged;

			ShareTheSpace();
		};
	}

	private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(TagSearchViewModel.EmptySessionsText))
			ShareTheSpace();
	}

	/// <summary>
	/// With no tag selected the session list has nothing but its hint to show: it takes only the room of the hint and the chips get the rest of the panel (no big empty area under
	/// the chips). Once a tag is selected the list gets two thirds, as before.
	/// </summary>
	private void ShareTheSpace()
	{
		var hintOnly = _observed?.EmptySessionsText == Constants.SessionLists.TagsSelectTagsText;
		SessionsRow.Height = hintOnly ? GridLength.Auto : new GridLength(2, GridUnitType.Star);
		SessionsRow.MinHeight = hintOnly ? 0 : 80;
		Splitter.Visibility = hintOnly ? Visibility.Collapsed : Visibility.Visible;
	}
}