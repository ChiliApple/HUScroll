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
using HUScroll.Core.Stitching;
using Xunit;

namespace HUScroll.Tests
{
    public class StitchEngineTests
    {
        private const int Width = 320;

        /// <summary>
        /// A synthetic "web page": rows with random content, some plain rows (white space) in between
        /// </summary>
        private static int[] CreatePage(int rows, int seed, int plainEvery = 7)
        {
            var random = new Random(seed);
            var page = new int[Width * rows];
            for (int y = 0; y < rows; y++)
            {
                bool plain = plainEvery > 0 && y % plainEvery == 0;
                for (int x = 0; x < Width; x++)
                {
                    page[y * Width + x] = plain ? unchecked((int)0xFFFFFFFF) : unchecked((int)0xFF000000) | random.Next(0, 0xFFFFFF);
                }
            }
            return page;
        }

        /// <summary>
        /// Render a viewport: fixed header, page content at scroll offset, fixed footer,
        /// optional scrollbar at the right which moves, optional fixed sidebar at the left
        /// </summary>
        private static FrameData Viewport(int[] page, int pageRows, int offset, int contentRows, int header, int footer, bool scrollbar, int sidebar)
        {
            int height = header + contentRows + footer;
            var pixels = new int[Width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int value;
                    if (y < header)
                    {
                        value = unchecked((int)0xFF203040) + ((x * 7 + y * 3) % 50);
                    }
                    else if (y >= header + contentRows)
                    {
                        value = unchecked((int)0xFF405060) + ((x * 5 + y) % 40);
                    }
                    else if (sidebar > 0 && x < sidebar)
                    {
                        value = unchecked((int)0xFF102010) + ((x * 13 + y * 11) % 90);
                    }
                    else
                    {
                        value = page[(offset + y - header) * Width + x];
                    }
                    pixels[y * Width + x] = value;
                }
            }

            if (scrollbar)
            {
                int thumbTop = header + (int)((long)offset * contentRows / pageRows);
                int thumbHeight = Math.Max(10, contentRows * contentRows / pageRows);
                for (int y = header; y < header + contentRows; y++)
                {
                    bool thumb = y >= thumbTop && y < thumbTop + thumbHeight;
                    for (int x = Width - 16; x < Width; x++)
                    {
                        pixels[y * Width + x] = thumb ? unchecked((int)0xFF808080) : unchecked((int)0xFFE0E0E0);
                    }
                }
            }

            return new FrameData(Width, height, pixels);
        }

        private static void AssertRowsEqual(FrameData expected, FrameData actual)
        {
            Assert.Equal(expected.Width, actual.Width);
            Assert.Equal(expected.Height, actual.Height);
            for (int y = 0; y < expected.Height; y++)
            {
                for (int x = 0; x < expected.Width; x++)
                {
                    if (expected[x, y] != actual[x, y])
                    {
                        Assert.Fail($"Pixel mismatch at x={x}, y={y}");
                    }
                }
            }
        }

        [Fact]
        public void PlainScroll_ResultIsTheWholePage()
        {
            const int pageRows = 2000, viewRows = 400, step = 230;
            var page = CreatePage(pageRows, 1);
            var engine = new StitchEngine();
            Assert.Equal(StitchStatus.First, engine.Add(Viewport(page, pageRows, 0, viewRows, 0, 0, false, 0)).Status);

            int offset = 0;
            while (offset < pageRows - viewRows)
            {
                int next = Math.Min(offset + step, pageRows - viewRows);
                var result = engine.Add(Viewport(page, pageRows, next, viewRows, 0, 0, false, 0));
                Assert.Equal(StitchStatus.Appended, result.Status);
                Assert.Equal(next - offset, result.Shift);
                offset = next;
            }

            // At the end the content doesn't move anymore
            Assert.Equal(StitchStatus.NoMovement, engine.Add(Viewport(page, pageRows, offset, viewRows, 0, 0, false, 0)).Status);

            var composed = engine.Compose();
            AssertRowsEqual(new FrameData(Width, pageRows, page), composed);
        }

