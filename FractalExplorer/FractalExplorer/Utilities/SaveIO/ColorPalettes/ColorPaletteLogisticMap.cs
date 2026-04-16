namespace FractalExplorer.Utilities.SaveIO.ColorPalettes
{
    public sealed class LogisticMapPaletteManager : PaletteManager
    {
        protected override string CustomPalettesFileName => "logistic_map_palettes.json";
        protected override string PaletteSubjectName => "логистического отображения";

        protected override IEnumerable<Palette> CreateBuiltInPalettes()
        {
            return new List<Palette>
            {
                new("Орбиты: монохром (high-contrast)", new List<Color>
                {
                    Color.Black,
                    Color.White
                }, true, true, 500, 1.0, false),

                new("Орбиты: периодические полосы", new List<Color>
                {
                    Color.FromArgb(12, 12, 12),
                    Color.FromArgb(255, 235, 59),
                    Color.FromArgb(3, 169, 244),
                    Color.FromArgb(233, 30, 99),
                    Color.FromArgb(76, 175, 80),
                    Color.White
                }, false, true, 16, 1.0, false),

                new("Плотность: холодная", new List<Color>
                {
                    Color.Black,
                    Color.FromArgb(11, 35, 79),
                    Color.FromArgb(24, 119, 242),
                    Color.FromArgb(127, 219, 255),
                    Color.White
                }, true, true, 900, 1.0, false),

                new("Плотность: тёплая", new List<Color>
                {
                    Color.Black,
                    Color.FromArgb(76, 0, 0),
                    Color.FromArgb(191, 54, 12),
                    Color.FromArgb(255, 167, 38),
                    Color.FromArgb(255, 245, 157),
                    Color.White
                }, true, true, 900, 1.0, false),

                new("Плотность: холодно-тёплая", new List<Color>
                {
                    Color.Black,
                    Color.FromArgb(16, 38, 84),
                    Color.FromArgb(70, 117, 255),
                    Color.FromArgb(210, 240, 255),
                    Color.FromArgb(255, 193, 7),
                    Color.FromArgb(239, 83, 80),
                    Color.White
                }, true, true, 1100, 1.0, false),

                new("Периодические зоны (banded)", new List<Color>
                {
                    Color.Black,
                    Color.FromArgb(44, 62, 80),
                    Color.FromArgb(142, 68, 173),
                    Color.FromArgb(41, 128, 185),
                    Color.FromArgb(39, 174, 96),
                    Color.FromArgb(243, 156, 18),
                    Color.FromArgb(231, 76, 60),
                    Color.White
                }, false, true, 24, 1.0, false)
            };
        }
    }
}
