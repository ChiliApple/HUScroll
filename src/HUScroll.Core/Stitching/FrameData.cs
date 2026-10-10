/*
 * HUScroll - Scroll-Capture fuer Greenshot
 * Copyright (C) 2026 ChiliApple
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
 *
 * The HU logo (Assets/*) is NOT covered by this license, see Assets/LICENSE-LOGO.txt
 */

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
