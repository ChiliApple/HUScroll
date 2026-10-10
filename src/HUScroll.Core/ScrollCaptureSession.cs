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
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using HUScroll.Core.Capture;
using HUScroll.Core.Native;
using HUScroll.Core.Stitching;
using HUScroll.Core.UI;

namespace HUScroll.Core
{
    /// <summary>
    /// The result of a scroll capture
    /// </summary>
    public sealed class ScrollCaptureResult
    {
        public ScrollCaptureResult(Bitmap image, int frames, string stopReason, Rectangle area, bool isNormalEnd)
        {
            Image = image;
            Frames = frames;
            StopReason = stopReason;
            Area = area;
            IsNormalEnd = isNormalEnd;
        }

        /// <summary>
        /// True when the capture ended as expected (end of the content, or stopped by the user):
        /// nothing to tell the user then, the editor opening is feedback enough
        /// </summary>
        public bool IsNormalEnd { get; }

        /// <summary>The stitched image, the caller owns it</summary>
        public Bitmap Image { get; }

        public int Frames { get; }

        /// <summary>Why the capture ended (for the user)</summary>
        public string StopReason { get; }

        /// <summary>The captured screen area</summary>
        public Rectangle Area { get; }
    }

    /// <summary>
    /// How the content is scrolled
    /// </summary>
    public enum ScrollCaptureMode
    {
        /// <summary>HUScroll scrolls by itself (mouse wheel input) until the end of the content</summary>
        Automatic,
        /// <summary>The user scrolls, HUScroll captures every settled position (scrolling back up is allowed)</summary>
        Manual
    }

    /// <summary>
    /// One scroll capture: select the window or area, then the content is scrolled (by HUScroll or by the user)
    /// and the frames are stitched, until the end of the content, the maximum height, or Esc / "Fertig".
    /// Runs on the UI thread (async, the UI stays responsive), host independent: the Greenshot plugins only pass the image on.
    /// </summary>
    public sealed class ScrollCaptureSession
    {
        private const int PollIntervalMs = 25;
        internal const string ReasonEnd = "Ende erreicht";
        internal const string ReasonUser = "Vom Benutzer beendet";

        private readonly ScrollCaptureOptions _options;
        private readonly Action<string> _log;
        private static readonly Color FrameColor = Color.FromArgb(0, 170, 255);
        private static readonly Color FrameErrorColor = Color.FromArgb(235, 50, 50);

        private CaptureStatusForm _status;
        private SelectionFrameForm _frame;
        private double _maxPixelsPerNotch;

        public ScrollCaptureSession(ScrollCaptureOptions options = null, Action<string> log = null)
        {
            _options = options ?? new ScrollCaptureOptions();
            _log = log ?? (_ => { });
        }

        /// <summary>
        /// Run the capture
        /// </summary>
        /// <returns>The result, or null when the user cancelled the selection</returns>
        public async Task<ScrollCaptureResult> RunAsync(ScrollCaptureMode mode = ScrollCaptureMode.Automatic, IWin32Window owner = null)
        {
            bool manual = mode == ScrollCaptureMode.Manual;
            await Task.Delay(_options.StartDelayMs);

            var virtualScreen = SystemInformation.VirtualScreen;
            Rectangle area;
            IntPtr topLevel;
            using (var screenshot = ScreenGrabber.CaptureBitmap(virtualScreen))
            {
                var finder = new WindowFinder();
                using (var selector = new RegionSelectorForm(screenshot, virtualScreen, finder, manual))
                {
                    var dialogResult = owner == null ? selector.ShowDialog() : selector.ShowDialog(owner);
                    if (dialogResult != DialogResult.OK)
                    {
                        _log("Selection cancelled");
                        return null;
                    }

                    area = Rectangle.Intersect(selector.SelectedArea, virtualScreen);
                    topLevel = selector.SelectedWindow;
                }
            }

            if (area.Width < 50 || area.Height < 50)
            {
                _log($"Selection too small: {area}");
                return null;
            }

            _log($"Selected area {area}, mode {mode}");
            var originalCursor = ScrollDriver.GetCursorPosition();
            var center = new NativeMethods.POINT(area.Left + area.Width / 2, area.Top + area.Height / 2);
            if (topLevel == IntPtr.Zero)
            {
                topLevel = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(center), NativeMethods.GA_ROOT);
            }

            if (topLevel != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(topLevel);
            }

            var driver = new ScrollDriver(area, ScrollDriver.FindParkPoint(area, virtualScreen, originalCursor));
            var engine = new StitchEngine(_options.Stitch);

