using System;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace B0r3dQR
{
    // The b0r3d cup from the icon, at the picture that suits the screen: its hand-made 32 px cup at
    // 100 %, the 48 px one at 150 %, and so on, rather than one picture stretched.
    static class Cup
    {
        static BitmapFrame[] frames;

        public static ImageSource For(double size, double scale)
        {
            if (frames == null)
            {
                var icon = BitmapDecoder.Create(new Uri("pack://application:,,,/b0r3dQR;component/assets/b0r3dQR.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                frames = icon.Frames.OrderBy(f => f.PixelWidth).ToArray();
            }
            double wanted = size * scale;
            return frames.FirstOrDefault(f => f.PixelWidth >= wanted - 0.5) ?? frames.Last();
        }
    }
}
