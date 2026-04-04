using System.Text.Json;
using System.Text.Json.Serialization;
using FractalExplorer.Utilities.Coloring;

namespace FractalExplorer.Utilities.SaveIO.ColorPalettes
{
    public class LyapunovColorPalette
    {
        public string Name { get; set; } = "Новая палитра";
        public LyapunovColoringMode Mode { get; set; } = LyapunovColoringMode.LegacyBuiltIn;
        public List<Color> Colors { get; set; } = new();
        public double ExponentRange { get; set; } = 2.0;
        public double ZeroBandWidth { get; set; } = 0.05;

        [JsonIgnore]
        public bool IsBuiltIn { get; set; }

        public LyapunovColorPalette CloneAsCustom(string newName)
        {
            return new LyapunovColorPalette
            {
                Name = newName,
                Mode = Mode,
                Colors = Colors.ToList(),
                ExponentRange = ExponentRange,
                ZeroBandWidth = ZeroBandWidth,
                IsBuiltIn = false
            };
        }
    }

    public class LyapunovPaletteManager
    {
        private const string PaletteFile = "lyapunov_palettes.json";

        public List<LyapunovColorPalette> Palettes { get; } = new();
        public LyapunovColorPalette ActivePalette { get; set; }

        public LyapunovPaletteManager()
        {
            LoadPalettes();
            ActivePalette = Palettes.FirstOrDefault() ?? CreateDefaultBuiltInPalette();
        }

        public static LyapunovColorPalette CreateDefaultBuiltInPalette()
        {
            return new LyapunovColorPalette
            {
                Name = "Наследуемая встроенная",
                Mode = LyapunovColoringMode.LegacyBuiltIn,
                Colors = new List<Color>
                {
                    Color.FromArgb(20, 30, 80),
                    Color.FromArgb(90, 200, 255),
                    Color.FromArgb(120, 140, 70),
                    Color.FromArgb(190, 100, 45),
                    Color.FromArgb(255, 50, 30)
                },
                ExponentRange = 2.0,
                ZeroBandWidth = 0.05,
                IsBuiltIn = true
            };
        }

