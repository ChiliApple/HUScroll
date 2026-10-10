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
using System.Threading.Tasks;

namespace HUScroll.Core.Stitching
{
    /// <summary>
    /// Finds animated content (GIFs, videos, carousels) by comparing two consecutive grabs:
    /// if the content did not move vertically, every changed pixel is animated.
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
        /// Width of the vertical strips used to tell scrolling from animation
        /// </summary>
        public int StripWidth { get; set; } = 16;

        /// <summary>
        /// A mask covering more than this fraction of the pixels is not trusted (dropped)
        /// </summary>
        public double MaxMaskedPixelFraction { get; set; } = 0.9;

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
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == b[i]) continue;
                changed = changed ?? new bool[a.Length];
                changed[i] = true;
            }

            if (changed == null) return true;
            if (IsScrolled(previous, current)) return false;

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

        /// <summary>
        /// Scrolled or animated in place? The image is cut into vertical strips, every strip row gets a hash.
        /// Animation: the strip rows which have content are mostly unchanged at the same position, no shift explains more.
        /// Scrolling: a vertical shift explains clearly more strip rows than "no shift".
        /// Works even when most of the area is animated (e.g. a page full of GIFs).
        /// </summary>
        private bool IsScrolled(FrameData previous, FrameData current)
        {
            const int quantize = 0x00F8F8F8;
            int strips = (_width + StripWidth - 1) / StripWidth;
            var a = StripHashes(previous, strips, quantize, out var informative);
            var b = StripHashes(current, strips, quantize, out _);

            double samePosition = MatchRatio(a, b, informative, strips, 0);
            // Clearly the same position: most structure (text, borders, gaps between GIFs) is unchanged
            if (samePosition >= 0.9) return false;

            int maxShift = _height * 3 / 4;
            double best = 0;
            object gate = new object();
            // The shifts are independent, use all cores (keeps the manual mode responsive while scrolling)
            Parallel.For(-maxShift, maxShift + 1, () => 0.0, (shift, _, localBest) =>
            {
                if (shift == 0) return localBest;
                double ratio = MatchRatio(a, b, informative, strips, shift);
                return ratio > localBest ? ratio : localBest;
            }, localBest =>
            {
                lock (gate)
                {
                    if (localBest > best) best = localBest;
                }
            });

            // A real shift explains clearly more than "same position" (even if animations keep both ratios low)
            return best >= 0.1 && best > samePosition * 1.5 + 0.05;
        }

        /// <summary>
        /// Fraction of the informative strip rows of the previous grab which are found in the current grab moved up by <paramref name="shift"/> rows
        /// </summary>
        private double MatchRatio(ulong[] a, ulong[] b, bool[] informative, int strips, int shift)
        {
            int considered = 0;
            int matches = 0;
            int from = Math.Max(0, shift);
            int to = Math.Min(_height, _height + shift);
            // Every 3rd row is enough for the decision and keeps it fast (the shift itself is still exact)
            for (int y = from; y < to; y += 3)
            {
                int rowA = y * strips;
                int rowB = (y - shift) * strips;
                for (int strip = 0; strip < strips; strip++)
                {
                    if (!informative[rowA + strip]) continue;
                    considered++;
                    if (a[rowA + strip] == b[rowB + strip]) matches++;
                }
            }

            // Too little overlap to say anything
            return considered < 8 ? 0 : (double)matches / considered;
        }

        private ulong[] StripHashes(FrameData frame, int strips, int quantize, out bool[] informative)
        {
            var hashes = new ulong[_height * strips];
            informative = new bool[_height * strips];
            var pixels = frame.Pixels;
            for (int y = 0; y < _height; y++)
            {
                int rowStart = y * _width;
                for (int strip = 0; strip < strips; strip++)
                {
                    int x0 = strip * StripWidth;
                    int x1 = Math.Min(_width, x0 + StripWidth);
                    ulong hash = 14695981039346656037UL;
                    int first = pixels[rowStart + x0] & quantize;
                    bool varies = false;
                    for (int x = x0; x < x1; x++)
                    {
                        int value = pixels[rowStart + x] & quantize;
                        if (value != first) varies = true;
                        hash ^= (uint)value;
                        hash *= 1099511628211UL;
                    }

                    hashes[y * strips + strip] = hash;
                    // Plain segments (background) match everywhere and say nothing
                    informative[y * strips + strip] = varies;
                }
            }

            return hashes;
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