            _status = new CaptureStatusForm();
            _frame = new SelectionFrameForm(area, FrameColor);
            try
            {
                _status.ApplyDpiScale();
                if (_status.PlaceOutside(area))
                {
                    _status.Show();
                }
                _frame.Show();

                if (!manual)
                {
                    driver.Park();
                }
                await WaitForEscapeReleasedAsync();
                if (await DelayAsync(_options.AfterSelectionDelayMs))
                {
                    return null;
                }

                var frame = await GrabSettledAsync(area);
                if (frame == null) return null;
                engine.Add(frame);

                string reason = manual
                    ? await ManualLoopAsync(area, engine, frame)
                    : await ScrollLoopAsync(area, driver, engine);
                _log($"Finished: {reason}, {engine.FrameCount} frames, {engine.Width}x{engine.ResultHeight}");

                var composed = engine.Compose();
                if (engine.EstimatedCount > 0)
                {
                    reason += $" – {engine.EstimatedCount} Übergang/Übergänge geschätzt (bewegter Inhalt), bitte prüfen";
                }
                bool isNormalEnd = (reason == ReasonEnd || reason == ReasonUser) && engine.EstimatedCount == 0;
                return new ScrollCaptureResult(ScreenGrabber.ToBitmap(composed), engine.FrameCount, reason, area, isNormalEnd);
            }
            finally
            {
                _frame.Close();
                _frame.Dispose();
                _frame = null;
                _status.Close();
                _status.Dispose();
                _status = null;
                if (!manual)
                {
                    ScrollDriver.SetCursorPosition(originalCursor);
                }
            }
        }

        /// <summary>
        /// Manual mode: the user scrolls, every settled position which differs from the last one is offered to the stitching.
        /// Scrolling back up is ignored (the deepest frame stays the reference), a gap (scrolled too far at once) turns the frame red.
        /// </summary>
        private async Task<string> ManualLoopAsync(Rectangle area, StitchEngine engine, FrameData first)
        {
            var lastGrab = first;
            var lastProcessed = first;
            var detector = new AnimationDetector(area.Width, area.Height);
            string hint = "Mit dem Mausrad scrollen";
            while (true)
            {
                SetStatus($"● {hint}  ·  {engine.FrameCount} Bilder · {engine.ResultHeight} px");
                if (await DelayAsync(_options.ManualPollIntervalMs)) return ReasonUser;

                var grab = ScreenGrabber.Capture(area);
                // Calm = same scroll position as the grab before: identical, or only an animation (GIF, video) changed
                bool calm = grab.PixelEquals(lastGrab) || detector.Observe(lastGrab, grab);
                if (!calm) detector.Reset();
                lastGrab = grab;
                if (!calm) continue;

                // Nothing new since the last processed frame (apart from animations)
                if (new AnimationDetector(area.Width, area.Height).Observe(lastProcessed, grab)) continue;
                grab.Animated = detector.GetUsableMask();
                lastProcessed = grab;

                var result = engine.Add(grab);
                _log($"Manual frame: {result}");
                switch (result.Status)
                {
                    case StitchStatus.Appended:
                        _frame.SetColor(FrameColor);
                        hint = "Weiter scrollen – Esc / Fertig zum Beenden";
                        break;
                    case StitchStatus.LimitReached:
                        return $"Maximale Höhe erreicht ({_options.Stitch.MaxOutputHeight} px)";
                    case StitchStatus.ScrolledBack:
                        _frame.SetColor(FrameColor);
                        hint = "Zurückgescrollt – weiter unten geht's weiter";
                        break;
                    case StitchStatus.NoMovement:
                        break;
                    case StitchStatus.NoMatch:
                        _frame.SetColor(FrameErrorColor);
                        hint = "Kein Anschluss – etwas zurückscrollen";
                        break;
                }

                if (engine.FrameCount >= _options.MaxFrames)
                {
                    return $"Maximale Anzahl Bilder erreicht ({_options.MaxFrames})";
                }
            }
        }

