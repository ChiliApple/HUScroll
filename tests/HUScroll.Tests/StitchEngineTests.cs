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
    }
}
