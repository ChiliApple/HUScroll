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
