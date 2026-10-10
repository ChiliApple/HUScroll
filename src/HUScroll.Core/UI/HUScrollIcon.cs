using System.Drawing;
using System.Drawing.Drawing2D;

namespace HUScroll.Core.UI
{
    /// <summary>
    /// The menu icon, drawn in code (no resources needed): a page with a down arrow
    /// </summary>
    public static class HUScrollIcon
    {
        public static Bitmap Create(int size = 16)
        {
            var bitmap = new Bitmap(size, size);
            float s = size / 16f;
            using (var g = Graphics.FromImage(bitmap))
            using (var page = new SolidBrush(Color.FromArgb(255, 255, 255)))
            using (var border = new Pen(Color.FromArgb(70, 70, 70), 1f * s))
            using (var lines = new Pen(Color.FromArgb(150, 150, 150), 1f * s))
            using (var accent = new SolidBrush(Color.FromArgb(0, 150, 230)))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                g.FillRectangle(page, 2 * s, 0.5f * s, 8 * s, 14.5f * s);
                g.DrawRectangle(border, 2 * s, 0.5f * s, 8 * s, 14.5f * s);
                for (int i = 0; i < 5; i++)
                {
                    float y = (3 + i * 2.4f) * s;
                    g.DrawLine(lines, 3.5f * s, y, 8.5f * s, y);
                }

                // down arrow
                g.FillRectangle(accent, 11.5f * s, 2 * s, 2.5f * s, 7 * s);
                g.FillPolygon(accent, new[]
                {
                    new PointF(9.5f * s, 8.5f * s),
                    new PointF(16f * s, 8.5f * s),
                    new PointF(12.75f * s, 14f * s)
                });
            }
            return bitmap;
        }
    }
}