        [Fact]
        public void StickyHeaderFooterAndScrollbar_AreTakenOnce()
        {
            const int pageRows = 1800, viewRows = 500, header = 60, footer = 30, step = 280;
            var page = CreatePage(pageRows, 2);
            var engine = new StitchEngine();
            engine.Add(Viewport(page, pageRows, 0, viewRows, header, footer, true, 0));
            int offset = 0;
            while (offset < pageRows - viewRows)
            {
                int next = Math.Min(offset + step, pageRows - viewRows);
                var result = engine.Add(Viewport(page, pageRows, next, viewRows, header, footer, true, 0));
                Assert.Equal(StitchStatus.Appended, result.Status);
                Assert.Equal(next - offset, result.Shift);
                offset = next;
            }

            // White content rows can look "fixed" by coincidence, the result must be right anyway
            Assert.InRange(engine.Header, header, header + 2);
            Assert.InRange(engine.Footer, footer, footer + 2);
            var composed = engine.Compose();
            Assert.Equal(header + pageRows + footer, composed.Height);

            // Compare the content columns (scrollbar excluded, its thumb is from the last frame)
            var reference = Viewport(page, pageRows, 0, viewRows, header, footer, false, 0);
            for (int y = 0; y < header; y++)
            for (int x = 0; x < Width - 16; x++)
                Assert.Equal(reference[x, y], composed[x, y]);
            for (int y = 0; y < pageRows; y++)
            for (int x = 0; x < Width - 16; x++)
                Assert.Equal(page[y * Width + x], composed[x, header + y]);
        }

        [Fact]
        public void FixedSidebar_DoesNotBreakMatching()
        {
            const int pageRows = 1500, viewRows = 400, step = 200;
            var page = CreatePage(pageRows, 3);
            var engine = new StitchEngine();
            engine.Add(Viewport(page, pageRows, 0, viewRows, 0, 0, true, 40));
            int offset = 0;
            while (offset < pageRows - viewRows)
            {
                int next = Math.Min(offset + step, pageRows - viewRows);
                var result = engine.Add(Viewport(page, pageRows, next, viewRows, 0, 0, true, 40));
                Assert.Equal(StitchStatus.Appended, result.Status);
                Assert.Equal(next - offset, result.Shift);
                offset = next;
            }

            Assert.Equal(pageRows, engine.Compose().Height);
        }

        [Fact]
        public void UnrelatedFrame_IsNoMatch()
        {
            var engine = new StitchEngine();
            engine.Add(Viewport(CreatePage(600, 4), 600, 0, 400, 0, 0, false, 0));
            var result = engine.Add(Viewport(CreatePage(600, 5), 600, 0, 400, 0, 0, false, 0));
            Assert.Equal(StitchStatus.NoMatch, result.Status);
            Assert.Equal(400, engine.Compose().Height);
        }

        [Fact]
        public void MaxHeight_StopsWithLimitReached()
        {
            const int pageRows = 5000, viewRows = 400;
            var page = CreatePage(pageRows, 6);
            var engine = new StitchEngine(new StitchOptions { MaxOutputHeight = 1000 });
            engine.Add(Viewport(page, pageRows, 0, viewRows, 0, 0, false, 0));
            StitchStatus status = StitchStatus.Appended;
            int offset = 0;
            while (status == StitchStatus.Appended)
            {
                offset += 250;
                status = engine.Add(Viewport(page, pageRows, offset, viewRows, 0, 0, false, 0)).Status;
            }

            Assert.Equal(StitchStatus.LimitReached, status);
            Assert.True(engine.Compose().Height >= 1000);
        }

