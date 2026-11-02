using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pulsive;

internal static class EdgeFeather
{
    // Relative widths keep the fade consistent when the overlay is resized.
    public static Brush? CreateMask(Thickness edges,Thickness bounds=default)
    {
        bool fullBounds=bounds.Left==0&&bounds.Top==0&&bounds.Right==0&&bounds.Bottom==0;
        if (fullBounds&&edges.Left <= 0 && edges.Top <= 0 && edges.Right <= 0 && edges.Bottom <= 0) return null;
        double width=1-bounds.Left-bounds.Right,height=1-bounds.Top-bounds.Bottom;
        if(width<=0||height<=0) return Brushes.Transparent;
        const int size = 256;
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            double u = (x / (double)(size - 1)-bounds.Left)/width, v = (y / (double)(size - 1)-bounds.Top)/height;
            double opacity = u<0||u>1||v<0||v>1?0:Fade(u, edges.Left) * Fade(1 - u, edges.Right) * Fade(v, edges.Top) * Fade(1 - v, edges.Bottom);
            byte alpha = (byte)Math.Round(opacity * 255);
            int p = (y * size + x) * 4;
            pixels[p] = pixels[p + 1] = pixels[p + 2] = pixels[p + 3] = alpha;
        }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4);
        bitmap.Freeze();
        var mask = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
        mask.Freeze();
        return mask;
    }
    static double Fade(double distance, double width)
    {
        if (width <= 0) return 1;
        double t = Math.Clamp(distance / width, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
