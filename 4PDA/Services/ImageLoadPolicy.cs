using System;

namespace _4PDA.Services
{
    internal static class ImageLoadPolicy
    {
        internal static bool DecodeSize(uint originalWidth, uint originalHeight, int requestedWidth,
            bool mobile, out int width, out int height)
        {
            width = height = 0;
            if (originalWidth == 0 || originalHeight == 0 || originalWidth > 65535 || originalHeight > 65535
                || (long)originalWidth * originalHeight > 268435456) return false;
            int edge = mobile ? 640 : 1280;
            int pixels = mobile ? 393216 : 1572864;
            double scale = Math.Min(1, Math.Min((double)Math.Max(32, Math.Min(edge, requestedWidth)) / originalWidth,
                (double)edge / originalHeight));
            scale = Math.Min(scale, Math.Sqrt((double)pixels / ((long)originalWidth * originalHeight)));
            width = Math.Max(1, (int)(originalWidth * scale));
            height = Math.Max(1, (int)(originalHeight * scale));
            return true;
        }

        internal static double NewsGap(double width, double height) { return width > height ? 8 : 0; }
    }
}
