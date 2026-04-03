using FractalExplorer.Utilities.SaveIO.ColorPalettes;

namespace FractalExplorer.Utilities.Coloring
{
    public enum LyapunovColoringMode
    {
        Diverging = 0,
        Absolute = 1,
        ZeroBandHighlight = 2,
        HistogramEqualized = 3,
        LegacyBuiltIn = 4
    }

    public sealed class LyapunovColoringContext
    {
        public double MinExponent { get; init; }
        public double MaxExponent { get; init; }
        public double[] Cdf { get; init; } = Array.Empty<double>();

        public double MapByHistogram(double exponent)
        {
            if (Cdf.Length == 0 || !double.IsFinite(exponent))
            {
                return 0;
            }

            if (MaxExponent <= MinExponent)
            {
                return 0;
            }

            double normalized = (exponent - MinExponent) / (MaxExponent - MinExponent);
            normalized = Math.Clamp(normalized, 0d, 1d);
            int index = (int)Math.Round(normalized * (Cdf.Length - 1));
            return Cdf[Math.Clamp(index, 0, Cdf.Length - 1)];
        }
    }

    public static class LyapunovColoring
    {
        public static Color MapExponent(double exponent, LyapunovColorPalette palette, LyapunovColoringContext? context = null)
        {
            if (!double.IsFinite(exponent))
            {
                return Color.Black;
            }

            palette ??= LyapunovPaletteManager.CreateDefaultBuiltInPalette();
            if (palette.Mode == LyapunovColoringMode.LegacyBuiltIn)
            {
                return MapLegacyBuiltIn(exponent);
            }

            List<Color> colors = EnsureColors(palette.Colors);
            double range = Math.Max(1e-9, Math.Abs(palette.ExponentRange));

            return palette.Mode switch
            {
                LyapunovColoringMode.Absolute => Interpolate(colors, Math.Clamp(Math.Abs(exponent) / range, 0, 1)),
                LyapunovColoringMode.ZeroBandHighlight => MapZeroBand(exponent, colors, range, Math.Max(1e-9, Math.Abs(palette.ZeroBandWidth))),
                LyapunovColoringMode.HistogramEqualized => Interpolate(colors, context?.MapByHistogram(exponent) ?? Math.Clamp((exponent + range) / (2 * range), 0, 1)),
                _ => MapDiverging(exponent, colors, range)
            };
        }

        private static Color MapLegacyBuiltIn(double exponent)
        {
            if (double.IsNaN(exponent) || double.IsInfinity(exponent))
            {
                return Color.Black;
            }

            if (exponent < 0)
            {
                double t = Math.Clamp((-exponent) / 2.0, 0, 1);
                int r = (int)(20 + 70 * t);
                int g = (int)(30 + 170 * t);
                int b = (int)(80 + 175 * t);
                return Color.FromArgb(r, g, b);
            }

            double tp = Math.Clamp(exponent / 2.0, 0, 1);
            int rp = (int)(120 + 135 * tp);
            int gp = (int)(50 + 90 * (1 - tp));
            int bp = (int)(30 + 40 * (1 - tp));
            return Color.FromArgb(rp, gp, bp);
        }

        private static Color MapDiverging(double exponent, List<Color> colors, double range)
        {
            int mid = colors.Count / 2;
            if (exponent < 0)
            {
                var negative = colors.Take(mid + 1).ToList();
                return Interpolate(negative, Math.Clamp((-exponent) / range, 0, 1));
            }

            var positive = colors.Skip(mid).ToList();
            return Interpolate(positive, Math.Clamp(exponent / range, 0, 1));
        }

        private static Color MapZeroBand(double exponent, List<Color> colors, double range, double zeroBand)
        {
            int mid = colors.Count / 2;
            if (Math.Abs(exponent) <= zeroBand)
            {
                return colors[mid];
            }

            if (exponent < 0)
            {
                double t = Math.Clamp((Math.Abs(exponent) - zeroBand) / Math.Max(1e-9, range - zeroBand), 0, 1);
                return Interpolate(colors.Take(mid + 1).ToList(), t);
            }

            double tp = Math.Clamp((exponent - zeroBand) / Math.Max(1e-9, range - zeroBand), 0, 1);
            return Interpolate(colors.Skip(mid).ToList(), tp);
        }

        private static List<Color> EnsureColors(List<Color>? source)
        {
            if (source == null || source.Count == 0)
            {
                return LyapunovPaletteManager.CreateDefaultBuiltInPalette().Colors;
            }

            if (source.Count == 1)
            {
                return new List<Color> { source[0], source[0], source[0] };
            }

            if (source.Count % 2 == 0)
            {
                var fixedSet = source.ToList();
                fixedSet.Insert(source.Count / 2, source[source.Count / 2]);
                return fixedSet;
            }

            return source.ToList();
        }

        private static Color Interpolate(List<Color> colors, double t)
        {
            if (colors.Count == 1)
            {
                return colors[0];
            }

            t = Math.Clamp(t, 0, 1);
            double scaled = t * (colors.Count - 1);
            int idx = (int)Math.Floor(scaled);
            int idx2 = Math.Min(colors.Count - 1, idx + 1);
            double frac = scaled - idx;
            Color c1 = colors[idx];
            Color c2 = colors[idx2];

            int r = (int)Math.Round(c1.R + (c2.R - c1.R) * frac);
            int g = (int)Math.Round(c1.G + (c2.G - c1.G) * frac);
            int b = (int)Math.Round(c1.B + (c2.B - c1.B) * frac);
            return Color.FromArgb(r, g, b);
        }
    }
}
