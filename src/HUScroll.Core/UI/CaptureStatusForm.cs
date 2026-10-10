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
using System.Windows.Forms;
using HUScroll.Core.Native;

namespace HUScroll.Core.UI
{
    /// <summary>
    /// Small status window while scrolling. It never takes the focus (WS_EX_NOACTIVATE),
    /// so the scrolled application stays active, and it is placed outside of the captured area.
    /// </summary>
    internal sealed class CaptureStatusForm : Form
    {
        private readonly Label _status;
        private readonly Button _done;
        private readonly PictureBox _logo;

        public CaptureStatusForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(24, 26, 30);
            ForeColor = Color.FromArgb(235, 235, 235);
            Font = new Font("Segoe UI", 9.5f);
            Padding = new Padding(10, 8, 8, 8);
            ClientSize = new Size(380, 44);

            _status = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "● HUScroll startet …",
                Padding = new Padding(6, 0, 0, 0)
            };

            _done = new Button
            {
                Text = "Fertig (Esc)",
                Dock = DockStyle.Right,
                Width = 100,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 170, 255),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            _done.FlatAppearance.BorderSize = 0;
            _done.Click += (s, e) => StopRequested = true;

            _logo = new PictureBox
            {
                Dock = DockStyle.Left,
                Width = 34,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = HUScrollIcon.Create(64)
            };

            // Fill first, then the docked sides (WinForms docks in reverse order of the collection)
            Controls.Add(_status);
            Controls.Add(_logo);
            Controls.Add(_done);
        }

        /// <summary>
        /// Scale the fixed pixel sizes to the DPI of the monitor (fonts in points scale by themselves)
        /// </summary>
        public void ApplyDpiScale()
        {
            float factor;
            using (var g = CreateGraphics())
            {
                factor = g.DpiX / 96f;
            }

            if (factor <= 1.01f) return;
            ClientSize = new Size((int)(380 * factor), (int)(44 * factor));
            Padding = new Padding((int)(10 * factor), (int)(8 * factor), (int)(8 * factor), (int)(8 * factor));
            _done.Width = (int)(100 * factor);
            _logo.Width = (int)(34 * factor);
        }

        /// <summary>True after the user clicked "Fertig"</summary>
        public bool StopRequested { get; private set; }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST;
                return cp;
            }
        }

        public void SetStatus(string text)
        {
            if (IsDisposed) return;
            _status.Text = text;
            _status.Refresh();
        }

        /// <summary>
        /// Place the window next to the captured area, outside of it. Returns false if there is no room (it is not shown then).
        /// </summary>
        public bool PlaceOutside(Rectangle area)
        {
            var screen = Screen.FromRectangle(area).WorkingArea;
            var size = Size;
            var candidates = new[]
            {
                new Point(area.Left, area.Top - size.Height - 8),
                new Point(area.Left, area.Bottom + 8),
                new Point(area.Right + 8, area.Top),
                new Point(area.Left - size.Width - 8, area.Top),
                new Point(screen.Left + 8, screen.Top + 8),
                new Point(screen.Right - size.Width - 8, screen.Bottom - size.Height - 8)
            };

            foreach (var screenInfo in new[] { screen, SystemInformation.VirtualScreen })
            {
                foreach (var candidate in candidates)
                {
                    var bounds = new Rectangle(candidate, size);
                    if (screenInfo.Contains(bounds) && !bounds.IntersectsWith(area))
                    {
                        Location = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Color.FromArgb(0, 170, 255)))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            }
        }
    }
}
