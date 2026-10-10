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
using System.Drawing;
using HUScroll.Core.Native;

namespace HUScroll.Core.Capture
{
    /// <summary>
    /// A window (or a child window, e.g. the page area of a browser) under the cursor
    /// </summary>
    public sealed class WindowHit
    {
        public WindowHit(IntPtr topLevel, IntPtr handle, Rectangle bounds, string className)
        {
            TopLevel = topLevel;
            Handle = handle;
            Bounds = bounds;
            ClassName = className;
        }

        /// <summary>The top level window</summary>
        public IntPtr TopLevel { get; }

        /// <summary>The window which was hit, can be a child of <see cref="TopLevel"/></summary>
        public IntPtr Handle { get; }

        /// <summary>Bounds in screen coordinates</summary>
        public Rectangle Bounds { get; }

        public string ClassName { get; }
    }

    /// <summary>
    /// Finds the window under a point. Takes a snapshot of the top level windows (Z order) when created,
    /// so the selection overlay itself (created later) doesn't get in the way.
    /// </summary>
    public sealed class WindowFinder
    {
        /// <summary>
        /// Child windows smaller than this are not interesting for a scroll capture (buttons, toolbars)
        /// </summary>
        private static readonly Size MinChildSize = new Size(160, 120);

        private readonly List<KeyValuePair<IntPtr, Rectangle>> _topLevelWindows = new List<KeyValuePair<IntPtr, Rectangle>>();

        public WindowFinder(IEnumerable<IntPtr> ignore = null)
        {
            var ignored = new HashSet<IntPtr>(ignore ?? Array.Empty<IntPtr>());
            NativeMethods.EnumWindows((hWnd, _) =>
            {
                if (ignored.Contains(hWnd) || !NativeMethods.IsWindowVisible(hWnd) || NativeMethods.IsIconic(hWnd) || NativeMethods.IsCloaked(hWnd))
                {
                    return true;
                }

                long exStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);
                // Click-through overlays (e.g. screen annotation tools) are not what the user points at
                if ((exStyle & NativeMethods.WS_EX_TRANSPARENT) != 0 && (exStyle & NativeMethods.WS_EX_LAYERED) != 0)
                {
                    return true;
                }

                var bounds = NativeMethods.GetVisibleBounds(hWnd);
                if (bounds.Width > 1 && bounds.Height > 1)
                {
                    _topLevelWindows.Add(new KeyValuePair<IntPtr, Rectangle>(hWnd, bounds));
                }

                return true;
            }, IntPtr.Zero);
        }

        /// <summary>
        /// Find the window at the screen point.
        /// </summary>
        /// <param name="screenPoint">Point in screen coordinates</param>
        /// <param name="preferContent">true: go down to the deepest child which is big enough (e.g. the page of a browser), false: the top level window</param>
        public WindowHit Find(Point screenPoint, bool preferContent)
        {
            foreach (var entry in _topLevelWindows)
            {
                if (!entry.Value.Contains(screenPoint)) continue;

                var topLevel = entry.Key;
                if (!preferContent)
                {
                    return new WindowHit(topLevel, topLevel, entry.Value, NativeMethods.GetClassName(topLevel));
                }

                var current = topLevel;
                var bounds = entry.Value;
                for (int depth = 0; depth < 12; depth++)
                {
                    var clientPoint = new NativeMethods.POINT(screenPoint.X, screenPoint.Y);
                    if (!NativeMethods.ScreenToClient(current, ref clientPoint)) break;
                    var child = NativeMethods.ChildWindowFromPointEx(current, clientPoint,
                        NativeMethods.CWP_SKIPINVISIBLE | NativeMethods.CWP_SKIPDISABLED | NativeMethods.CWP_SKIPTRANSPARENT);
                    if (child == IntPtr.Zero || child == current) break;
                    if (!NativeMethods.GetWindowRect(child, out var childRect)) break;
                    var childBounds = Rectangle.Intersect(childRect.ToRectangle(), bounds);
                    if (childBounds.Width < MinChildSize.Width || childBounds.Height < MinChildSize.Height) break;
                    current = child;
                    bounds = childBounds;
                }

                return new WindowHit(topLevel, current, bounds, NativeMethods.GetClassName(current));
            }

            return null;
        }
    }
}
