using System.IO;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Mochi.Core;
using Windows.UI;

namespace MochiIsland
{
    /// <summary>
    /// The pet face from the macOS build: the earlier full kitten illustration
    /// (`Web/pets/kitten-wake.png`) with a small letter badge in the bottom-right
    /// corner. Look ids (`a`…`f`) only change the badge colour until separate art ships.
    /// </summary>
    internal static class KittenArt
    {
        private static readonly Dictionary<string, ImageSource?> cache = new();

        /// <summary>
        /// Look `a` is the untinted illustration. `b`…`f` are the same art multiplied by
        /// the macOS PetLookStyle tint, so the desktop pet changes coat colour with look.
        /// </summary>
        public static string? ArtworkPath(string look)
        {
            var resolved = IslandPrefs.NormalizeLook(look);
            var name = resolved == "a" ? "kitten-wake.png" : "kitten-wake-" + resolved + ".png";
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Web", "pets", name);
            if (File.Exists(path)) return path;
            var fallback = System.IO.Path.Combine(AppContext.BaseDirectory, "Web", "pets", "kitten-wake.png");
            return File.Exists(fallback) ? fallback : null;
        }

        public static ImageSource? Artwork(string look)
        {
            var resolved = IslandPrefs.NormalizeLook(look);
            if (cache.TryGetValue(resolved, out var cached)) return cached;
            var path = ArtworkPath(resolved);
            ImageSource? source = path != null ? new BitmapImage(new Uri(path)) : null;
            cache[resolved] = source;
            return source;
        }

        public static FrameworkElement Face(string look, double side)
        {
            var resolved = IslandPrefs.NormalizeLook(look);
            var grid = new Grid { Width = side, Height = side };
            if (Artwork(resolved) is ImageSource source)
            {
                grid.Children.Add(new Image { Source = source, Stretch = Stretch.Uniform });
            }
            var badge = Math.Max(10, side * 0.28);
            var disc = new Grid
            {
                Width = badge,
                Height = badge,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, side * 0.04, side * 0.04)
            };
            disc.Children.Add(new Ellipse
            {
                Fill = new SolidColorBrush(Badge(resolved)),
                Stroke = new SolidColorBrush(Color.FromArgb(217, 255, 255, 255)),
                StrokeThickness = Math.Max(0.8, badge * 0.06)
            });
            disc.Children.Add(new TextBlock
            {
                Text = IslandPrefs.PetLetter(resolved),
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = badge * 0.62,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            });
            grid.Children.Add(disc);
            return grid;
        }

        public static Color Badge(string look) => look switch
        {
            "b" => Color.FromArgb(255, 219, 107, 71),
            "c" => Color.FromArgb(255, 71, 133, 219),
            "d" => Color.FromArgb(255, 56, 158, 112),
            "e" => Color.FromArgb(255, 133, 87, 199),
            "f" => Color.FromArgb(255, 199, 148, 41),
            _ => Color.FromArgb(255, 89, 71, 56)
        };
    }
}
