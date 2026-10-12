using System;
using System.IO;
namespace CTLife.Net
{
    /// <summary>Read dimensions before Unity/native image allocation. Only the pipeline's PNG/JPEG formats are accepted.</summary>
    public static class LikenessImageHeader
    {
        public static void Size(byte[] bytes, int start, int count, out int width, out int height)
        {
            width = height = 0;
            if (bytes == null || start < 0 || count < 24 || start + (long)count > bytes.Length) throw new InvalidDataException("Invalid image range");
            if (bytes[start] == 137 && bytes[start + 1] == 80 && bytes[start + 2] == 78 && bytes[start + 3] == 71)
            { width = Int32(bytes, start + 16); height = Int32(bytes, start + 20); }
            else if (bytes[start] == 255 && bytes[start + 1] == 216)
            {
                int at = start + 2, end = start + count;
                while (at + 3 < end)
                {
                    if (bytes[at++] != 255) throw new InvalidDataException("Malformed JPEG");
                    while (at < end && bytes[at] == 255) at++;
                    if (at + 2 >= end) break;
                    int marker = bytes[at++];
                    if (marker == 217 || marker == 218) break;
                    if (marker == 1 || marker >= 208 && marker <= 215) continue;
                    int size = bytes[at] * 256 + bytes[at + 1];
                    if (size < 2 || at + size > end) throw new InvalidDataException("Malformed JPEG segment");
                    if (marker >= 192 && marker <= 195 && size >= 7) { height = bytes[at + 3] * 256 + bytes[at + 4]; width = bytes[at + 5] * 256 + bytes[at + 6]; break; }
                    at += size;
                }
            }
            if (width <= 0 || height <= 0 || width > 16000 || height > 16000 || (long)width * height > 64 * 1024 * 1024) throw new InvalidDataException("Image dimensions are unsafe or unsupported");
        }
        private static int Int32(byte[] b, int i) => checked((int)((uint)b[i] << 24 | (uint)b[i + 1] << 16 | (uint)b[i + 2] << 8 | b[i + 3]));
    }
}
