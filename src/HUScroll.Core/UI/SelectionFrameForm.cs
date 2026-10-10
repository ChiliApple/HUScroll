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

using System.Drawing;
using System.Windows.Forms;
using HUScroll.Core.Native;

namespace HUScroll.Core.UI
{
    /// <summary>
    /// A glowing border around the captured area for the manual mode.
    /// Click-through (WS_EX_TRANSPARENT), never activated, only the border is visible (color key),
    /// and the border lies outside of the area, so it is never part of a capture.
    /// </summary>
    internal sealed class SelectionFrameForm : Form
    {
        private const int Gap = 2;
        private const int Thickness = 3;
        private static readonly Color KeyColor = Color.FromArgb(255, 0, 255);

        private Color _color;

        public SelectionFrameForm(Rectangle area, Color color)
        {
            _color = color;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = KeyColor;
            TransparencyKey = KeyColor;
            var outer = area;
            outer.Inflate(Gap + Thickness, Gap + Thickness);
            Bounds = outer;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_NOACTIVATE
                              | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST;
                return cp;
            }
        }

        public void SetColor(Color color)
        {
            if (color == _color || IsDisposed) return;
            _color = color;
            Invalidate();
            Update();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(KeyColor);
            using (var brush = new SolidBrush(_color))
            {
                int w = ClientSize.Width;
                int h = ClientSize.Height;
                g.FillRectangle(brush, 0, 0, w, Thickness);
                g.FillRectangle(brush, 0, h - Thickness, w, Thickness);
                g.FillRectangle(brush, 0, 0, Thickness, h);
                g.FillRectangle(brush, w - Thickness, 0, Thickness, h);
            }
        }
    }
}
