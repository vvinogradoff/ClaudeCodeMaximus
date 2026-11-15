using System.Collections.ObjectModel;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>Brings an observable collection to a wanted order by removing, inserting and moving only what differs, so the items that stay keep their visuals.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ListDiff
{
	public void Apply<T>(ObservableCollection<T> target, IReadOnlyList<T> wanted) where T : class
	{
		var keep = new HashSet<T>(wanted, ReferenceEqualityComparer.Instance as IEqualityComparer<T>);
		for (var i = target.Count - 1; i >= 0; i--)
		{
			if (!keep.Contains(target[i]))
				target.RemoveAt(i);
		}

		for (var i = 0; i < wanted.Count; i++)
		{
			if (i < target.Count && ReferenceEquals(target[i], wanted[i]))
				continue;

			var at = IndexOf(target, wanted[i], i + 1);
			if (at >= 0)
				target.Move(at, i);
			else
				target.Insert(i, wanted[i]);
		}
	}

	private static int IndexOf<T>(ObservableCollection<T> target, T item, int from) where T : class
	{
		for (var i = from; i < target.Count; i++)
		{
			if (ReferenceEquals(target[i], item))
				return i;
		}

		return -1;
	}
}
