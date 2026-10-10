using System;
using System.Collections.Generic;

namespace HUScroll.Core.Stitching
{
    /// <summary>
    /// Result of adding a frame to the <see cref="StitchEngine"/>
    /// </summary>
    public enum StitchStatus
    {
        /// <summary>The first frame, nothing to compare yet</summary>
        First,
        /// <summary>The frame scrolled, the new rows were appended</summary>
        Appended,
        /// <summary>The content didn't move (end of the page reached, or nothing scrolled)</summary>
        NoMovement,
        /// <summary>No reliable overlap found, the frame was not appended</summary>
        NoMatch,
        /// <summary>The new rows were appended and the maximum height is reached</summary>
        LimitReached
    }

    /// <summary>
    /// What happened with a frame
    /// </summary>
    public readonly struct StitchResult
    {
        public StitchResult(StitchStatus status, int shift, double score)
        {
            Status = status;
            Shift = shift;
            Score = score;
        }

        public StitchStatus Status { get; }

        /// <summary>
        /// The number of rows the content moved up between the previous and this frame
        /// </summary>
        public int Shift { get; }

        /// <summary>
        /// Fraction of the compared rows which matched for the chosen shift (0..1)
        /// </summary>
        public double Score { get; }

        public override string ToString() => $"{Status} shift={Shift} score={Score:0.000}";
    }

    /// <summary>
    /// Stitches frames of a vertically scrolled area into one tall image.
    ///
    /// Every new frame is compared with the previous one:
    ///  1. Rows which are identical at the same position at the top / bottom are a fixed header / footer
    ///     (e.g. a sticky navigation bar or the browser toolbar). They are detected with the first pair and only taken once.
    ///  2. The right edge (scrollbar) and columns which don't move (fixed sidebars) are ignored for the matching.
    ///  3. In the scrolling part the shift with the best row match (by row hashes) is searched.
    ///  4. Only the rows which are new (scrolled in at the bottom) are appended.
    /// Only the previous frame and the appended rows are kept in memory.
    /// </summary>
    public sealed class StitchEngine
    {
        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        private readonly StitchOptions _options;
        private readonly List<int[]> _blocks = new List<int[]>();
        private FrameData _previous;
        private bool _layoutKnown;
        private int _header;
        private int _footer;
        private int _blockRows;
        private int _lastShift;

        private readonly int _quantize;

        public StitchEngine(StitchOptions options = null)
        {
            _options = options ?? new StitchOptions();
            int ignore = (1 << Math.Max(0, Math.Min(7, _options.IgnoreLowColorBits))) - 1;
            int channel = 0xFF & ~ignore;
            _quantize = (channel << 16) | (channel << 8) | channel;
        }

        /// <summary>
        /// Number of frames which are part of the result
        /// </summary>
        public int FrameCount { get; private set; }

        /// <summary>
        /// Detected fixed header in rows (valid after the first appended frame)
        /// </summary>
        public int Header => _header;

        /// <summary>
        /// Detected fixed footer in rows (valid after the first appended frame)
        /// </summary>
        public int Footer => _footer;

        /// <summary>
        /// Width of the frames, 0 before the first frame
        /// </summary>
        public int Width => _previous?.Width ?? 0;

        /// <summary>
        /// Height the composed result has right now
        /// </summary>
        public int ResultHeight
        {
            get
            {
                if (_previous == null) return 0;
                return _layoutKnown ? _blockRows + _footer : _previous.Height;
            }
        }

        /// <summary>
        /// Add the next frame (same size as the first one)
        /// </summary>
        public StitchResult Add(FrameData frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));

            if (_previous == null)
            {
                _previous = frame;
                FrameCount = 1;
                return new StitchResult(StitchStatus.First, 0, 1);
            }

            if (frame.Width != _previous.Width || frame.Height != _previous.Height)
            {
                throw new ArgumentException("All frames must have the same size", nameof(frame));
            }

            if (frame.PixelEquals(_previous))
            {
                return new StitchResult(StitchStatus.NoMovement, 0, 1);
            }

            int width = frame.Width;
            int height = frame.Height;

            // 1. Ignore the right edge (scrollbar thumb moves with every scroll)
            int rightExclude = Math.Max(_options.RightExcludeMinPixels, (int)Math.Round(width * _options.RightExcludeFraction));
            rightExclude = Math.Min(rightExclude, width / 4);
            var baseMask = new bool[width];
            for (int x = 0; x < width - rightExclude; x++)
            {
                baseMask[x] = true;
            }

            var previousFull = RowHashes(_previous, baseMask, 0, height, _quantize);
            var currentFull = RowHashes(frame, baseMask, 0, height, _quantize);

