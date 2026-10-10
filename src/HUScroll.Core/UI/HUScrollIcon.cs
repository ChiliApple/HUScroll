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
