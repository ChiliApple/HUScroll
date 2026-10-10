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
    /// Finds animated content (GIFs, videos, carousels) by comparing two grabs of the same scroll position:
    /// scrolling changes (almost) every row, an animation only a limited part.
    /// </summary>
    public sealed class AnimationDetector
    {
        private readonly int _width;
        private readonly int _height;
        private bool[] _mask;

        public AnimationDetector(int width, int height)
        {
            _width = width;
            _height = height;
        }

        /// <summary>
        /// Rows with at least one changed pixel above this fraction mean "scrolling", not "animation"
        /// </summary>
        public double MaxAnimatedRowFraction { get; set; } = 0.6;

        /// <summary>
        /// A mask covering more than this fraction of the pixels is not trusted (dropped)
        /// </summary>
        public double MaxMaskedPixelFraction { get; set; } = 0.5;

        /// <summary>
        /// Pixels around a change which are masked as well (anti-aliased edges of the animation)
        /// </summary>
        public int Padding { get; set; } = 3;

        /// <summary>
        /// The animated pixels collected since the last <see cref="Reset"/>, null when nothing was animated
        /// </summary>
        public bool[] Mask => _mask;

        public void Reset() => _mask = null;

        /// <summary>
        /// Compare two consecutive grabs.
        /// </summary>
        /// <returns>
        /// true when they show the same scroll position: identical, or only an animation changed (it is added to <see cref="Mask"/>);
        /// false when the content moved (still scrolling)
        /// </returns>
        public bool Observe(FrameData previous, FrameData current)
        {
            if (previous == null || current == null || previous.Width != _width || current.Width != _width
                || previous.Height != _height || current.Height != _height)
            {
                return false;
            }

            var a = previous.Pixels;
            var b = current.Pixels;
            bool[] changed = null;
            int changedRows = 0;
            for (int y = 0; y < _height; y++)
            {
                int rowStart = y * _width;
                bool rowChanged = false;
                for (int x = 0; x < _width; x++)
                {
                    int i = rowStart + x;
                    if (a[i] == b[i]) continue;
                    changed = changed ?? new bool[a.Length];
                    changed[i] = true;
                    rowChanged = true;
                }

                if (rowChanged) changedRows++;
            }

            if (changed == null) return true;
            if (changedRows > _height * MaxAnimatedRowFraction) return false;

            Dilate(changed);
            if (_mask == null)
            {
                _mask = changed;
            }
            else
            {
                for (int i = 0; i < _mask.Length; i++)
                {
                    _mask[i] |= changed[i];
                }
            }

            return true;
        }

        /// <summary>
        /// The mask to attach to a frame, null when there is none or it covers too much to be useful
        /// </summary>
        public bool[] GetUsableMask()
        {
            if (_mask == null) return null;
            int count = 0;
            foreach (bool m in _mask)
            {
                if (m) count++;
            }

            return count > _mask.Length * MaxMaskedPixelFraction ? null : (bool[])_mask.Clone();
        }

        private void Dilate(bool[] mask)
        {
            int r = Math.Max(0, Padding);
            if (r == 0) return;

            // horizontal
            var temp = new bool[mask.Length];
            for (int y = 0; y < _height; y++)
            {
                int rowStart = y * _width;
                int lastSet = -1000000;
                for (int x = 0; x < _width; x++)
                {
                    if (mask[rowStart + x]) lastSet = x;
                    if (x - lastSet <= r) temp[rowStart + x] = true;
                }

                lastSet = 1000000;
                for (int x = _width - 1; x >= 0; x--)
                {
                    if (mask[rowStart + x]) lastSet = x;
                    if (lastSet - x <= r) temp[rowStart + x] = true;
                }
            }

            // vertical
            Array.Clear(mask, 0, mask.Length);
            for (int x = 0; x < _width; x++)
            {
                int lastSet = -1000000;
                for (int y = 0; y < _height; y++)
                {
                    if (temp[y * _width + x]) lastSet = y;
                    if (y - lastSet <= r) mask[y * _width + x] = true;
                }

                lastSet = 1000000;
                for (int y = _height - 1; y >= 0; y--)
                {
                    if (temp[y * _width + x]) lastSet = y;
                    if (lastSet - y <= r) mask[y * _width + x] = true;
                }
            }
        }
    }
}
