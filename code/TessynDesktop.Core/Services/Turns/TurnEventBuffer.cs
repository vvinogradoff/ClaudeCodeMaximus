using System.Collections.Generic;
using System.Text;
using TessynDesktop.Models.Remote;
using TessynDesktop.Models.Turns;

namespace TessynDesktop.Services;

/// <summary>
/// The wire notifications of one turn that wait for the next batch flush (P1.I). Consecutive deltas of one kind merge into one item;
/// when the buffer outgrows its limits the oldest deltas are dropped (their sequence numbers stay used, so the client sees the gap),
/// while tool, status and usage items are never dropped. Not thread-safe: the owner locks.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class TurnEventBuffer
{
	private readonly int _maxItems;
	private readonly int _maxDeltaChars;
	private readonly string _nodeId;
	private readonly string _turnId;
	private readonly List<Item> _items = [];
	private long _seq;
	private int _deltaChars;

	public TurnEventBuffer(string nodeId, string turnId, int maxItems, int maxDeltaChars)
	{
		_nodeId        = nodeId;
		_turnId        = turnId;
		_maxItems      = maxItems;
		_maxDeltaChars = maxDeltaChars;
	}

	public bool IsEmpty => _items.Count == 0;

	/// <summary>Deltas dropped over the buffer's life.</summary>
	public int Dropped { get; private set; }

	public void AddDelta(TurnDeltaKind kind, string text)
	{
		_deltaChars += text.Length;

		if (_items.Count > 0 && _items[^1].Delta is { } last && last.Kind == kind)
			last.Text.Append(text);
		else
			_items.Add(new Item(null, new DeltaItem(++_seq, kind, new StringBuilder(text))));

		Trim();
	}

	/// <summary>Adds a tool, status or usage notification; it keeps its place between the deltas.</summary>
	public void AddOther(string method, object payload) =>
		_items.Add(new Item(new OtherItem(method, payload), null));

	/// <summary>Removes and returns everything buffered, in order, as notifications ready to publish.</summary>
	public List<(string Method, object Payload)> Drain()
	{
		var batch = new List<(string Method, object Payload)>(_items.Count);
		foreach (var item in _items)
		{
			if (item.Other != null)
				batch.Add((item.Other.Method, item.Other.Payload));
			else if (item.Delta != null)
				batch.Add((RemoteProtocol.TurnStream.DeltaEvent,
					new TurnDeltaPayload(_nodeId, _turnId, item.Delta.Seq, item.Delta.Kind, item.Delta.Text.ToString())));
		}

		_items.Clear();
		_deltaChars = 0;
		return batch;
	}

	/// <summary>Drops the oldest deltas while the buffer is over a limit, but always keeps the newest one.</summary>
	private void Trim()
	{
		while ((_items.Count > _maxItems || _deltaChars > _maxDeltaChars) && TryDropOldestDelta())
		{
		}
	}

	private bool TryDropOldestDelta()
	{
		var newestDelta = _items.FindLastIndex(i => i.Delta != null);
		for (var i = 0; i < newestDelta; i++)
		{
			if (_items[i].Delta is not { } delta)
				continue;

			_deltaChars -= delta.Text.Length;
			_items.RemoveAt(i);
			Dropped++;
			return true;
		}

		return false;
	}

	private sealed record DeltaItem(long Seq, TurnDeltaKind Kind, StringBuilder Text);

	private sealed record OtherItem(string Method, object Payload);

	private sealed record Item(OtherItem? Other, DeltaItem? Delta);
}
