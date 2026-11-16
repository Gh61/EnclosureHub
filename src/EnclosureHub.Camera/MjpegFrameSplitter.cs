namespace EnclosureHub.Camera;

/// <summary>
/// Splits a raw MJPEG byte stream (concatenated JPEG images) into individual frames.
/// Looks for the JPEG markers SOI (FF D8) and EOI (FF D9). Inside JPEG entropy-coded
/// data every 0xFF is stuffed as FF 00, so FF D9 only appears at the end of a frame.
/// Not thread-safe; feed it from a single reader.
/// </summary>
public sealed class MjpegFrameSplitter
{
    private readonly int _maxFrameSize;
    private byte[] _buffer;
    private int _length;        // valid bytes in _buffer
    private int _scanFrom;      // first index not scanned yet
    private int _frameStart = -1; // index of FF D8 of the current frame, -1 = outside a frame

    public MjpegFrameSplitter(int initialCapacity = 1024 * 1024, int maxFrameSize = 32 * 1024 * 1024)
    {
        if (initialCapacity < 2)
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        if (maxFrameSize < initialCapacity)
            throw new ArgumentOutOfRangeException(nameof(maxFrameSize));

        _buffer = new byte[initialCapacity];
        _maxFrameSize = maxFrameSize;
    }

    /// <summary>Number of times a frame exceeded the maximum size and was dropped.</summary>
    public int OverflowCount { get; private set; }

    public void Push(ReadOnlySpan<byte> data, Action<byte[]> onFrame)
    {
        while (!data.IsEmpty)
        {
            int free = EnsureFree(data.Length);
            if (free == 0)
            {
                // Current frame is larger than the maximum – drop it and resynchronise on the next SOI.
                OverflowCount++;
                Reset();
                free = _buffer.Length;
            }

            int take = Math.Min(free, data.Length);
            data[..take].CopyTo(_buffer.AsSpan(_length));
            _length += take;
            data = data[take..];

            Scan(onFrame);
        }
    }

    public void Reset()
    {
        _length = 0;
        _scanFrom = 0;
        _frameStart = -1;
    }

    private void Scan(Action<byte[]> onFrame)
    {
        for (int i = Math.Max(_scanFrom, 1); i < _length; i++)
        {
            if (_buffer[i - 1] != 0xFF)
                continue;

            if (_frameStart < 0)
            {
                if (_buffer[i] == 0xD8)
                    _frameStart = i - 1;
            }
            else if (_buffer[i] == 0xD9)
            {
                onFrame(_buffer.AsSpan(_frameStart, i + 1 - _frameStart).ToArray());
                _frameStart = -1;
            }
        }

        // Move unprocessed bytes to the start of the buffer. Outside a frame keep the last
        // byte, it may be the first half of an FF D8 marker split across two reads.
        int keepFrom = _frameStart >= 0 ? _frameStart : Math.Max(_length - 1, 0);
        if (keepFrom > 0)
        {
            Buffer.BlockCopy(_buffer, keepFrom, _buffer, 0, _length - keepFrom);
            _length -= keepFrom;
            if (_frameStart >= 0)
                _frameStart = 0;
        }

        _scanFrom = _length;
    }

    private int EnsureFree(int requested)
    {
        int free = _buffer.Length - _length;
        if (free >= requested || _buffer.Length >= _maxFrameSize)
            return free;

        int newSize = _buffer.Length;
        while (newSize - _length < requested && newSize < _maxFrameSize)
            newSize = (int)Math.Min((long)newSize * 2, _maxFrameSize);

        Array.Resize(ref _buffer, newSize);
        return _buffer.Length - _length;
    }
}
