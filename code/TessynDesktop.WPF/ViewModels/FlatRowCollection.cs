using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The flattened, visible rows of the tree. <see cref="Apply"/> turns the old list into the new one with the fewest
/// notifications: a common prefix and suffix stay, the middle is removed and inserted. WPF collection views reject range
/// notifications, so a large change is applied as one Reset instead of many single events.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FlatRowCollection : ObservableCollection<TreeNodeViewModel>
{
	private const int SingleEventLimit = 400;

	public void Apply(IReadOnlyList<TreeNodeViewModel> target)
	{
		var prefix = 0;
		var maxPrefix = Math.Min(Count, target.Count);
		while (prefix < maxPrefix && ReferenceEquals(this[prefix], target[prefix]))
			prefix++;

		var suffix = 0;
		var maxSuffix = Math.Min(Count, target.Count) - prefix;
		while (suffix < maxSuffix && ReferenceEquals(this[Count - 1 - suffix], target[target.Count - 1 - suffix]))
			suffix++;

		var removeCount = Count - prefix - suffix;
		var addCount = target.Count - prefix - suffix;
		if (removeCount == 0 && addCount == 0)
			return;

		if (removeCount + addCount <= SingleEventLimit)
		{
			for (var i = 0; i < removeCount; i++)
				RemoveAt(prefix);
			for (var i = 0; i < addCount; i++)
				Insert(prefix + i, target[prefix + i]);
			return;
		}

		for (var i = 0; i < removeCount; i++)
			Items.RemoveAt(prefix);
		for (var i = 0; i < addCount; i++)
			Items.Insert(prefix + i, target[prefix + i]);
		OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
		OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
		OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
	}
}