            // Content at the same position (only something small changed, e.g. an animation): nothing scrolled
            var informativeFull = InformativeRows(_previous, baseMask, 0, height, _options.InformativeRowMinTransitions, _quantize);
            Score(previousFull, currentFull, informativeFull, 0, out double unchangedScore, out _);
            if (unchangedScore >= _options.MinMatchScore)
            {
                return new StitchResult(StitchStatus.NoMovement, 0, unchangedScore);
            }

            // 2. Fixed header / footer: rows identical at the same position
            int header;
            int footer;
            if (_layoutKnown)
            {
                header = _header;
                footer = _footer;
            }
            else
            {
                header = 0;
                while (header < height && previousFull[header] == currentFull[header])
                {
                    header++;
                }

                if (header == height)
                {
                    // Only the ignored columns changed (e.g. a scrollbar which flashed), the content didn't move
                    return new StitchResult(StitchStatus.NoMovement, 0, 1);
                }

                footer = 0;
                while (footer < height - header && previousFull[height - 1 - footer] == currentFull[height - 1 - footer])
                {
                    footer++;
                }

                // Make sure there is enough room to scroll, rows that are equal by coincidence (white margins) are reduced first at the bottom
                int minScrollArea = Math.Max(_options.MinOverlapRows * 2, (int)(height * _options.MinScrollAreaFraction));
                int excess = header + footer - (height - minScrollArea);
                if (excess > 0)
                {
                    int reduceFooter = Math.Min(footer, excess);
                    footer -= reduceFooter;
                    excess -= reduceFooter;
                    header = Math.Max(0, header - excess);
                }
            }

            int top = header;
            int bottom = height - footer;
            int scrollRows = bottom - top;
            int maxShift = scrollRows - _options.MinOverlapRows;
            if (maxShift < 1)
            {
                return new StitchResult(StitchStatus.NoMatch, 0, 0);
            }

            // 3. Ignore columns which don't move but have content (fixed sidebars, floating buttons)
            var mask = BuildMovingMask(_previous, frame, baseMask, top, bottom);

            var previousHashes = RowHashes(_previous, mask, top, scrollRows, _quantize);
            var currentHashes = RowHashes(frame, mask, top, scrollRows, _quantize);
            var informative = InformativeRows(_previous, mask, top, scrollRows, _options.InformativeRowMinTransitions, _quantize);

            // 4. Find the shift with the best match
            Score(previousHashes, currentHashes, informative, 0, out double zeroScore, out _);
            int bestShift = 0;
            double bestScore = -1;
            int bestMatches = -1;
            var scores = new double[maxShift + 1];
            scores[0] = zeroScore;
            for (int shift = 1; shift <= maxShift; shift++)
            {
                Score(previousHashes, currentHashes, informative, shift, out double score, out int matches);
                scores[shift] = score;
                bool better = score > bestScore + 1e-9;
                if (!better && Math.Abs(score - bestScore) <= 1e-9)
                {
                    better = matches > bestMatches || (matches == bestMatches && IsPreferredShift(shift, bestShift));
                }

                if (!better) continue;
                bestScore = score;
                bestShift = shift;
                bestMatches = matches;
            }

            if (zeroScore >= _options.MinMatchScore && zeroScore >= bestScore)
            {
                // The content is at the same position (only something animated changed)
                return new StitchResult(StitchStatus.NoMovement, 0, zeroScore);
            }

            if (!IsAccepted(scores, bestShift, bestScore))
            {
                return new StitchResult(StitchStatus.NoMatch, bestShift, bestScore);
            }

            // 5. Commit
            if (!_layoutKnown)
            {
                _header = header;
                _footer = footer;
                _layoutKnown = true;
                AddBlock(_previous.CopyRows(0, bottom));
            }

            AddBlock(frame.CopyRows(bottom - bestShift, bestShift));
            _previous = frame;
            _lastShift = bestShift;
            FrameCount++;

