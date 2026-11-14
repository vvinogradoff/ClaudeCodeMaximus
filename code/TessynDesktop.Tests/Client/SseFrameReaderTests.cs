using System.Text;
using TessynDesktop.Client;

namespace TessynDesktop.Tests.Client;

/// <summary><see cref="SseFrameReader"/>: the text/event-stream parsing rules the client relies on.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SseFrameReaderTests
{
	/// <summary>A stream that hands out one byte per read, to exercise partial reads.</summary>
	private sealed class OneByteStream(byte[] data) : Stream
	{
		private int _pos;

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => data.Length;
		public override long Position { get => _pos; set => throw new NotSupportedException(); }
		public override void Flush() { }
		public override int Read(byte[] buffer, int offset, int count)
		{
			if (_pos >= data.Length || count == 0)
				return 0;
			buffer[offset] = data[_pos++];
			return 1;
		}
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}

	private static async Task<List<SseFrame>> ReadAllAsync(TextReader reader, Action? onActivity = null)
	{
		var frames = new List<SseFrame>();
		await foreach (var frame in SseFrameReader.ReadAsync(reader, onActivity ?? (() => { }), CancellationToken.None))
			frames.Add(frame);
		return frames;
	}

	[Fact]
	public async Task A_frame_has_its_id_and_data()
	{
		var frames = await ReadAllAsync(new StringReader("id: 42\ndata: {\"a\":1}\n\n"));

		var frame = Assert.Single(frames);
		Assert.Equal(42, frame.Id);
		Assert.Equal("{\"a\":1}", frame.Data);
	}

	[Fact]
	public async Task Multi_line_data_is_joined_with_newlines()
	{
		var frames = await ReadAllAsync(new StringReader("data: one\ndata: two\ndata:three\n\n"));

		Assert.Equal("one\ntwo\nthree", Assert.Single(frames).Data);
	}

	[Fact]
	public async Task Crlf_line_endings_are_understood()
	{
		var frames = await ReadAllAsync(new StringReader("id: 7\r\ndata: x\r\n\r\nid: 8\r\ndata: y\r\n\r\n"));

		Assert.Equal([(7L, "x"), (8L, "y")], frames.Select(f => (f.Id, f.Data)));
	}

	[Fact]
	public async Task Comments_are_skipped_but_count_as_activity()
	{
		var activity = 0;

		var frames = await ReadAllAsync(new StringReader(": connected\n\n: ping\ndata: x\n\n"), () => activity++);

		Assert.Equal("x", Assert.Single(frames).Data);
		Assert.True(activity >= 4);
	}

	[Fact]
	public async Task The_id_does_not_carry_over_to_the_next_frame()
	{
		var frames = await ReadAllAsync(new StringReader("id: 5\ndata: a\n\ndata: b\n\n"));

		Assert.Equal([5L, 0L], frames.Select(f => f.Id));
	}

	[Fact]
	public async Task A_frame_without_data_or_without_its_blank_line_is_not_delivered()
	{
		var frames = await ReadAllAsync(new StringReader("id: 1\n\nid: 2\ndata: unfinished"));

		Assert.Empty(frames);
	}

	[Fact]
	public async Task Unknown_fields_and_a_non_numeric_id_are_ignored()
	{
		var frames = await ReadAllAsync(new StringReader("event: x\nretry: 5\nid: abc\ndata: ok\n\n"));

		var frame = Assert.Single(frames);
		Assert.Equal(0, frame.Id);
		Assert.Equal("ok", frame.Data);
	}

	[Fact]
	public async Task Frames_survive_being_read_one_byte_at_a_time()
	{
		var text = ": hi\r\n\r\nid: 12\r\ndata: {\"m\":\"é\"}\r\n\r\nid: 13\ndata: z\n\n";
		using var reader = new StreamReader(new OneByteStream(Encoding.UTF8.GetBytes(text)), Encoding.UTF8);

		var frames = await ReadAllAsync(reader);

		Assert.Equal([(12L, "{\"m\":\"é\"}"), (13L, "z")], frames.Select(f => (f.Id, f.Data)));
	}
}
