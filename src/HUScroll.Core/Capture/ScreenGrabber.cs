using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using HUScroll.Core.Stitching;

namespace HUScroll.Core.Capture
{
    /// <summary>
    /// Screen capture and conversion between Bitmap and <see cref="FrameData"/>
    /// </summary>
    public static class ScreenGrabber
    {
        /// <summary>
        /// Capture an area of the screen (screen coordinates, physical pixels: Greenshot is per monitor DPI aware)
        /// </summary>
        public static Bitmap CaptureBitmap(Rectangle area)
        {
            var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy);
                }
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Capture an area of the screen as raw pixels
        /// </summary>
        public static FrameData Capture(Rectangle area)
        {
            using (var bitmap = CaptureBitmap(area))
            {
                return ToFrame(bitmap);
            }
        }

        public static FrameData ToFrame(Bitmap bitmap)
        {
            if (bitmap == null) throw new ArgumentNullException(nameof(bitmap));
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var pixels = new int[bitmap.Width * bitmap.Height];
                if (data.Stride == bitmap.Width * 4)
                {
                    Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                }
                else
                {
                    for (int y = 0; y < bitmap.Height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * bitmap.Width, bitmap.Width);
                    }
                }

                // Screen captures have no transparency, normalize alpha so equal colors have equal values
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] |= unchecked((int)0xFF000000);
                }

                return new FrameData(bitmap.Width, bitmap.Height, pixels);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        public static Bitmap ToBitmap(FrameData frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            var bitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                if (data.Stride == frame.Width * 4)
                {
                    Marshal.Copy(frame.Pixels, 0, data.Scan0, frame.Pixels.Length);
                }
                else
                {
                    for (int y = 0; y < frame.Height; y++)
                    {
                        Marshal.Copy(frame.Pixels, y * frame.Width, IntPtr.Add(data.Scan0, y * data.Stride), frame.Width);
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }
    }
}
