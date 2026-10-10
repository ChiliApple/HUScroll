using System;

namespace HUScroll.Core.Stitching
{
    /// <summary>
    /// A captured frame as raw 32 bit ARGB pixels (row major, no padding).
    /// Pure data, no System.Drawing dependency, so the stitching logic can be unit tested anywhere.
    /// </summary>
    public sealed class FrameData
    {
        public FrameData(int width, int height, int[] pixels)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));
            if (pixels.Length != width * height) throw new ArgumentException("Pixel count does not match width * height", nameof(pixels));
            Width = width;
            Height = height;
            Pixels = pixels;
        }

        public int Width { get; }

        public int Height { get; }

        public int[] Pixels { get; }

        public int this[int x, int y] => Pixels[y * Width + x];

        /// <summary>
        /// True when both frames have the same size and identical pixels
        /// </summary>
        public bool PixelEquals(FrameData other)
        {
            if (other == null || other.Width != Width || other.Height != Height) return false;
            var a = Pixels;
            var b = other.Pixels;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        /// <summary>
        /// Copy the rows [startRow, startRow + rowCount) into a new array
        /// </summary>
        public int[] CopyRows(int startRow, int rowCount)
        {
            if (startRow < 0 || rowCount < 0 || startRow + rowCount > Height) throw new ArgumentOutOfRangeException(nameof(rowCount));
            var result = new int[rowCount * Width];
            Array.Copy(Pixels, startRow * Width, result, 0, result.Length);
            return result;
        }
    }
}
