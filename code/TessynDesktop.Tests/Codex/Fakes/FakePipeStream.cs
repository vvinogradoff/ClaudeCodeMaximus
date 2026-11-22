using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace TessynDesktop.Tests.Codex.Fakes;

/// <summary>
/// A one-way in-memory pipe: what is written comes out of the reads, in order; after <see cref="CompleteWriting"/> reads drain and then
/// return 0 (end of stream), and writes throw <see cref="IOException"/>, as on a closed process pipe.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FakePipeStream : Stream
{
	private readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
	private byte[]? _current;
	private int     _offset;

	public override bool CanRead  => true;
	public override bool CanSeek  => false;
	public override bool CanWrite => true;
	public override long Length   => throw new NotSupportedException();

	public override long Position
	{
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	public bool IsWritingCompleted { get; private set; }

	public void CompleteWriting()
	{
		IsWritingCompleted = true;
		_chunks.Writer.TryComplete();
	}

	public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

	public override void Write(ReadOnlySpan<byte> buffer)
	{
		if (buffer.IsEmpty)
			return;
		if (!_chunks.Writer.TryWrite(buffer.ToArray()))
			throw new IOException("The pipe is closed.");
	}

	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
	{
		Write(buffer.Span);
		return ValueTask.CompletedTask;
	}

	public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
	{
		Write(buffer.AsSpan(offset, count));
		return Task.CompletedTask;
	}

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
	{
		while (_current is null || _offset >= _current.Length)
		{
			if (!await _chunks.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
				return 0;
			if (_chunks.Reader.TryRead(out _current))
				_offset = 0;
		}

		var count = Math.Min(buffer.Length, _current.Length - _offset);
		_current.AsMemory(_offset, count).CopyTo(buffer);
		_offset += count;
		return count;
	}

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
		ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

	public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

	public override void Flush() { }

	public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

	public override void SetLength(long value) => throw new NotSupportedException();

	protected override void Dispose(bool disposing)
	{
		if (disposing)
			CompleteWriting();
		base.Dispose(disposing);
	}
}
