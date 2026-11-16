using EnclosureHub.Camera;

namespace EnclosureHub.Tests;

public class MjpegFrameSplitterTests
{
    [Fact]
    public void Splits_frames_across_random_chunk_boundaries()
    {
        var random = new Random(1);
        var frames = Enumerable.Range(0, 200)
            .Select(_ => MakeFrame(random, random.Next(10, 50_000)))
            .ToList();

        // Stray 0xFF bytes between frames must not confuse the parser
        var stream = new List<byte>();
        for (int i = 0; i < frames.Count; i++)
        {
            if (i % 7 == 0)
                stream.Add(0xFF);
            stream.AddRange(frames[i]);
        }

        var received = Feed(new MjpegFrameSplitter(initialCapacity: 1024), stream.ToArray(), random, maxChunk: 5000);

        Assert.Equal(frames.Count, received.Count);
        for (int i = 0; i < frames.Count; i++)
            Assert.Equal(frames[i], received[i]);
    }

    [Fact]
    public void Drops_oversized_frame_and_recovers()
    {
        var random = new Random(2);
        var small1 = MakeFrame(random, 100);
        var huge = MakeFrame(random, 10_000);
        var small2 = MakeFrame(random, 100);

        var splitter = new MjpegFrameSplitter(initialCapacity: 1024, maxFrameSize: 4096);
        var received = Feed(splitter, [.. small1, .. huge, .. small2], random, maxChunk: 700);

        Assert.Equal(2, received.Count);
        Assert.Equal(small1, received[0]);
        Assert.Equal(small2, received[1]);
        Assert.Equal(1, splitter.OverflowCount);
    }

    [Fact]
    public void Handles_marker_split_between_reads()
    {
        var frame = new byte[] { 0xFF, 0xD8, 0x01, 0x02, 0xFF, 0x00, 0x03, 0xFF, 0xD9 };
        var splitter = new MjpegFrameSplitter(initialCapacity: 16);
        var received = new List<byte[]>();

        foreach (var b in frame)
            splitter.Push([b], received.Add);

        Assert.Single(received);
        Assert.Equal(frame, received[0]);
    }

    private static List<byte[]> Feed(MjpegFrameSplitter splitter, byte[] data, Random random, int maxChunk)
    {
        var received = new List<byte[]>();
        int position = 0;
        while (position < data.Length)
        {
            int length = Math.Min(random.Next(1, maxChunk), data.Length - position);
            splitter.Push(data.AsSpan(position, length), received.Add);
            position += length;
        }
        return received;
    }

    /// <summary>Fake JPEG: SOI, random body with 0xFF byte-stuffed as FF 00, EOI.</summary>
    private static byte[] MakeFrame(Random random, int bodyLength)
    {
        var frame = new List<byte> { 0xFF, 0xD8 };
        for (int i = 0; i < bodyLength; i++)
        {
            byte b = (byte)random.Next(256);
            frame.Add(b);
            if (b == 0xFF)
                frame.Add(0x00);
        }
        frame.Add(0xFF);
        frame.Add(0xD9);
        return frame.ToArray();
    }
}
