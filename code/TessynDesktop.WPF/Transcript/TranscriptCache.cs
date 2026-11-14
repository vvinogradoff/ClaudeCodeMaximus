namespace TessynDesktop.WPF.Transcript;

/// <summary>An LRU of loaded transcripts: at most <c>capacity</c> sessions' entries stay in memory (UI thread only).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TranscriptCache
{
	private readonly int _capacity;
	private readonly LinkedList<TranscriptModel> _byRecency = new();
	private readonly Dictionary<string, LinkedListNode<TranscriptModel>> _nodes = new();

	public TranscriptCache(int capacity)
	{
		_capacity = Math.Max(1, capacity);
	}

	public int Count => _nodes.Count;

	/// <summary>Node ids, most recently used first.</summary>
	public IReadOnlyList<string> NodeIds => _byRecency.Select(m => m.NodeId).ToList();

	/// <summary>Finds a transcript and marks it most recently used.</summary>
	public bool TryGet(string nodeId, out TranscriptModel model)
	{
		if (_nodes.TryGetValue(nodeId, out var node))
		{
			_byRecency.Remove(node);
			_byRecency.AddFirst(node);
			model = node.Value;
			return true;
		}

		model = null!;
		return false;
	}

	/// <summary>Adds or replaces a transcript as most recently used and evicts the least recently used beyond the capacity.</summary>
	public void Put(TranscriptModel model)
	{
		Remove(model.NodeId);
		_nodes[model.NodeId] = _byRecency.AddFirst(model);
		while (_nodes.Count > _capacity)
		{
			var last = _byRecency.Last!;
			_byRecency.RemoveLast();
			_nodes.Remove(last.Value.NodeId);
		}
	}

	public void Remove(string nodeId)
	{
		if (_nodes.Remove(nodeId, out var node))
			_byRecency.Remove(node);
	}

	public void Clear()
	{
		_nodes.Clear();
		_byRecency.Clear();
	}
}
