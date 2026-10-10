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

namespace HUScroll.Core.Stitching
{
    /// <summary>
    /// Tuning of the stitching
    /// </summary>
    public sealed class StitchOptions
    {
        /// <summary>
        /// Columns at the right edge which are ignored for matching (scrollbar), at least this many pixels
        /// </summary>
        public int RightExcludeMinPixels { get; set; } = 24;

        /// <summary>
        /// Columns at the right edge which are ignored for matching (scrollbar), as fraction of the width
        /// </summary>
        public double RightExcludeFraction { get; set; } = 0.025;

        /// <summary>
        /// A shift with at least this fraction of matching rows is always accepted
        /// </summary>
        public double MinMatchScore { get; set; } = 0.92;

        /// <summary>
        /// Below <see cref="MinMatchScore"/> a shift is still accepted when it is a clear peak:
        /// at least this score ...
        /// </summary>
        public double MinPeakScore { get; set; } = 0.3;

        /// <summary>
        /// ... and this much better than every other shift (content changed while scrolling, e.g. lazy loaded images)
        /// </summary>
        public double MinPeakMargin { get; set; } = 0.2;

        /// <summary>
        /// Estimating (no clear match, expected shift known): the best shift needs at least this score, otherwise the expected shift is used
        /// </summary>
        public double MinEstimateScore { get; set; } = 0.15;

        /// <summary>
        /// How many estimated frames in a row are allowed before the capture stops with NoMatch
        /// </summary>
        public int MaxConsecutiveEstimates { get; set; } = 1;

        /// <summary>
        /// Low bits of each color channel which are ignored when comparing (anti-aliasing / rendering noise)
        /// </summary>
        public int IgnoreLowColorBits { get; set; } = 3;

        /// <summary>
        /// Minimum number of overlapping rows between two frames
        /// </summary>
        public int MinOverlapRows { get; set; } = 16;

        /// <summary>
        /// The scrolling middle part must be at least this fraction of the frame height,
        /// detected fixed header and footer are reduced if needed
        /// </summary>
        public double MinScrollAreaFraction { get; set; } = 0.25;

        /// <summary>
        /// A column which is identical (at the same position) in this fraction of the rows of two frames,
        /// and is not a plain color, is a fixed element (e.g. a sidebar) and ignored for matching
        /// </summary>
        public double StaticColumnFraction { get; set; } = 0.98;

        /// <summary>
        /// A row needs at least this many color changes to count as "informative" for the matching
        /// (plain rows match everywhere and would make the result ambiguous)
        /// </summary>
        public int InformativeRowMinTransitions { get; set; } = 3;

        /// <summary>
        /// Maximum height of the result in pixels
        /// </summary>
        public int MaxOutputHeight { get; set; } = 30000;
    }
}
