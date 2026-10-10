using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using HUScroll.Core.Capture;
using HUScroll.Core.Native;

namespace HUScroll.Core.UI
{
    /// <summary>
    /// Full screen overlay on a frozen screenshot: hover highlights the window (or the page area of a browser) under the cursor,
    /// a click selects it, dragging selects a free area.
    /// </summary>
    internal sealed class RegionSelectorForm : Form
    {
        private const int DragThreshold = 5;
        private static readonly Color Accent = Color.FromArgb(255, 0, 170, 255);

        private readonly Bitmap _screenshot;
        private readonly Bitmap _dimmed;
        private readonly Rectangle _virtualScreen;
        private readonly WindowFinder _finder;
        private readonly Font _labelFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        private readonly Font _hintFont = new Font("Segoe UI", 10f);
        private readonly Font _titleFont = new Font("Segoe UI", 12f, FontStyle.Bold);
        private readonly Rectangle _hintMonitor;
        private readonly bool _manual;
        private readonly Bitmap _logo = HUScrollIcon.Create(64);

        private Rectangle _selection = Rectangle.Empty; // screen coordinates
        private WindowHit _hover;
        private Point? _dragStart;
        private bool _dragging;

        public RegionSelectorForm(Bitmap screenshot, Rectangle virtualScreen, WindowFinder finder, bool manual = false)
        {
            _manual = manual;
            _screenshot = screenshot;
            _virtualScreen = virtualScreen;
            _finder = finder;
            _dimmed = CreateDimmed(screenshot);
            _hintMonitor = Screen.FromPoint(Cursor.Position).Bounds;

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            BackColor = Color.Black;
            Text = "HUScroll";
            Bounds = virtualScreen;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Opaque, true);
        }

        /// <summary>The selected area in screen coordinates</summary>
        public Rectangle SelectedArea { get; private set; }

        /// <summary>The top level window of the selection, IntPtr.Zero for a dragged area</summary>
        public IntPtr SelectedWindow { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // WinForms may scale the bounds when the form is created on a monitor with another DPI
            Bounds = _virtualScreen;
            Activate();
            UpdateHover(Cursor.Position);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                Cancel();
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                _dragStart = ToScreen(e.Location);
                _dragging = false;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var screenPoint = ToScreen(e.Location);
            if (_dragStart.HasValue)
            {
                var start = _dragStart.Value;
                if (!_dragging && (Math.Abs(screenPoint.X - start.X) > DragThreshold || Math.Abs(screenPoint.Y - start.Y) > DragThreshold))
                {
                    _dragging = true;
                    _hover = null;
                }

                if (_dragging)
                {
                    SetSelection(Rectangle.FromLTRB(
                        Math.Min(start.X, screenPoint.X), Math.Min(start.Y, screenPoint.Y),
                        Math.Max(start.X, screenPoint.X) + 1, Math.Max(start.Y, screenPoint.Y) + 1));
                    return;
                }
            }

            UpdateHover(screenPoint);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !_dragStart.HasValue) return;
            _dragStart = null;
            if (_dragging)
            {
                _dragging = false;
                if (_selection.Width >= 50 && _selection.Height >= 50)
                {
                    Accept(_selection, IntPtr.Zero);
                }
                else
                {
                    UpdateHover(ToScreen(e.Location));
                }
                return;
            }

            if (_hover != null)
            {
                Accept(_selection, _hover.TopLevel);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Escape:
                    Cancel();
                    break;
                case Keys.Enter:
                    if (!_selection.IsEmpty) Accept(_selection, _hover?.TopLevel ?? IntPtr.Zero);
                    break;
                case Keys.ShiftKey:
                    if (!_dragging) UpdateHover(Cursor.Position);
                    break;
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.ShiftKey && !_dragging) UpdateHover(Cursor.Position);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            var clip = e.ClipRectangle;
            g.DrawImage(_dimmed, clip, clip, GraphicsUnit.Pixel);

            var selection = ToClient(_selection);
            if (!selection.IsEmpty)
            {
                var visible = Rectangle.Intersect(selection, clip);
                if (!visible.IsEmpty) g.DrawImage(_screenshot, visible, visible, GraphicsUnit.Pixel);
            }

            g.CompositingMode = CompositingMode.SourceOver;
            g.PixelOffsetMode = PixelOffsetMode.Default;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            if (!selection.IsEmpty)
            {
                using (var pen = new Pen(Accent, 2f))
                {
                    var border = selection;
                    border.Inflate(-1, -1);
                    g.DrawRectangle(pen, border);
                }
                DrawLabel(g, selection);
            }

