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
        public ScrollCaptureResult(Bitmap image, int frames, string stopReason, Rectangle area)
        {
            Image = image;
            Frames = frames;
            StopReason = stopReason;
            Area = area;
        }

        /// <summary>The stitched image, the caller owns it</summary>
        public Bitmap Image { get; }

        public int Frames { get; }

        /// <summary>Why the capture ended (for the user)</summary>
        public string StopReason { get; }

        /// <summary>The captured screen area</summary>
        public Rectangle Area { get; }
    }

    /// <summary>
    /// One scroll capture, Samsung style: select the window or area, then it scrolls by itself and stitches the frames,
    /// until the end of the content, the maximum height, or Esc / "Fertig".
    /// Runs on the UI thread (async, the UI stays responsive), host independent: the Greenshot plugins only pass the image on.
    /// </summary>
    public sealed class ScrollCaptureSession
    {
        private const int PollIntervalMs = 25;

        private readonly ScrollCaptureOptions _options;
        private readonly Action<string> _log;
        private CaptureStatusForm _status;

        public ScrollCaptureSession(ScrollCaptureOptions options = null, Action<string> log = null)
        {
            _options = options ?? new ScrollCaptureOptions();
            _log = log ?? (_ => { });
        }

        /// <summary>
        /// Run the capture
        /// </summary>
        /// <returns>The result, or null when the user cancelled the selection</returns>
        public async Task<ScrollCaptureResult> RunAsync(IWin32Window owner = null)
        {
            await Task.Delay(_options.StartDelayMs);

            var virtualScreen = SystemInformation.VirtualScreen;
            Rectangle area;
            IntPtr topLevel;
            using (var screenshot = ScreenGrabber.CaptureBitmap(virtualScreen))
            {
                var finder = new WindowFinder();
                using (var selector = new RegionSelectorForm(screenshot, virtualScreen, finder))
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

            _log($"Selected area {area}");
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
            try
            {
                _status.ApplyDpiScale();
                if (_status.PlaceOutside(area))
                {
                    _status.Show();
                }

                driver.Park();
                await WaitForEscapeReleasedAsync();
                if (await DelayAsync(_options.AfterSelectionDelayMs))
                {
                    return null;
                }

                var frame = await GrabSettledAsync(area);
                if (frame == null) return null;
                engine.Add(frame);

                string reason = await ScrollLoopAsync(area, driver, engine);
                _log($"Finished: {reason}, {engine.FrameCount} frames, {engine.Width}x{engine.ResultHeight}");

                var composed = engine.Compose();
                return new ScrollCaptureResult(ScreenGrabber.ToBitmap(composed), engine.FrameCount, reason, area);
            }
            finally
            {
                _status.Close();
                _status.Dispose();
                _status = null;
                ScrollDriver.SetCursorPosition(originalCursor);
            }
        }

        private async Task<string> ScrollLoopAsync(Rectangle area, ScrollDriver driver, StitchEngine engine)
        {
            int notches = Math.Max(1, _options.InitialWheelNotches);
            int noMovement = 0;
            for (int i = 1; i < _options.MaxFrames; i++)
            {
                SetStatus($"● Aufnahme läuft …  {engine.FrameCount} Bilder · {engine.ResultHeight} px");
                if (StopRequested()) return "Vom Benutzer beendet";

                if (!driver.ScrollDown(notches))
                {
                    return "Scrollen wurde blockiert (läuft das Programm als Administrator?)";
                }

                if (await DelayAsync(_options.SettleDelayMs)) return "Vom Benutzer beendet";
                var frame = await GrabSettledAsync(area);
                if (frame == null) return "Vom Benutzer beendet";

                var result = engine.Add(frame);
                _log($"Frame {i}: {result} (notches={notches})");
                switch (result.Status)
                {
                    case StitchStatus.Appended:
                        noMovement = 0;
                        notches = NextNotches(engine, frame.Height, result.Shift, notches);
                        break;
                    case StitchStatus.LimitReached:
                        return $"Maximale Höhe erreicht ({_options.Stitch.MaxOutputHeight} px)";
                    case StitchStatus.NoMovement:
                        noMovement++;
                        if (noMovement > _options.NoMovementRetries)
                        {
                            return engine.FrameCount <= 1
                                ? "Der Inhalt hat sich nicht bewegt (nicht scrollbar, oder Mauszeiger über einem anderen Element)"
                                : "Ende erreicht";
                        }
                        break;
                    case StitchStatus.NoMatch:
                        return "Bilder passten nicht zusammen (Inhalt hat sich verändert) – Ergebnis bis hierher";
                }
            }

            return $"Maximale Anzahl Bilder erreicht ({_options.MaxFrames})";
        }

        /// <summary>
        /// Adapt the wheel notches so one step scrolls about TargetScrollFraction of the scrolling area
        /// </summary>
        private int NextNotches(StitchEngine engine, int frameHeight, int shift, int notches)
        {
            if (shift <= 0) return notches;
            double pixelsPerNotch = (double)shift / notches;
            int scrollArea = Math.Max(1, frameHeight - engine.Header - engine.Footer);
            double target = scrollArea * _options.TargetScrollFraction;
            int next = (int)Math.Floor(target / pixelsPerNotch);
            return Math.Max(1, Math.Min(_options.MaxWheelNotches, next));
        }

        /// <summary>
        /// Grab until two grabs are identical (smooth scrolling, animations), at most MaxStableChecks times
        /// </summary>
        /// <returns>The frame, null when the user stopped</returns>
        private async Task<FrameData> GrabSettledAsync(Rectangle area)
        {
            var last = ScreenGrabber.Capture(area);
            for (int i = 0; i < _options.MaxStableChecks; i++)
            {
                if (await DelayAsync(_options.StableCheckDelayMs)) return null;
                var current = ScreenGrabber.Capture(area);
                if (current.PixelEquals(last)) return current;
                last = current;
            }

            _log("Frame did not settle, using the last grab");
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