        private void LoadPalettes()
        {
            Palettes.Clear();

            LyapunovColorPalette defaultBuiltIn = CreateDefaultBuiltInPalette();
            Palettes.Add(defaultBuiltIn);
            Palettes.AddRange(CreateAdditionalBuiltInPalettes(defaultBuiltIn));

            string filePath = Path.Combine(Application.StartupPath, "Saves", PaletteFile);
            if (!File.Exists(filePath))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                var options = new JsonSerializerOptions();
                options.Converters.Add(new JsonConverters.JsonColorConverter());
                var loaded = JsonSerializer.Deserialize<List<LyapunovColorPalette>>(json, options);
                if (loaded != null)
                {
                    Palettes.AddRange(loaded.Where(p => p != null));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось загрузить палитры Ляпунова: {ex.Message}", "Ошибка загрузки палитр", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void SavePalettes()
        {
            try
            {
                string saveDir = Path.Combine(Application.StartupPath, "Saves");
                Directory.CreateDirectory(saveDir);
                string filePath = Path.Combine(saveDir, PaletteFile);

                var custom = Palettes.Where(p => !p.IsBuiltIn).ToList();
                var options = new JsonSerializerOptions { WriteIndented = true };
                options.Converters.Add(new JsonConverters.JsonColorConverter());
                string json = JsonSerializer.Serialize(custom, options);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось сохранить палитры Ляпунова: {ex.Message}", "Ошибка сохранения палитр", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static IEnumerable<LyapunovColorPalette> CreateAdditionalBuiltInPalettes(LyapunovColorPalette defaultBuiltIn)
        {
            return new List<LyapunovColorPalette>
            {
                new() { Name = "Классическая Ляпунова", Mode = LyapunovColoringMode.Diverging, Colors = defaultBuiltIn.Colors.ToList(), ExponentRange = 2.0, ZeroBandWidth = 0.05, IsBuiltIn = true },
                new() { Name = "Стандартный серый", Mode = LyapunovColoringMode.Absolute, Colors = new List<Color> { Color.Black, Color.DimGray, Color.Silver, Color.White }, ExponentRange = 2.0, ZeroBandWidth = 0.05, IsBuiltIn = true },
                new() { Name = "Ультрафиолет", Mode = LyapunovColoringMode.HistogramEqualized, Colors = new List<Color> { Color.Black, Color.DarkViolet, Color.Violet, Color.White }, ExponentRange = 2.1, ZeroBandWidth = 0.05, IsBuiltIn = true },
                new() { Name = "Огонь", Mode = LyapunovColoringMode.Diverging, Colors = new List<Color> { Color.Black, Color.DarkRed, Color.Red, Color.Orange, Color.Yellow, Color.White }, ExponentRange = 1.9, ZeroBandWidth = 0.035, IsBuiltIn = true },
                new() { Name = "Лёд", Mode = LyapunovColoringMode.Absolute, Colors = new List<Color> { Color.Black, Color.DarkBlue, Color.Blue, Color.Cyan, Color.White }, ExponentRange = 2.0, ZeroBandWidth = 0.05, IsBuiltIn = true },
                new() { Name = "Огонь и лед", Mode = LyapunovColoringMode.ZeroBandHighlight, Colors = new List<Color> { Color.Black, Color.DarkBlue, Color.Cyan, Color.White, Color.Yellow, Color.Red, Color.DarkRed }, ExponentRange = 2.0, ZeroBandWidth = 0.03, IsBuiltIn = true },
                new() { Name = "Психоделика", Mode = LyapunovColoringMode.HistogramEqualized, Colors = new List<Color> { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta }, ExponentRange = 2.5, ZeroBandWidth = 0.05, IsBuiltIn = true },
                new() { Name = "Черно-белый", Mode = LyapunovColoringMode.Absolute, Colors = new List<Color> { Color.Black, Color.White }, ExponentRange = 2.0, ZeroBandWidth = 0.05, IsBuiltIn = true },
                new() { Name = "Зеленый", Mode = LyapunovColoringMode.Diverging, Colors = new List<Color> { Color.Black, Color.FromArgb(255, 0, 128, 0), Color.FromArgb(255, 0, 204, 0), Color.FromArgb(255, 60, 255, 60), Color.FromArgb(255, 213, 255, 213), Color.White }, ExponentRange = 2.0, ZeroBandWidth = 0.04, IsBuiltIn = true },
                new() { Name = "Сепия", Mode = LyapunovColoringMode.Absolute, Colors = new List<Color> { Color.FromArgb(20, 10, 0), Color.FromArgb(255, 240, 192) }, ExponentRange = 2.0, ZeroBandWidth = 0.05, IsBuiltIn = true },
                new() { Name = "Белый ультрафиолет", Mode = LyapunovColoringMode.HistogramEqualized, Colors = new List<Color> { Color.White, Color.Lavender, Color.Violet, Color.DarkViolet, Color.Indigo, Color.Black }, ExponentRange = 2.2, ZeroBandWidth = 0.05, IsBuiltIn = true },
                new() { Name = "Белый огонь", Mode = LyapunovColoringMode.Diverging, Colors = new List<Color> { Color.White, Color.LightYellow, Color.Yellow, Color.Orange, Color.Red, Color.DarkRed, Color.Maroon }, ExponentRange = 1.9, ZeroBandWidth = 0.03, IsBuiltIn = true },
                new() { Name = "Белый лед", Mode = LyapunovColoringMode.Absolute, Colors = new List<Color> { Color.White, Color.LightCyan, Color.Cyan, Color.DeepSkyBlue, Color.Blue, Color.DarkBlue, Color.Navy }, ExponentRange = 2.1, ZeroBandWidth = 0.05, IsBuiltIn = true },
                new() { Name = "Белый зеленый", Mode = LyapunovColoringMode.ZeroBandHighlight, Colors = new List<Color> { Color.White, Color.FromArgb(255, 180, 255, 180), Color.FromArgb(255, 60, 220, 60), Color.FromArgb(255, 0, 120, 0), Color.Black }, ExponentRange = 2.0, ZeroBandWidth = 0.025, IsBuiltIn = true },
                new() { Name = "Бело-черный", Mode = LyapunovColoringMode.Absolute, Colors = new List<Color> { Color.White, Color.Black }, ExponentRange = 2.0, ZeroBandWidth = 0.05, IsBuiltIn = true },
                new() { Name = "Закат", Mode = LyapunovColoringMode.Diverging, Colors = new List<Color> { Color.Black, Color.FromArgb(255, 25, 25, 112), Color.FromArgb(255, 75, 0, 130), Color.FromArgb(255, 220, 20, 60), Color.FromArgb(255, 255, 140, 0), Color.White }, ExponentRange = 2.2, ZeroBandWidth = 0.04, IsBuiltIn = true },
                new() { Name = "Океан", Mode = LyapunovColoringMode.Absolute, Colors = new List<Color> { Color.Black, Color.FromArgb(255, 0, 20, 40), Color.FromArgb(255, 0, 100, 150), Color.FromArgb(255, 100, 200, 255), Color.White }, ExponentRange = 2.0, ZeroBandWidth = 0.05, IsBuiltIn = true },
                new() { Name = "Золото", Mode = LyapunovColoringMode.HistogramEqualized, Colors = new List<Color> { Color.Black, Color.FromArgb(255, 85, 65, 0), Color.FromArgb(255, 205, 173, 0), Color.FromArgb(255, 255, 215, 0), Color.White }, ExponentRange = 2.0, ZeroBandWidth = 0.05, IsBuiltIn = true },
                new() { Name = "Медь", Mode = LyapunovColoringMode.Diverging, Colors = new List<Color> { Color.Black, Color.FromArgb(255, 72, 61, 20), Color.FromArgb(255, 184, 115, 51), Color.FromArgb(255, 240, 147, 43), Color.White }, ExponentRange = 1.95, ZeroBandWidth = 0.035, IsBuiltIn = true },
                new() { Name = "Неон", Mode = LyapunovColoringMode.HistogramEqualized, Colors = new List<Color> { Color.Black, Color.FromArgb(255, 75, 0, 75), Color.FromArgb(255, 255, 0, 255), Color.FromArgb(255, 0, 255, 255), Color.FromArgb(255, 0, 255, 0), Color.White }, ExponentRange = 2.4, ZeroBandWidth = 0.05, IsBuiltIn = true },
                new() { Name = "Радуга", Mode = LyapunovColoringMode.HistogramEqualized, Colors = new List<Color> { Color.Black, Color.FromArgb(255, 148, 0, 211), Color.FromArgb(255, 0, 0, 255), Color.FromArgb(255, 0, 255, 0), Color.FromArgb(255, 255, 255, 0), Color.FromArgb(255, 255, 0, 0), Color.White }, ExponentRange = 2.3, ZeroBandWidth = 0.05, IsBuiltIn = true },
                new() { Name = "Аметист", Mode = LyapunovColoringMode.Diverging, Colors = new List<Color> { Color.Black, Color.FromArgb(255, 25, 25, 112), Color.FromArgb(255, 123, 104, 238), Color.FromArgb(255, 221, 160, 221), Color.White }, ExponentRange = 2.1, ZeroBandWidth = 0.04, IsBuiltIn = true },
                new() { Name = "Лес", Mode = LyapunovColoringMode.Diverging, Colors = new List<Color> { Color.Black, Color.FromArgb(255, 0, 39, 0), Color.FromArgb(255, 34, 139, 34), Color.FromArgb(255, 124, 252, 0), Color.White }, ExponentRange = 2.1, ZeroBandWidth = 0.035, IsBuiltIn = true },
                new() { Name = "Космос", Mode = LyapunovColoringMode.Absolute, Colors = new List<Color> { Color.Black, Color.FromArgb(255, 25, 25, 112), Color.FromArgb(255, 72, 61, 139), Color.FromArgb(255, 138, 43, 226), Color.FromArgb(255, 255, 20, 147), Color.White }, ExponentRange = 2.5, ZeroBandWidth = 0.05, IsBuiltIn = true },
                new() { Name = "Бирюза", Mode = LyapunovColoringMode.Absolute, Colors = new List<Color> { Color.Black, Color.FromArgb(255, 0, 100, 100), Color.FromArgb(255, 72, 209, 204), Color.FromArgb(255, 224, 255, 255), Color.White }, ExponentRange = 2.0, ZeroBandWidth = 0.05, IsBuiltIn = true },
                new() { Name = "Лава", Mode = LyapunovColoringMode.ZeroBandHighlight, Colors = new List<Color> { Color.FromArgb(255, 139, 0, 0), Color.FromArgb(255, 255, 69, 0), Color.FromArgb(255, 255, 140, 0), Color.FromArgb(255, 255, 255, 224) }, ExponentRange = 1.9, ZeroBandWidth = 0.02, IsBuiltIn = true },
                new() { Name = "Монохром синий", Mode = LyapunovColoringMode.ZeroBandHighlight, Colors = new List<Color> { Color.FromArgb(255, 0, 0, 139), Color.FromArgb(255, 65, 105, 225), Color.FromArgb(255, 176, 224, 230), Color.White }, ExponentRange = 1.8, ZeroBandWidth = 0.025, IsBuiltIn = true },
                new() { Name = "Монохром красный", Mode = LyapunovColoringMode.Absolute, Colors = new List<Color> { Color.Black, Color.FromArgb(255, 139, 0, 0), Color.FromArgb(255, 220, 20, 60), Color.FromArgb(255, 255, 182, 193), Color.White }, ExponentRange = 2.0, ZeroBandWidth = 0.05, IsBuiltIn = true }
            };
        }
    }
}