        [Fact]
        public void AnimationWithoutScroll_IsNoMovement()
        {
            var page = CreatePage(800, 7);
            var engine = new StitchEngine();
            engine.Add(Viewport(page, 800, 0, 400, 0, 0, false, 0));
            var animated = Viewport(page, 800, 0, 400, 0, 0, false, 0);
            // a small animated element (e.g. a spinner) changed
            for (int y = 100; y < 110; y++)
            for (int x = 50; x < 70; x++)
                animated.Pixels[y * Width + x] = unchecked((int)0xFFFF0000);
            Assert.Equal(StitchStatus.NoMovement, engine.Add(animated).Status);
        }

        [Fact]
        public void ChangedContentAndRenderNoise_StillMatches()
        {
            // Real browsers: lazy loaded images change a block of rows between two frames,
            // and anti-aliasing differs slightly after scrolling (low color bits)
            const int pageRows = 2400, viewRows = 600, step = 360;
            var page = CreatePage(pageRows, 8);
            var random = new Random(9);
            var engine = new StitchEngine();
            engine.Add(Viewport(page, pageRows, 0, viewRows, 0, 0, true, 0));
            int offset = 0;
            while (offset < pageRows - viewRows)
            {
                int next = Math.Min(offset + step, pageRows - viewRows);
                var frame = Viewport(page, pageRows, next, viewRows, 0, 0, true, 0);
                // a "loaded image": 1/8 of the rows replaced
                int imageTop = random.Next(0, viewRows - viewRows / 8);
                for (int y = imageTop; y < imageTop + viewRows / 8; y++)
                for (int x = 0; x < Width; x++)
                    frame.Pixels[y * Width + x] = unchecked((int)0xFF000000) | random.Next(0, 0xFFFFFF);
                // rendering noise in the lowest bits
                for (int i = 0; i < frame.Pixels.Length; i += 3)
                    frame.Pixels[i] ^= 0x00010101 * random.Next(0, 4);

                var result = engine.Add(frame);
                Assert.Equal(StitchStatus.Appended, result.Status);
                Assert.Equal(next - offset, result.Shift);
                offset = next;
            }

            Assert.Equal(pageRows, engine.Compose().Height);
        }

        [Fact]
        public void ScrollBackAndForth_ContinuesAtTheDeepestFrame()
        {
            // Manual mode: the user scrolls down, back up a bit, and further down again
            const int pageRows = 2000, viewRows = 500;
            var page = CreatePage(pageRows, 10);
            var engine = new StitchEngine();
            FrameData At(int offset) => Viewport(page, pageRows, offset, viewRows, 0, 0, true, 0);

            engine.Add(At(0));
            Assert.Equal(StitchStatus.Appended, engine.Add(At(200)).Status);
            Assert.Equal(StitchStatus.Appended, engine.Add(At(450)).Status);
            var back = engine.Add(At(300));
            Assert.Equal(StitchStatus.ScrolledBack, back.Status);
            Assert.Equal(-150, back.Shift);
            Assert.Equal(StitchStatus.ScrolledBack, engine.Add(At(100)).Status);
            // further down than before: continues from offset 450
            var forward = engine.Add(At(700));
            Assert.Equal(StitchStatus.Appended, forward.Status);
            Assert.Equal(250, forward.Shift);
            Assert.Equal(StitchStatus.Appended, engine.Add(At(1100)).Status);
            Assert.Equal(StitchStatus.Appended, engine.Add(At(1500)).Status);

            var composed = engine.Compose();
            Assert.Equal(1500 + viewRows, composed.Height);
            for (int y = 0; y < composed.Height; y++)
            for (int x = 0; x < Width - 24; x++)
                Assert.Equal(page[y * Width + x], composed[x, y]);
        }