            var status = ResultHeight >= _options.MaxOutputHeight ? StitchStatus.LimitReached : StitchStatus.Appended;
            return new StitchResult(status, bestShift, bestScore);
        }

        /// <summary>
        /// Compose the result: fixed header + all scrolled rows + the footer of the last frame
        /// </summary>
        public FrameData Compose()
        {
            if (_previous == null) return null;
            if (!_layoutKnown)
            {
                return new FrameData(_previous.Width, _previous.Height, (int[])_previous.Pixels.Clone());
            }

            int width = _previous.Width;
            int rows = Math.Min(_blockRows + _footer, Math.Max(_options.MaxOutputHeight, _previous.Height));
            var pixels = new int[width * rows];
            int offset = 0;
            foreach (var block in _blocks)
            {
                int length = Math.Min(block.Length, pixels.Length - offset);
                if (length <= 0) break;
                Array.Copy(block, 0, pixels, offset, length);
                offset += length;
            }

            if (_footer > 0 && offset < pixels.Length)
            {
                var footerPixels = _previous.CopyRows(_previous.Height - _footer, _footer);
                Array.Copy(footerPixels, 0, pixels, offset, Math.Min(footerPixels.Length, pixels.Length - offset));
            }

            return new FrameData(width, rows, pixels);
        }

        private void AddBlock(int[] block)
        {
            _blocks.Add(block);
            _blockRows += block.Length / _previous.Width;
        }

        /// <summary>
        /// A shift is accepted when it matches (almost) perfectly, or when it is a clear peak:
        /// content that changed while scrolling (lazy loaded images, re-rendered text) lowers the score of the right shift,
        /// but every other shift stays far below it.
        /// </summary>
        private bool IsAccepted(double[] scores, int bestShift, double bestScore)
        {
            if (bestScore >= _options.MinMatchScore) return true;
            if (bestScore < _options.MinPeakScore) return false;

            double secondBest = 0;
            for (int shift = 0; shift < scores.Length; shift++)
            {
                if (Math.Abs(shift - bestShift) <= 3) continue;
                if (scores[shift] > secondBest) secondBest = scores[shift];
            }

            return bestScore - secondBest >= _options.MinPeakMargin;
        }

        private bool IsPreferredShift(int candidate, int current)
        {
            if (_lastShift <= 0) return candidate < current;
            return Math.Abs(candidate - _lastShift) < Math.Abs(current - _lastShift);
        }

        private bool[] BuildMovingMask(FrameData previous, FrameData current, bool[] baseMask, int top, int bottom)
        {
            int width = previous.Width;
            int rows = bottom - top;
            var mask = (bool[])baseMask.Clone();
            int remaining = 0;
            for (int x = 0; x < width; x++)
            {
                if (!mask[x]) continue;
                int first = previous[x, top];
                bool uniform = true;
                int same = 0;
                for (int y = top; y < bottom; y++)
                {
                    int p = previous[x, y];
                    if (p != first) uniform = false;
                    if (p == current[x, y]) same++;
                }

                if (!uniform && same >= rows * _options.StaticColumnFraction)
                {
                    mask[x] = false;
                }
                else
                {
                    remaining++;
                }
            }

            // When almost everything looks static, don't trust the detection
            return remaining < Math.Max(8, width / 10) ? baseMask : mask;
        }

        private static void Score(ulong[] previous, ulong[] current, bool[] informative, int shift, out double score, out int matches)
        {
            int count = previous.Length;
            int considered = 0;
            matches = 0;
            for (int i = shift; i < count; i++)
            {
                if (!informative[i]) continue;
                considered++;
                if (previous[i] == current[i - shift]) matches++;
            }

            if (considered < 3)
            {
                // A (nearly) plain area: compare all rows
                considered = 0;
                matches = 0;
                for (int i = shift; i < count; i++)
                {
                    considered++;
                    if (previous[i] == current[i - shift]) matches++;
                }
            }

            score = considered == 0 ? 0 : (double)matches / considered;
        }

        private static ulong[] RowHashes(FrameData frame, bool[] mask, int startRow, int rowCount, int quantize)
        {
            var hashes = new ulong[rowCount];
            var pixels = frame.Pixels;
            int width = frame.Width;
            for (int r = 0; r < rowCount; r++)
            {
                int rowStart = (startRow + r) * width;
                ulong hash = FnvOffset;
                for (int x = 0; x < width; x++)
                {
                    if (!mask[x]) continue;
                    hash ^= (uint)(pixels[rowStart + x] & quantize);
                    hash *= FnvPrime;
                }

                hashes[r] = hash;
            }

            return hashes;
        }

        private static bool[] InformativeRows(FrameData frame, bool[] mask, int startRow, int rowCount, int minTransitions, int quantize)
        {
            var result = new bool[rowCount];
            var pixels = frame.Pixels;
            int width = frame.Width;
            for (int r = 0; r < rowCount; r++)
            {
                int rowStart = (startRow + r) * width;
                int transitions = 0;
                bool hasLast = false;
                int last = 0;
                for (int x = 0; x < width && transitions < minTransitions; x++)
                {
                    if (!mask[x]) continue;
                    int p = pixels[rowStart + x] & quantize;
                    if (hasLast && p != last) transitions++;
                    last = p;
                    hasLast = true;
                }

                result[r] = transitions >= minTransitions;
            }

            return result;
        }
    }
}
