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
using System.Drawing;
using System.Runtime.InteropServices;
using HUScroll.Core.Native;

namespace HUScroll.Core.Capture
{
    /// <summary>
    /// Scrolls the content under a point with simulated mouse wheel input (SendInput),
    /// which works for browsers, Office, Explorer etc. without knowing the application.
    /// </summary>
    public sealed class ScrollDriver
    {
        private readonly Point _scrollPoint;
        private readonly Point _parkPoint;

        /// <param name="area">The captured area, the wheel is sent to its center</param>
        /// <param name="parkPoint">Where the cursor waits between the scroll steps (outside of the area, so no hover effects are captured)</param>
        public ScrollDriver(Rectangle area, Point parkPoint)
        {
            _scrollPoint = new Point(area.Left + area.Width / 2, area.Top + area.Height / 2);
            _parkPoint = parkPoint;
        }

        /// <summary>
        /// Scroll down by a number of wheel notches
        /// </summary>
        /// <returns>false when the input was blocked (e.g. the target runs elevated, UIPI)</returns>
        public bool ScrollDown(int notches)
        {
            if (notches < 1) notches = 1;
            NativeMethods.SetCursorPos(_scrollPoint.X, _scrollPoint.Y);

            var inputs = new NativeMethods.INPUT[notches];
            for (int i = 0; i < notches; i++)
            {
                inputs[i].type = NativeMethods.INPUT_MOUSE;
                inputs[i].u.mi = new NativeMethods.MOUSEINPUT
                {
                    mouseData = -NativeMethods.WHEEL_DELTA,
                    dwFlags = NativeMethods.MOUSEEVENTF_WHEEL
                };
            }

            uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
            Park();
            return sent == inputs.Length;
        }

        /// <summary>
        /// Move the cursor out of the captured area
        /// </summary>
        public void Park() => NativeMethods.SetCursorPos(_parkPoint.X, _parkPoint.Y);

        /// <summary>
        /// A point near the area, but outside of it, on a screen
        /// </summary>
        public static Point FindParkPoint(Rectangle area, Rectangle virtualScreen, Point fallback)
        {
            var candidates = new[]
            {
                new Point(area.Right + 8, area.Top + area.Height / 2),
                new Point(area.Left - 8, area.Top + area.Height / 2),
                new Point(area.Left + area.Width / 2, area.Bottom + 8),
                new Point(area.Left + area.Width / 2, area.Top - 8)
            };
            foreach (var candidate in candidates)
            {
                if (virtualScreen.Contains(candidate) && !area.Contains(candidate)) return candidate;
            }

            // The area covers the whole screen: use a corner, the cursor isn't captured anyway, only hover effects are a risk
            return area.Contains(fallback) ? new Point(area.Right - 1, area.Bottom - 1) : fallback;
        }

        public static Point GetCursorPosition()
        {
            return NativeMethods.GetCursorPos(out var point) ? new Point(point.X, point.Y) : Point.Empty;
        }

        public static void SetCursorPosition(Point point) => NativeMethods.SetCursorPos(point.X, point.Y);
    }
}
