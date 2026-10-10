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
 * Name and HU logo: no trademark rights are granted (GPLv3 section 7 e), see Assets/LICENSE-LOGO.txt
 */

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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
        LimitReached,
        /// <summary>The content moved down (the user scrolled back up), nothing appended, the deepest frame stays the reference</summary>
        ScrolledBack
    }

    /// <summary>
    /// What happened with a frame
    /// </summary>
    public readonly struct StitchResult
    {
        public StitchResult(StitchStatus status, int shift, double score, bool estimated = false)
        {
            Status = status;
            Shift = shift;
            Score = score;
            Estimated = estimated;
        }

        /// <summary>
        /// True when no clear match was found and the shift was estimated from the expected scroll distance
        /// </summary>
        public bool Estimated { get; }

        public StitchStatus Status { get; }

        /// <summary>
        /// The number of rows the content moved up between the previous and this frame
        /// </summary>
        public int Shift { get; }

        /// <summary>
        /// Fraction of the compared cells which matched for the chosen shift (0..1)
        /// </summary>
        public double Score { get; }

        public override string ToString() => $"{Status} shift={Shift} score={Score:0.000}{(Estimated ? " (estimated)" : "")}";
    }

    /// <summary>
    /// Stitches frames of a vertically scrolled area into one tall image.
    ///
    /// Every frame is cut into a grid of cells (a strip of <see cref="StitchOptions.StripWidth"/> pixels, one row high),
    /// each cell gets a hash. A new frame is compared with the previous one:
    ///  1. Rows which are identical at the same position at the top / bottom are a fixed header / footer
    ///     (e.g. a sticky navigation bar or the browser toolbar). They are detected with the first pair and only taken once.
    ///  2. The right edge (scrollbar) and strips which don't move (fixed sidebars) are ignored.
    ///  3. Cells with animated content (<see cref="FrameData.Animated"/>) are ignored when they are animated in either frame -
    ///     so a video background or a GIF doesn't spoil the rows it runs through.
    ///  4. In the scrolling part the shift where most cells match is searched; only the new rows are appended.
    /// Only the previous frame and the appended rows are kept in memory.
    /// </summary>
    public sealed class StitchEngine
    {
        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        private readonly StitchOptions _options;
        private readonly List<int[]> _blocks = new List<int[]>();
        private readonly int _quantize;
        private FrameData _previous;
        private bool _layoutKnown;
        private int _header;
        private int _footer;
        private int _blockRows;
        private int _lastShift;
        private int _consecutiveEstimates;

        public StitchEngine(StitchOptions options = null)
        {
            _options = options ?? new StitchOptions();
            int ignore = (1 << Math.Max(0, Math.Min(7, _options.IgnoreLowColorBits))) - 1;
            int channel = 0xFF & ~ignore;
            _quantize = (channel << 16) | (channel << 8) | channel;
        }

        /// <summary>Number of frames which are part of the result</summary>
        public int FrameCount { get; private set; }

        /// <summary>Detected fixed header in rows (valid after the first appended frame)</summary>
        public int Header => _header;

        /// <summary>Detected fixed footer in rows (valid after the first appended frame)</summary>
        public int Footer => _footer;

        /// <summary>Width of the frames, 0 before the first frame</summary>
        public int Width => _previous?.Width ?? 0;

        /// <summary>Number of appended frames whose shift was estimated</summary>
        public int EstimatedCount { get; private set; }

        /// <summary>Height the composed result has right now</summary>
        public int ResultHeight
        {
            get
            {
                if (_previous == null) return 0;
                return _layoutKnown ? _blockRows + _footer : _previous.Height;
            }
        }

        /// <summary>
        /// Hashes of the cells of a frame: index row * Strips + strip
        /// </summary>
        private sealed class Grid
        {
            public int Strips;
            public int Rows;
            public ulong[] Hash;
            /// <summary>The cell has content (not a plain color), plain cells match everywhere and say nothing</summary>
            public bool[] Informative;
            /// <summary>At least one pixel of the cell is animated</summary>
            public bool[] Animated;
        }

        /// <summary>
        /// Add the next frame (same size as the first one)
        /// </summary>
        /// <param name="frame">The frame</param>
        /// <param name="expectedShift">The expected scroll distance in rows (0 = unknown). When there is no clear match,
        /// the best shift up to a bit more than this is taken (estimated), so a capture doesn't stop at changing content.</param>
        public StitchResult Add(FrameData frame, int expectedShift = 0)
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
            int usableWidth = width - rightExclude;
            int stripWidth = Math.Max(4, _options.StripWidth);

            var previousGrid = BuildGrid(_previous, usableWidth, stripWidth);
            var currentGrid = BuildGrid(frame, usableWidth, stripWidth);
            int strips = previousGrid.Strips;
            var allStrips = new bool[strips];
            for (int c = 0; c < strips; c++) allStrips[c] = true;

            // Content at the same position (only something small changed, e.g. an animation that wasn't detected): nothing scrolled
            CellScore(previousGrid, currentGrid, 0, height, 0, allStrips, out double unchangedScore, out int unchangedConsidered, out _);
            if (unchangedConsidered >= MinCells(strips, height) && unchangedScore >= _options.MinMatchScore)
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
                while (header < height && RowUnchanged(previousGrid, currentGrid, header)) header++;
                if (header == height)
                {
                    return new StitchResult(StitchStatus.NoMovement, 0, 1);
                }

                footer = 0;
                while (footer < height - header && RowUnchanged(previousGrid, currentGrid, height - 1 - footer)) footer++;

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

            // 3. Ignore strips which don't move but have content (fixed sidebars, floating buttons)
            var useStrips = MovingStrips(previousGrid, currentGrid, top, bottom);

            // Not enough still content to compare (e.g. a page full of GIFs, a video): only an estimate from the scroll distance
            if (CountComparableCells(previousGrid, top, bottom, useStrips) < MinCells(strips, scrollRows))
            {
                if (!TryEstimate(new double[maxShift + 1], expectedShift, maxShift, out int blindShift, out _))
                {
                    _consecutiveEstimates = 0;
                    return new StitchResult(StitchStatus.NoMatch, 0, 0);
                }

                return Commit(frame, header, footer, bottom, blindShift, 0, true);
            }

            // 4. Find the shift with the best match (content moved up)
            var scores = new double[maxShift + 1];
            var matchCounts = new int[maxShift + 1];
            Parallel.For(0, maxShift + 1, shift =>
            {
                CellScore(previousGrid, currentGrid, top, bottom, shift, useStrips, out double score, out _, out int matches);
                scores[shift] = score;
                matchCounts[shift] = matches;
            });

            double zeroScore = scores[0];
            int bestShift = 0;
            double bestScore = -1;
            int bestMatches = -1;
            for (int shift = 1; shift <= maxShift; shift++)
            {
                double score = scores[shift];
                bool better = score > bestScore + 1e-9;
                if (!better && Math.Abs(score - bestScore) <= 1e-9)
                {
                    better = matchCounts[shift] > bestMatches || (matchCounts[shift] == bestMatches && IsPreferredShift(shift, bestShift));
                }

                if (!better) continue;
                bestScore = score;
                bestShift = shift;
                bestMatches = matchCounts[shift];
            }

            if (zeroScore >= _options.MinMatchScore && zeroScore >= bestScore)
            {
                // The content is at the same position (only something animated changed)
                return new StitchResult(StitchStatus.NoMovement, 0, zeroScore);
            }

            bool forwardAccepted = IsAccepted(scores, bestShift, bestScore);

            // Content moved down (scrolled back up, manual mode): not appended, the previous (deepest) frame stays the reference.
            // Only searched when scrolling down doesn't explain the frame (saves half of the work).
            if (!forwardAccepted || bestScore < _options.MinMatchScore)
            {
                var back = CheckScrolledBack(previousGrid, currentGrid, top, bottom, maxShift, useStrips, zeroScore, bestScore);
                if (back.HasValue) return back.Value;
            }

            bool estimated = false;
            if (!forwardAccepted)
            {
                if (!TryEstimate(scores, expectedShift, maxShift, out int estimatedShift, out double estimatedScore))
                {
                    _consecutiveEstimates = 0;
                    return new StitchResult(StitchStatus.NoMatch, bestShift, bestScore);
                }

                bestShift = estimatedShift;
                bestScore = estimatedScore;
                estimated = true;
            }

            return Commit(frame, header, footer, bottom, bestShift, bestScore, estimated);
        }

        private StitchResult? CheckScrolledBack(Grid previousGrid, Grid currentGrid, int top, int bottom, int maxShift, bool[] useStrips,
            double zeroScore, double bestScore)
        {
            var backScores = new double[maxShift + 1];
            backScores[0] = zeroScore;
            Parallel.For(1, maxShift + 1, shift =>
            {
                CellScore(currentGrid, previousGrid, top, bottom, shift, useStrips, out double score, out _, out _);
                backScores[shift] = score;
            });
            int backShift = 0;
            double backScore = -1;
            for (int shift = 1; shift <= maxShift; shift++)
            {
                if (backScores[shift] <= backScore) continue;
                backScore = backScores[shift];
                backShift = shift;
            }

            if (backScore > bestScore && IsAccepted(backScores, backShift, backScore))
            {
                return new StitchResult(StitchStatus.ScrolledBack, -backShift, backScore);
            }

            return null;
        }

        /// <summary>
        /// Compose the result: fixed header + all scrolled rows + the footer of the last frame
        /// </summary>
        public FrameData Compose()
        {
            if (_previous == null) return null;
            int width = _previous.Width;
            var pixels = new int[width * ComposedHeight];
            int offset = 0;
            foreach (var block in ComposedBlocks())
            {
                Array.Copy(block, 0, pixels, offset, block.Length);
                offset += block.Length;
            }

            return new FrameData(width, ComposedHeight, pixels);
        }

        /// <summary>
        /// Height of the composed result (see <see cref="ComposedBlocks"/>)
        /// </summary>
        public int ComposedHeight
        {
            get
            {
                if (_previous == null) return 0;
                if (!_layoutKnown) return _previous.Height;
                return Math.Min(_blockRows + _footer, Math.Max(_options.MaxOutputHeight, _previous.Height));
            }
        }

        /// <summary>
        /// The rows of the result in order (row major, <see cref="Width"/> pixels per row), without building one big array:
        /// lets the caller write them straight into a bitmap (saves a full copy of a tall image).
        /// The sum of the rows is <see cref="ComposedHeight"/>.
        /// </summary>
        public IEnumerable<int[]> ComposedBlocks()
        {
            if (_previous == null) yield break;
            if (!_layoutKnown)
            {
                yield return _previous.Pixels;
                yield break;
            }

            int width = _previous.Width;
            int remaining = ComposedHeight;
            foreach (var block in _blocks)
            {
                if (remaining <= 0) yield break;
                int rows = block.Length / width;
                if (rows <= remaining)
                {
                    yield return block;
                }
                else
                {
                    var part = new int[remaining * width];
                    Array.Copy(block, part, part.Length);
                    yield return part;
                }
                remaining -= rows;
            }

            if (_footer > 0 && remaining > 0)
            {
                yield return _previous.CopyRows(_previous.Height - _footer, Math.Min(_footer, remaining));
            }
        }

        /// <summary>
        /// Drop the collected rows (after the result was written), so the memory is free before the editor opens
        /// </summary>
        public void Release()
        {
            _blocks.Clear();
            _blockRows = 0;
            _previous = null;
            _layoutKnown = false;
        }

        private StitchResult Commit(FrameData frame, int header, int footer, int bottom, int shift, double score, bool estimated)
        {
            if (!_layoutKnown)
            {
                _header = header;
                _footer = footer;
                _layoutKnown = true;
                AddBlock(_previous.CopyRows(0, bottom));
            }

            AddBlock(frame.CopyRows(bottom - shift, shift));
            _previous = frame;
            _lastShift = shift;
            FrameCount++;
            if (estimated)
            {
                EstimatedCount++;
                _consecutiveEstimates++;
            }
            else
            {
                _consecutiveEstimates = 0;
            }

            var status = ResultHeight >= _options.MaxOutputHeight ? StitchStatus.LimitReached : StitchStatus.Appended;
            return new StitchResult(status, shift, score, estimated);
        }

        private void AddBlock(int[] block)
        {
            _blocks.Add(block);
            _blockRows += block.Length / _previous.Width;
        }

        private Grid BuildGrid(FrameData frame, int usableWidth, int stripWidth)
        {
            int strips = Math.Max(1, (usableWidth + stripWidth - 1) / stripWidth);
            int rows = frame.Height;
            var grid = new Grid
            {
                Strips = strips,
                Rows = rows,
                Hash = new ulong[rows * strips],
                Informative = new bool[rows * strips],
                Animated = new bool[rows * strips]
            };
            var pixels = frame.Pixels;
            var animated = frame.Animated;
            int width = frame.Width;
            int quantize = _quantize;
            Parallel.For(0, rows, y =>
            {
                int rowStart = y * width;
                for (int c = 0; c < strips; c++)
                {
                    int x0 = c * stripWidth;
                    int x1 = Math.Min(usableWidth, x0 + stripWidth);
                    ulong hash = FnvOffset;
                    int first = pixels[rowStart + x0] & quantize;
                    int transitions = 0;
                    int last = first;
                    bool anim = false;
                    for (int x = x0; x < x1; x++)
                    {
                        int index = rowStart + x;
                        int value = pixels[index] & quantize;
                        if (value != last) transitions++;
                        last = value;
                        if (animated != null && animated[index]) anim = true;
                        hash ^= (uint)value;
                        hash *= FnvPrime;
                    }

                    int cell = y * strips + c;
                    grid.Hash[cell] = hash;
                    grid.Informative[cell] = transitions >= _options.InformativeRowMinTransitions;
                    grid.Animated[cell] = anim;
                }
            });
            return grid;
        }

        /// <summary>
        /// Compare row (top + i) of <paramref name="a"/> with row (top + i - shift) of <paramref name="b"/> for all i in [shift, bottom - top).
        /// Cells animated in either frame are skipped; only informative cells of <paramref name="a"/> count,
        /// unless there are almost none (then all comparable cells).
        /// </summary>
        private static void CellScore(Grid a, Grid b, int top, int bottom, int shift, bool[] useStrips,
            out double score, out int considered, out int matches)
        {
            int strips = a.Strips;
            int rows = bottom - top;
            considered = 0;
            matches = 0;
            int plainConsidered = 0;
            int plainMatches = 0;
            for (int i = shift; i < rows; i++)
            {
                int rowA = (top + i) * strips;
                int rowB = (top + i - shift) * strips;
                for (int c = 0; c < strips; c++)
                {
                    if (!useStrips[c]) continue;
                    int cellA = rowA + c;
                    int cellB = rowB + c;
                    if (a.Animated[cellA] || b.Animated[cellB]) continue;
                    bool equal = a.Hash[cellA] == b.Hash[cellB];
                    if (a.Informative[cellA])
                    {
                        considered++;
                        if (equal) matches++;
                    }
                    else
                    {
                        plainConsidered++;
                        if (equal) plainMatches++;
                    }
                }
            }

            if (considered < 3)
            {
                // A (nearly) plain area: compare all cells
                considered += plainConsidered;
                matches += plainMatches;
            }

            score = considered == 0 ? 0 : (double)matches / considered;
        }

        /// <summary>
        /// A row is unchanged when all its comparable cells are equal at the same position (and there is at least one)
        /// </summary>
        private static bool RowUnchanged(Grid a, Grid b, int row)
        {
            int start = row * a.Strips;
            int compared = 0;
            for (int c = 0; c < a.Strips; c++)
            {
                int cell = start + c;
                if (a.Animated[cell] || b.Animated[cell]) continue;
                if (a.Hash[cell] != b.Hash[cell]) return false;
                compared++;
            }

            return compared > 0;
        }

        /// <summary>
        /// Strips which are identical at the same position in almost all rows and are not a plain color are fixed elements
        /// (sidebars, floating buttons) - they are left out of the matching
        /// </summary>
        private bool[] MovingStrips(Grid previous, Grid current, int top, int bottom)
        {
            int strips = previous.Strips;
            var use = new bool[strips];
            int remaining = 0;
            for (int c = 0; c < strips; c++)
            {
                int comparable = 0;
                int same = 0;
                bool uniform = true;
                ulong first = previous.Hash[top * strips + c];
                for (int y = top; y < bottom; y++)
                {
                    int cell = y * strips + c;
                    if (previous.Hash[cell] != first) uniform = false;
                    if (previous.Animated[cell] || current.Animated[cell]) continue;
                    comparable++;
                    if (previous.Hash[cell] == current.Hash[cell]) same++;
                }

                bool isStatic = !uniform && comparable > (bottom - top) / 2 && same >= comparable * _options.StaticColumnFraction;
                use[c] = !isStatic;
                if (use[c]) remaining++;
            }

            if (remaining >= Math.Max(2, strips / 10)) return use;

            // When almost everything looks static, don't trust the detection
            for (int c = 0; c < strips; c++) use[c] = true;
            return use;
        }

        private static int CountComparableCells(Grid grid, int top, int bottom, bool[] useStrips)
        {
            int count = 0;
            for (int y = top; y < bottom; y++)
            {
                int row = y * grid.Strips;
                for (int c = 0; c < grid.Strips; c++)
                {
                    int cell = row + c;
                    if (useStrips[c] && grid.Informative[cell] && !grid.Animated[cell]) count++;
                }
            }

            return count;
        }

        private static int MinCells(int strips, int rows) => Math.Max(12, strips * rows / 200);

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

        /// <summary>
        /// No clear match: take the best shift up to a bit more than the expected scroll distance, if it matches at least a little,
        /// otherwise the expected distance itself. Never more often in a row than allowed (then something is really wrong).
        /// </summary>
        private bool TryEstimate(double[] scores, int expectedShift, int maxShift, out int shift, out double score)
        {
            shift = 0;
            score = 0;
            if (expectedShift <= 0 || _consecutiveEstimates >= _options.MaxConsecutiveEstimates) return false;

            int upper = Math.Min(maxShift, (int)(expectedShift * 1.25) + 2);
            for (int candidate = 1; candidate <= upper; candidate++)
            {
                if (scores[candidate] <= score) continue;
                score = scores[candidate];
                shift = candidate;
            }

            if (score >= _options.MinEstimateScore) return true;
            if (expectedShift > maxShift) return false;

            // Nothing matches at all (e.g. the whole overlap is a video): trust the scroll distance
            shift = expectedShift;
            score = scores[expectedShift];
            return true;
        }

        private bool IsPreferredShift(int candidate, int current)
        {
            if (_lastShift <= 0) return candidate < current;
            return Math.Abs(candidate - _lastShift) < Math.Abs(current - _lastShift);
        }
    }
}