        /// <summary>
        /// A "GIF": a box which moves with the content and shows a different random picture in every grab
        /// </summary>
        private static void PaintGif(FrameData frame, int boxTopInFrame, int boxHeight, Random random)
        {
            for (int y = Math.Max(0, boxTopInFrame); y < Math.Min(frame.Height, boxTopInFrame + boxHeight); y++)
            for (int x = 20; x < Width - 60; x++)
                frame.Pixels[y * Width + x] = unchecked((int)0xFF000000) | random.Next(0, 0xFFFFFF);
        }

        [Fact]
        public void AnimatedGif_IsMaskedAndMatches()
        {
            const int pageRows = 2400, viewRows = 600, step = 300, gifTop = 500, gifHeight = 260;
            var page = CreatePage(pageRows, 11);
            var random = new Random(12);
            var engine = new StitchEngine();

            FrameData GrabWithDetection(int offset)
            {
                // two grabs at the same position, the GIF shows another picture each time
                var first = Viewport(page, pageRows, offset, viewRows, 0, 0, false, 0);
                PaintGif(first, gifTop - offset, gifHeight, random);
                var second = Viewport(page, pageRows, offset, viewRows, 0, 0, false, 0);
                PaintGif(second, gifTop - offset, gifHeight, random);
                var detector = new AnimationDetector(Width, viewRows);
                Assert.True(detector.Observe(first, second));
                second.Animated = detector.GetUsableMask();
                return second;
            }

            engine.Add(GrabWithDetection(0));
            int offset = 0;
            while (offset < pageRows - viewRows)
            {
                int next = Math.Min(offset + step, pageRows - viewRows);
                var result = engine.Add(GrabWithDetection(next));
                Assert.Equal(StitchStatus.Appended, result.Status);
                Assert.False(result.Estimated);
                Assert.Equal(next - offset, result.Shift);
                offset = next;
            }

            Assert.Equal(pageRows, engine.Compose().Height);
        }

        [Fact]
        public void Scrolling_IsNotMistakenForAnimation()
        {
            var page = CreatePage(1200, 13);
            var detector = new AnimationDetector(Width, 400);
            Assert.False(detector.Observe(Viewport(page, 1200, 0, 400, 0, 0, false, 0), Viewport(page, 1200, 30, 400, 0, 0, false, 0)));
            Assert.True(detector.Observe(Viewport(page, 1200, 30, 400, 0, 0, false, 0), Viewport(page, 1200, 30, 400, 0, 0, false, 0)));
            Assert.Null(detector.Mask);
        }

        /// <summary>
        /// A page full of GIFs (like giphy.com): tiles cover most of the rows and most of the width,
        /// only the gaps between them and a title stay still
        /// </summary>
        private static FrameData GifGrid(int[] page, int pageRows, int offset, int viewRows, Random random)
        {
            var frame = Viewport(page, pageRows, offset, viewRows, 0, 0, false, 0);
            for (int tileTop = 40; tileTop < pageRows; tileTop += 120)
            {
                for (int tileLeft = 8; tileLeft + 70 < Width; tileLeft += 78)
                {
                    // like on giphy, some tiles are still images (or didn't change during the grabs)
                    if ((tileTop / 120 + tileLeft / 78) % 3 == 0) continue;
                    for (int y = tileTop; y < tileTop + 110; y++)
                    {
                        int fy = y - offset;
                        if (fy < 0 || fy >= viewRows) continue;
                        for (int x = tileLeft; x < tileLeft + 70; x++)
                            frame.Pixels[fy * Width + x] = unchecked((int)0xFF000000) | random.Next(0, 0xFFFFFF);
                    }
                }
            }
            return frame;
        }

        [Fact]
        public void PageFullOfGifs_AnimationIsNotMistakenForScrolling()
        {
            const int pageRows = 1600, viewRows = 500;
            var page = CreatePage(pageRows, 16);
            var random = new Random(17);
            var detector = new AnimationDetector(Width, viewRows);

            // same position, every GIF shows another picture: animation
            Assert.True(detector.Observe(GifGrid(page, pageRows, 200, viewRows, random), GifGrid(page, pageRows, 200, viewRows, random)));
            Assert.NotNull(detector.GetUsableMask());

            // scrolled by 60 rows, GIFs animated as well: scrolling
            detector.Reset();
            Assert.False(detector.Observe(GifGrid(page, pageRows, 200, viewRows, random), GifGrid(page, pageRows, 260, viewRows, random)));
        }

