using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;

namespace HUScroll.Core.UI
{
    /// <summary>
    /// The HU logo, embedded in the plugin assembly (Assets/logo16/32/48/64.png, logical name "HUScroll.logoNN.png")
    /// </summary>
    public static class HUScrollIcon
    {
        private static readonly int[] Sizes = { 16, 32, 48, 64 };

        /// <summary>
        /// The logo in the requested size (the next bigger embedded size, scaled down with high quality)
        /// </summary>
        public static Bitmap Create(int size = 16)
        {
            int source = Sizes.FirstOrDefault(s => s >= size);
            if (source == 0) source = Sizes[Sizes.Length - 1];

            using (var original = Load(source))
            {
                if (original == null) return new Bitmap(size, size);
                var result = new Bitmap(size, size);
                using (var g = Graphics.FromImage(result))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    g.DrawImage(original, new Rectangle(0, 0, size, size));
                }
                return result;
            }
        }

        private static Bitmap Load(int size)
        {
            try
            {
                var assembly = typeof(HUScrollIcon).Assembly;
                using (var stream = assembly.GetManifestResourceStream($"HUScroll.logo{size}.png"))
                {
                    if (stream == null) return null;
                    using (var memory = new MemoryStream())
                    {
                        stream.CopyTo(memory);
                        memory.Position = 0;
                        // A copy, so the stream can be closed (GDI+ keeps the stream of a Bitmap open otherwise)
                        using (var image = new Bitmap(memory))
                        {
                            return new Bitmap(image);
                        }
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