        private async Task<string> ScrollLoopAsync(Rectangle area, ScrollDriver driver, StitchEngine engine)
        {
            int notches = Math.Max(1, _options.InitialWheelNotches);
            int noMovement = 0;
            for (int i = 1; i < _options.MaxFrames; i++)
            {
                SetStatus($"● Aufnahme läuft …  {engine.FrameCount} Bilder · {engine.ResultHeight} px");
                if (StopRequested()) return ReasonUser;

                if (!driver.ScrollDown(notches))
                {
                    return "Scrollen wurde blockiert (läuft das Programm als Administrator?)";
                }

                if (await DelayAsync(_options.SettleDelayMs)) return ReasonUser;
                var frame = await GrabSettledAsync(area);
                if (frame == null) return ReasonUser;

                // Before the first measurement: browsers scroll about 100 px (at 100 %) per notch - only used when nothing can be matched
                double pixelsPerNotch = _maxPixelsPerNotch > 0 ? _maxPixelsPerNotch : GuessPixelsPerNotch();
                int expectedShift = (int)(pixelsPerNotch * notches);
                var result = engine.Add(frame, expectedShift);
                _log($"Frame {i}: {result} (notches={notches}, expected={expectedShift}{(frame.Animated != null ? ", animation masked" : "")})");
                switch (result.Status)
                {
                    case StitchStatus.Appended:
                        noMovement = 0;
                        // An estimated shift says nothing about the scroll distance per notch
                        if (!result.Estimated)
                        {
                            notches = NextNotches(engine, frame.Height, result.Shift, notches);
                        }
                        break;
                    case StitchStatus.LimitReached:
                        return $"Maximale Höhe erreicht ({_options.Stitch.MaxOutputHeight} px)";
                    case StitchStatus.NoMovement:
                    case StitchStatus.ScrolledBack:
                        noMovement++;
                        if (noMovement > _options.NoMovementRetries)
                        {
                            return engine.FrameCount <= 1
                                ? "Der Inhalt hat sich nicht bewegt (nicht scrollbar, oder Mauszeiger über einem anderen Element)"
                                : ReasonEnd;
                        }
                        break;
                    case StitchStatus.NoMatch:
                        return "Bilder passten nicht zusammen (Inhalt hat sich verändert) – Ergebnis bis hierher";
                }
            }

            return $"Maximale Anzahl Bilder erreicht ({_options.MaxFrames})";
        }

        private static double GuessPixelsPerNotch()
        {
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    return 100.0 * g.DpiY / 96.0;
                }
            }
            catch (Exception)
            {
                return 100.0;
            }
        }

        /// <summary>
        /// Adapt the wheel notches so one step scrolls about TargetScrollFraction of the scrolling area
        /// </summary>
        private int NextNotches(StitchEngine engine, int frameHeight, int shift, int notches)
        {
            if (shift <= 0) return notches;
            // The largest measured distance per notch counts: a smaller shift usually means the end of the page was reached
            // (the content couldn't move further), it must not lead to a huge jump with many notches
            _maxPixelsPerNotch = Math.Max(_maxPixelsPerNotch, (double)shift / notches);
            double pixelsPerNotch = _maxPixelsPerNotch;
            int scrollArea = Math.Max(1, frameHeight - engine.Header - engine.Footer);
            double target = scrollArea * _options.TargetScrollFraction;
            int next = (int)Math.Floor(target / pixelsPerNotch);
            return Math.Max(1, Math.Min(_options.MaxWheelNotches, next));
        }

        /// <summary>
        /// Grab until the position is settled: two identical grabs, or two grabs in a row where only an animation changed
        /// (GIF, video, carousel - those pixels are attached as <see cref="FrameData.Animated"/> and ignored for the matching).
        /// At most MaxStableChecks times.
        /// </summary>
        /// <returns>The frame, null when the user stopped</returns>
        private async Task<FrameData> GrabSettledAsync(Rectangle area)
        {
            var detector = new AnimationDetector(area.Width, area.Height);
            var last = ScreenGrabber.Capture(area);
            int animatedCalm = 0;
            for (int i = 0; i < _options.MaxStableChecks; i++)
            {
                if (await DelayAsync(_options.StableCheckDelayMs)) return null;
                var current = ScreenGrabber.Capture(area);
                if (current.PixelEquals(last))
                {
                    current.Animated = detector.GetUsableMask();
                    return current;
                }

                if (detector.Observe(last, current))
                {
                    animatedCalm++;
                    if (animatedCalm >= 2)
                    {
                        current.Animated = detector.GetUsableMask();
                        return current;
                    }
                }
                else
                {
                    // still scrolling
                    detector.Reset();
                    animatedCalm = 0;
                }

                last = current;
            }

            _log("Frame did not settle, using the last grab");
            last.Animated = detector.GetUsableMask();
            return last;
        }

        /// <returns>true when the user wants to stop</returns>
        private async Task<bool> DelayAsync(int milliseconds)
        {
            var end = DateTime.UtcNow.AddMilliseconds(milliseconds);
            do
            {
                if (StopRequested()) return true;
                await Task.Delay(PollIntervalMs);
            }
            while (DateTime.UtcNow < end);

            return StopRequested();
        }

        private static async Task WaitForEscapeReleasedAsync()
        {
            for (int i = 0; i < 80 && NativeMethods.IsKeyDown(NativeMethods.VK_ESCAPE); i++)
            {
                await Task.Delay(PollIntervalMs);
            }
        }

        private bool StopRequested() =>
            NativeMethods.IsKeyDown(NativeMethods.VK_ESCAPE) || (_status != null && _status.StopRequested);

        private void SetStatus(string text) => _status?.SetStatus(text);
    }
}