        [Fact]
        public void PageFullOfGifs_StitchesWithMask()
        {
            const int pageRows = 2400, viewRows = 600;
            var page = CreatePage(pageRows, 18);
            var random = new Random(19);
            var engine = new StitchEngine();

            FrameData Grab(int offset)
            {
                var detector = new AnimationDetector(Width, viewRows);
                var first = GifGrid(page, pageRows, offset, viewRows, random);
                var second = GifGrid(page, pageRows, offset, viewRows, random);
                Assert.True(detector.Observe(first, second));
                second.Animated = detector.GetUsableMask();
                return second;
            }

            engine.Add(Grab(0));
            int offset = 0;
            while (offset < pageRows - viewRows)
            {
                int next = Math.Min(offset + 300, pageRows - viewRows);
                var result = engine.Add(Grab(next));
                Assert.Equal(StitchStatus.Appended, result.Status);
                Assert.Equal(next - offset, result.Shift);
                offset = next;
            }
        }

        [Fact]
        public void HugeChange_IsEstimatedFromTheExpectedShift()
        {
            // A video fills most of the overlap: no clear match, but the expected scroll distance is known
            const int pageRows = 2000, viewRows = 500;
            var page = CreatePage(pageRows, 14);
            var random = new Random(15);
            var engine = new StitchEngine();
            engine.Add(Viewport(page, pageRows, 0, viewRows, 0, 0, false, 0));
            Assert.Equal(StitchStatus.Appended, engine.Add(Viewport(page, pageRows, 250, viewRows, 0, 0, false, 0)).Status);

            var noisy = Viewport(page, pageRows, 500, viewRows, 0, 0, false, 0);
            PaintGif(noisy, 0, 400, random); // 80 % of the frame changed, no mask (not detected)
            Assert.Equal(StitchStatus.NoMatch, new StitchEngineProbe(page, pageRows, viewRows).Add(noisy));

            var estimated = engine.Add(noisy, expectedShift: 250);
            Assert.Equal(StitchStatus.Appended, estimated.Status);
            Assert.True(estimated.Estimated);
            Assert.Equal(250, estimated.Shift);
            Assert.Equal(1, engine.EstimatedCount);

            // the next frame is clean again and matches normally
            var clean = engine.Add(Viewport(page, pageRows, 750, viewRows, 0, 0, false, 0), expectedShift: 250);
            Assert.Equal(StitchStatus.Appended, clean.Status);
            Assert.False(clean.Estimated);
        }

        /// <summary>Same two first frames, without an expected shift</summary>
        private sealed class StitchEngineProbe
        {
            private readonly StitchEngine _engine = new StitchEngine();

            public StitchEngineProbe(int[] page, int pageRows, int viewRows)
            {
                _engine.Add(Viewport(page, pageRows, 0, viewRows, 0, 0, false, 0));
                _engine.Add(Viewport(page, pageRows, 250, viewRows, 0, 0, false, 0));
            }

            public StitchStatus Add(FrameData frame) => _engine.Add(frame).Status;
        }

        /// <summary>
        /// Like bing.com: a video background behind everything (fixed on the screen, changes all the time),
        /// cards with content which scroll over it. Every grab sees another part of the video moving,
        /// so the animation masks of two frames are never the same.
        /// </summary>
        private static FrameData VideoBackground(int[] page, int pageRows, int offset, int viewRows, Random random, double movingFraction)
        {
            var frame = new FrameData(Width, viewRows, new int[Width * viewRows]);
            for (int y = 0; y < viewRows; y++)
            {
                int pageY = offset + y;
                bool cardRow = (pageY / 100) % 2 == 1; // cards every other 100 rows
                for (int x = 0; x < Width; x++)
                {
                    bool card = cardRow && x >= 40 && x < Width - 60;
                    frame.Pixels[y * Width + x] = card
                        ? page[pageY * Width + x]
                        : (random.NextDouble() < movingFraction ? unchecked((int)0xFF000000) | random.Next(0, 0xFFFFFF) : unchecked((int)0xFF203040));
                }
            }
            return frame;
        }

