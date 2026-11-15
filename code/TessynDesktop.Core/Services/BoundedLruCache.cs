using System;
using System.Collections.Generic;

namespace TessynDesktop.Services;

/// <summary>
/// Least-recently-used cache whose size is enforced by an explicit <see cref="Trim"/>: entries are never dropped behind
/// the caller's back, and an entry the caller declares pinned (running, holding unsent input...) is never chosen.
/// Not thread-safe; meant for a single (UI) thread.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class BoundedLruCache<TKey, TValue> where TKey : notnull
{
	private readonly int _capacity;
	private readonly Func<TValue, bool> _isPinned;
	private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _map;
	private readonly LinkedList<KeyValuePair<TKey, TValue>> _order = new(); // first = most recently used

	public BoundedLruCache(int capacity, Func<TValue, bool>? isPinned = null, IEqualityComparer<TKey>? comparer = null)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
		_capacity = capacity;
		_isPinned = isPinned ?? (_ => false);
		_map = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(comparer);
	}

	public int Count => _map.Count;

	/// <summary>Looks the key up and, on a hit, marks it most recently used.</summary>
	public bool TryGetValue(TKey key, out TValue value)
	{
		if (_map.TryGetValue(key, out var node))
		{
			_order.Remove(node);
			_order.AddFirst(node);
			value = node.Value.Value;
			return true;
		}

		value = default!;
		return false;
	}

	/// <summary>Adds or replaces the entry and marks it most recently used. Does not evict; call <see cref="Trim"/>.</summary>
	public void Set(TKey key, TValue value)
	{
		if (_map.TryGetValue(key, out var existing))
			_order.Remove(existing);

		var node = new LinkedListNode<KeyValuePair<TKey, TValue>>(new KeyValuePair<TKey, TValue>(key, value));
		_order.AddFirst(node);
		_map[key] = node;
	}

	/// <summary>Moves an entry to a new key (e.g. a session's key changed), keeping its recency. False when there is no entry under <paramref name="oldKey"/>.</summary>
	public bool Rekey(TKey oldKey, TKey newKey)
	{
		if (!_map.Remove(oldKey, out var node))
			return false;

		if (_map.Remove(newKey, out var replaced))
			_order.Remove(replaced);

		node.Value = new KeyValuePair<TKey, TValue>(newKey, node.Value.Value);
		_map[newKey] = node;
		return true;
	}

	/// <summary>The cached values, most recently used first; does not change the recency.</summary>
	public IReadOnlyList<TValue> Snapshot() => _order.Select(pair => pair.Value).ToList();

	/// <summary>
	/// Evicts least-recently-used entries until at most <c>capacity</c> remain. <paramref name="keep"/> and pinned entries are
	/// skipped, so the cache can stay above capacity while many entries are pinned. Returns the evicted values for the caller to dispose.
	/// </summary>
	public IReadOnlyList<TValue> Trim(TKey? keep = default)
	{
		var evicted = new List<TValue>();
		var node = _order.Last;
		while (node != null && _map.Count > _capacity)
		{
			var previous = node.Previous;
			var isKept = keep != null && _map.Comparer.Equals(node.Value.Key, keep);
			if (!isKept && !_isPinned(node.Value.Value))
			{
				_order.Remove(node);
				_map.Remove(node.Value.Key);
				evicted.Add(node.Value.Value);
			}

			node = previous;
		}

		return evicted;
	}
}
