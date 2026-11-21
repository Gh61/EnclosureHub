namespace EnclosureHub.Timelapse;

/// <summary>Reads the picture size from a JPEG header (SOFn marker) without decoding the image.</summary>
internal static class JpegInfo
{
    public static bool TryGetSize(ReadOnlySpan<byte> jpeg, out int width, out int height)
    {
        width = height = 0;
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
            return false;

        int i = 2;
        while (i + 4 <= jpeg.Length)
        {
            if (jpeg[i] != 0xFF)
                return false;

            byte marker = jpeg[i + 1];
            if (marker == 0xFF)
            {
                i++; // fill byte
                continue;
            }

            // Markers without a length
            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                i += 2;
                continue;
            }

            if (marker == 0xD9 || marker == 0xDA)
                return false; // end of image / start of scan before any frame header

            int length = (jpeg[i + 2] << 8) | jpeg[i + 3];
            if (length < 2)
                return false;

            // SOF0–SOF15 except DHT (C4), JPG (C8) and DAC (CC)
            bool isFrameHeader = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
            if (isFrameHeader)
            {
                if (i + 9 > jpeg.Length)
                    return false;

                height = (jpeg[i + 5] << 8) | jpeg[i + 6];
                width = (jpeg[i + 7] << 8) | jpeg[i + 8];
                return width > 0 && height > 0;
            }

            i += 2 + length;
        }

        return false;
    }
}