            DrawHint(g);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _dimmed.Dispose();
                _labelFont.Dispose();
                _hintFont.Dispose();
                _titleFont.Dispose();
                _logo.Dispose();
            }
            base.Dispose(disposing);
        }

        private void DrawLabel(Graphics g, Rectangle selection)
        {
            string kind = _hover == null ? "Bereich" : (_hover.Handle == _hover.TopLevel ? "Fenster" : "Inhalt");
            string text = $"{_selection.Width} × {_selection.Height} px  ·  {kind}";
            var size = g.MeasureString(text, _labelFont);
            var box = new RectangleF(selection.Left, selection.Bottom + 6, size.Width + 12, size.Height + 6);
            if (box.Bottom > ClientSize.Height) box.Y = selection.Top - box.Height - 6;
            if (box.Y < 0) box.Y = selection.Top + 6;
            if (box.Right > ClientSize.Width) box.X = ClientSize.Width - box.Width;
            using (var path = RoundedRectangle(box, 4))
            using (var brush = new SolidBrush(Accent))
            {
                g.FillPath(brush, path);
            }
            g.DrawString(text, _labelFont, Brushes.White, box.X + 6, box.Y + 3);
        }

        private void DrawHint(Graphics g)
        {
            string title = _manual ? "HUScroll – Scroll-Capture manuell (danach selbst scrollen)" : "HUScroll – Scroll-Capture automatisch";
            const string hint = "Klick: Fenster / Seiteninhalt   ·   Ziehen: Bereich   ·   Shift: ganzes Fenster   ·   Esc: Abbrechen";
            var titleSize = g.MeasureString(title, _titleFont);
            var hintSize = g.MeasureString(hint, _hintFont);
            float textHeight = titleSize.Height + hintSize.Height + 2;
            float logoSize = textHeight;
            float padding = 12;
            float width = logoSize + padding * 3 + Math.Max(titleSize.Width, hintSize.Width);
            float height = textHeight + padding * 2 - 4;
            var monitor = ToClient(_hintMonitor);
            var box = new RectangleF(monitor.Left + (monitor.Width - width) / 2, monitor.Top + 24, width, height);

            // Move the hint to the bottom when the selection is below it
            if (ToClient(_selection).IntersectsWith(Rectangle.Round(box)))
            {
                box.Y = monitor.Bottom - height - 48;
            }

            using (var path = RoundedRectangle(box, 8))
            using (var brush = new SolidBrush(Color.FromArgb(225, 24, 26, 30)))
            using (var pen = new Pen(Accent, 1.5f))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }

            var oldInterpolation = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_logo, box.X + padding, box.Y + (box.Height - logoSize) / 2, logoSize, logoSize);
            g.InterpolationMode = oldInterpolation;

            float textX = box.X + padding * 2 + logoSize;
            float textY = box.Y + (box.Height - textHeight) / 2;
            using (var accentBrush = new SolidBrush(Accent))
            using (var textBrush = new SolidBrush(Color.FromArgb(235, 235, 235)))
            {
                g.DrawString(title, _titleFont, accentBrush, textX, textY);
                g.DrawString(hint, _hintFont, textBrush, textX, textY + titleSize.Height + 2);
            }
        }

        private void UpdateHover(Point screenPoint)
        {
            bool wholeWindow = NativeMethods.IsKeyDown(NativeMethods.VK_SHIFT);
            _hover = _finder.Find(screenPoint, !wholeWindow);
            SetSelection(_hover == null ? Rectangle.Empty : Rectangle.Intersect(_hover.Bounds, _virtualScreen));
        }

        private void SetSelection(Rectangle selection)
        {
            if (selection == _selection) return;
            _selection = selection;
            // The hint and the label can move anywhere, a full repaint is simple and fast enough (two blits)
            Invalidate();
        }

        private void Accept(Rectangle area, IntPtr topLevel)
        {
            SelectedArea = area;
            SelectedWindow = topLevel;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Cancel()
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private Point ToScreen(Point client) => new Point(client.X + _virtualScreen.X, client.Y + _virtualScreen.Y);

        private Rectangle ToClient(Rectangle screen) =>
            screen.IsEmpty ? Rectangle.Empty : new Rectangle(screen.X - _virtualScreen.X, screen.Y - _virtualScreen.Y, screen.Width, screen.Height);

        private static Bitmap CreateDimmed(Bitmap source)
        {
            var dimmed = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(dimmed))
            using (var brush = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            {
                g.DrawImageUnscaled(source, 0, 0);
                g.FillRectangle(brush, 0, 0, source.Width, source.Height);
            }
            return dimmed;
        }

        private static GraphicsPath RoundedRectangle(RectangleF rect, float radius)
        {
            float d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
