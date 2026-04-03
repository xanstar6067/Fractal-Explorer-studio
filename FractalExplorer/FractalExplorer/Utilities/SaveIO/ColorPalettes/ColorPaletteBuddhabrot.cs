using FractalExplorer.Utilities.JsonConverters;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FractalExplorer.Utilities.SaveIO.ColorPalettes
{
    public enum BuddhabrotColoringMode
    {
        Logarithmic = 0,
        Sqrt = 1,
        Linear = 2
    }

    public sealed class BuddhabrotColorPalette
    {
        public string Name { get; set; } = "Новая палитра";
        public List<Color> Colors { get; set; } = new();
        public bool IsGradient { get; set; } = true;
        public int MaxColorIterations { get; set; } = 500;
        public bool AlignWithRenderIterations { get; set; }
        public double Gamma { get; set; } = 1.0;
        public BuddhabrotColoringMode ColoringMode { get; set; } = BuddhabrotColoringMode.Logarithmic;

        [JsonIgnore]
        public bool IsBuiltIn { get; set; }

        public BuddhabrotColorPalette CloneAsCustom(string name)
        {
            return new BuddhabrotColorPalette
            {
                Name = name,
                Colors = Colors.ToList(),
                IsGradient = IsGradient,
                MaxColorIterations = MaxColorIterations,
                AlignWithRenderIterations = AlignWithRenderIterations,
                Gamma = Gamma,
                ColoringMode = ColoringMode,
                IsBuiltIn = false
            };
        }
    }

    public sealed class BuddhabrotPaletteManager
    {
        private const string PaletteFile = "buddhabrot_palettes.json";

        public List<BuddhabrotColorPalette> Palettes { get; } = new();
        public BuddhabrotColorPalette ActivePalette { get; set; }

        public BuddhabrotPaletteManager()
        {
            LoadPalettes();
            ActivePalette = Palettes.FirstOrDefault() ?? CreateDefaultBuiltInPalette();
        }

        public static BuddhabrotColorPalette CreateDefaultBuiltInPalette()
        {
            return new BuddhabrotColorPalette
            {
                Name = "Стандартный Ч/Б",
                Colors = new List<Color> { Color.Black, Color.White },
                IsGradient = true,
                MaxColorIterations = 500,
                AlignWithRenderIterations = false,
                Gamma = 1.0,
                ColoringMode = BuddhabrotColoringMode.Linear,
                IsBuiltIn = true
            };
        }

        private static BuddhabrotColorPalette CreateClassicBuiltInPalette()
        {
            return new BuddhabrotColorPalette
            {
                Name = "Классический Buddhabrot",
                Colors = new List<Color> { Color.Black, Color.DarkBlue, Color.Cyan, Color.White },
                IsGradient = true,
                MaxColorIterations = 500,
                AlignWithRenderIterations = false,
                Gamma = 1.0,
                ColoringMode = BuddhabrotColoringMode.Logarithmic,
                IsBuiltIn = true
            };
        }

        private void LoadPalettes()
        {
            Palettes.Clear();
            Palettes.Add(CreateDefaultBuiltInPalette());
            Palettes.Add(CreateClassicBuiltInPalette());
            Palettes.Add(new BuddhabrotColorPalette
            {
                Name = "Fire",
                Colors = new List<Color> { Color.Black, Color.DarkRed, Color.OrangeRed, Color.Gold, Color.White },
                IsGradient = true,
                MaxColorIterations = 420,
                Gamma = 0.95,
                ColoringMode = BuddhabrotColoringMode.Logarithmic,
                IsBuiltIn = true
            });
            Palettes.Add(new BuddhabrotColorPalette
            {
                Name = "Спектральная",
                Colors = new List<Color> { Color.Black, Color.DarkViolet, Color.Cyan, Color.Lime, Color.Yellow, Color.White },
                IsGradient = true,
                MaxColorIterations = 700,
                Gamma = 1.05,
                ColoringMode = BuddhabrotColoringMode.Sqrt,
                IsBuiltIn = true
            });
            Palettes.Add(new BuddhabrotColorPalette
            {
                Name = "Дискретная",
                Colors = new List<Color> { Color.Black, Color.DarkBlue, Color.Teal, Color.Olive, Color.DarkRed, Color.White },
                IsGradient = false,
                MaxColorIterations = 36,
                Gamma = 1.0,
                ColoringMode = BuddhabrotColoringMode.Linear,
                IsBuiltIn = true
            });

            string filePath = Path.Combine(Application.StartupPath, "Saves", PaletteFile);
            if (!File.Exists(filePath))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                var options = new JsonSerializerOptions();
                options.Converters.Add(new JsonColorConverter());
                var loaded = JsonSerializer.Deserialize<List<BuddhabrotColorPalette>>(json, options);
                if (loaded != null)
                {
                    Palettes.AddRange(loaded.Where(p => p != null));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось загрузить палитры Buddhabrot: {ex.Message}", "Ошибка загрузки палитр", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                options.Converters.Add(new JsonColorConverter());
                string json = JsonSerializer.Serialize(custom, options);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось сохранить палитры Buddhabrot: {ex.Message}", "Ошибка сохранения палитр", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
