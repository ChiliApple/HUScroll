using HUScroll.Core.Stitching;

namespace HUScroll.Core
{
    /// <summary>
    /// Settings of a scroll capture
    /// </summary>
    public sealed class ScrollCaptureOptions
    {
        /// <summary>Wait before the overlay is shown, so the tray menu has closed (ms)</summary>
        public int StartDelayMs { get; set; } = 300;

        /// <summary>Wait after the selection, before the first frame (ms)</summary>
        public int AfterSelectionDelayMs { get; set; } = 250;

        /// <summary>Wait after a scroll step before the frame is grabbed (smooth scrolling, lazy loading) (ms)</summary>
        public int SettleDelayMs { get; set; } = 350;

        /// <summary>Interval between two grabs which must be identical before a frame counts as "settled" (ms)</summary>
        public int StableCheckDelayMs { get; set; } = 120;

        /// <summary>Maximum number of grabs to wait for a settled frame</summary>
        public int MaxStableChecks { get; set; } = 6;

        /// <summary>Wheel notches for the first scroll step (the scroll distance per notch is measured with it)</summary>
        public int InitialWheelNotches { get; set; } = 2;

        /// <summary>Maximum wheel notches per step</summary>
        public int MaxWheelNotches { get; set; } = 25;

        /// <summary>Which part of the scrolling area is scrolled per step (the rest is the overlap used for the matching)</summary>
        public double TargetScrollFraction { get; set; } = 0.6;

        /// <summary>Maximum number of frames</summary>
        public int MaxFrames { get; set; } = 120;

        /// <summary>How often the same "no movement" result may occur before the end of the page is assumed</summary>
        public int NoMovementRetries { get; set; } = 1;

        /// <summary>Stitching settings (including the maximum height)</summary>
        public StitchOptions Stitch { get; set; } = new StitchOptions();
    }
}