        [Fact]
        public void VideoBackground_DifferentMasksPerFrame_StillMatches()
        {
            const int pageRows = 2400, viewRows = 600;
            var page = CreatePage(pageRows, 20);
            var random = new Random(21);
            var engine = new StitchEngine();

            FrameData Grab(int offset)
            {
                var detector = new AnimationDetector(Width, viewRows);
                var first = VideoBackground(page, pageRows, offset, viewRows, random, 0.3);
                var second = VideoBackground(page, pageRows, offset, viewRows, random, 0.3);
                Assert.True(detector.Observe(first, second));
                second.Animated = detector.GetUsableMask();
                return second;
            }

            engine.Add(Grab(0));
            int offset = 0;
            while (offset < 1500)
            {
                int next = offset + 280;
                var result = engine.Add(Grab(next), expectedShift: 280);
                Assert.Equal(StitchStatus.Appended, result.Status);
                Assert.False(result.Estimated);
                Assert.Equal(280, result.Shift);
                offset = next;
            }
        }

        /// <summary>
        /// Sparse page (review finding H1): 12 px text lines every 48 px, white in between - scrolling changes only ~25 % of the rows
        /// </summary>
        private static int[] CreateSparsePage(int rows, int seed, int period)
        {
            var random = new Random(seed);
            var page = new int[Width * rows];
            for (int y = 0; y < rows; y++)
            {
                bool text = y % period < 12;
                for (int x = 0; x < Width; x++)
                    page[y * Width + x] = text && x > 20 && x < Width - 80 && random.Next(3) == 0
                        ? unchecked((int)0xFF000000) | random.Next(0, 0x404040)
                        : unchecked((int)0xFFFFFFFF);
            }
            return page;
        }

        [Theory]
        [InlineData(48, 20)]
        [InlineData(48, 100)]
        [InlineData(80, 60)]
        [InlineData(80, 120)]
        public void SparsePage_ScrollingIsNotMistakenForAnimation(int period, int step)
        {
            const int pageRows = 4000, viewRows = 600;
            var page = CreateSparsePage(pageRows, 22, period);
            var detector = new AnimationDetector(Width, viewRows);
            Assert.False(detector.Observe(Viewport(page, pageRows, 300, viewRows, 0, 0, false, 0), Viewport(page, pageRows, 300 + step, viewRows, 0, 0, false, 0)));
            Assert.Null(detector.Mask);
        }

        [Fact]
        public void SparsePage_ManualLoop_AppendsEverything()
        {
            // Simulation of the manual loop: grab, calm check with the detector, skip "nothing new", add
            const int pageRows = 3900, viewRows = 600;
            var page = CreateSparsePage(pageRows, 23, 48);
            var engine = new StitchEngine();
            var lastProcessed = Viewport(page, pageRows, 0, viewRows, 0, 0, false, 0);
            engine.Add(lastProcessed);
            for (int offset = 100; offset <= pageRows - viewRows; offset += 100)
            {
                var grab = Viewport(page, pageRows, offset, viewRows, 0, 0, false, 0);
                Assert.False(new AnimationDetector(Width, viewRows).Observe(lastProcessed, grab), $"offset {offset}: treated as nothing new");
                lastProcessed = grab;
                Assert.Equal(StitchStatus.Appended, engine.Add(grab).Status);
            }

            Assert.Equal(pageRows, engine.Compose().Height);
        }
    }
}
