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
            Palettes.AddRange(CreateAdditionalBuiltInPalettes());

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

        private static IEnumerable<BuddhabrotColorPalette> CreateAdditionalBuiltInPalettes()
        {
            return new List<BuddhabrotColorPalette>
            {
                new()
                {
                    Name = "Fire",
                    Colors = new List<Color> { Color.Black, Color.DarkRed, Color.OrangeRed, Color.Gold, Color.White },
                    IsGradient = true,
                    MaxColorIterations = 420,
                    Gamma = 0.95,
                    ColoringMode = BuddhabrotColoringMode.Logarithmic,
                    IsBuiltIn = true
                },
                new()
                {
                    Name = "Спектральная",
                    Colors = new List<Color> { Color.Black, Color.DarkViolet, Color.Cyan, Color.Lime, Color.Yellow, Color.White },
                    IsGradient = true,
                    MaxColorIterations = 700,
                    Gamma = 1.05,
                    ColoringMode = BuddhabrotColoringMode.Sqrt,
                    IsBuiltIn = true
                },
                new()
                {
                    Name = "Дискретная",
                    Colors = new List<Color> { Color.Black, Color.DarkBlue, Color.Teal, Color.Olive, Color.DarkRed, Color.White },
                    IsGradient = false,
                    MaxColorIterations = 36,
                    Gamma = 1.0,
                    ColoringMode = BuddhabrotColoringMode.Linear,
                    IsBuiltIn = true
                },
                new()
                {
                    Name = "Ультрафиолет",
                    Colors = new List<Color> { Color.Black, Color.DarkViolet, Color.Violet, Color.White },
                    IsGradient = true,
                    MaxColorIterations = 520,
                    Gamma = 1.15,
                    ColoringMode = BuddhabrotColoringMode.Logarithmic,
                    IsBuiltIn = true
                },
                new()
                {
                    Name = "Лёд",
                    Colors = new List<Color> { Color.Black, Color.DarkBlue, Color.Blue, Color.Cyan, Color.White },
                    IsGradient = true,
                    MaxColorIterations = 560,
                    Gamma = 1.1,
                    ColoringMode = BuddhabrotColoringMode.Sqrt,
                    IsBuiltIn = true
                },
                new()
                {
                    Name = "Огонь и лед",
                    Colors = new List<Color> { Color.Black, Color.DarkBlue, Color.Cyan, Color.White, Color.Yellow, Color.Red, Color.DarkRed },
                    IsGradient = true,
                    MaxColorIterations = 760,
                    Gamma = 1.0,
                    ColoringMode = BuddhabrotColoringMode.Logarithmic,
                    IsBuiltIn = true
                },
                new()
                {
                    Name = "Закат",
                    Colors = new List<Color>
                    {
                        Color.Black, Color.FromArgb(255, 25, 25, 112), Color.FromArgb(255, 75, 0, 130),
                        Color.FromArgb(255, 139, 0, 139), Color.FromArgb(255, 220, 20, 60),
                        Color.FromArgb(255, 255, 140, 0), Color.FromArgb(255, 255, 215, 0), Color.White
                    },
                    IsGradient = true,
                    MaxColorIterations = 620,
                    Gamma = 1.08,
                    ColoringMode = BuddhabrotColoringMode.Logarithmic,
                    IsBuiltIn = true
                },
                new()
                {
                    Name = "Океан",
                    Colors = new List<Color>
                    {
                        Color.Black, Color.FromArgb(255, 0, 20, 40), Color.FromArgb(255, 0, 50, 80),
                        Color.FromArgb(255, 0, 100, 150), Color.FromArgb(255, 0, 150, 200),
                        Color.FromArgb(255, 100, 200, 255), Color.FromArgb(255, 200, 240, 255), Color.White
                    },
                    IsGradient = true,
                    MaxColorIterations = 540,
                    Gamma = 1.0,
                    ColoringMode = BuddhabrotColoringMode.Sqrt,
                    IsBuiltIn = true
                },
                new()
                {
                    Name = "Космос",
                    Colors = new List<Color>
                    {
                        Color.Black, Color.FromArgb(255, 25, 25, 112), Color.FromArgb(255, 72, 61, 139),
                        Color.FromArgb(255, 138, 43, 226), Color.FromArgb(255, 255, 20, 147),
                        Color.FromArgb(255, 255, 105, 180), Color.FromArgb(255, 255, 182, 193), Color.White
                    },
                    IsGradient = true,
                    MaxColorIterations = 780,
                    Gamma = 1.18,
                    ColoringMode = BuddhabrotColoringMode.Logarithmic,
                    IsBuiltIn = true
                },
                new()
                {
                    Name = "Бирюза",
                    Colors = new List<Color>
                    {
                        Color.Black, Color.FromArgb(255, 0, 100, 100), Color.FromArgb(255, 0, 139, 139),
                        Color.FromArgb(255, 72, 209, 204), Color.FromArgb(255, 175, 238, 238),
                        Color.FromArgb(255, 224, 255, 255), Color.White
                    },
                    IsGradient = true,
                    MaxColorIterations = 520,
                    Gamma = 1.0,
                    ColoringMode = BuddhabrotColoringMode.Sqrt,
                    IsBuiltIn = true
                },
                new()
                {
                    Name = "Лава",
                    Colors = new List<Color>
                    {
                        Color.Black, Color.FromArgb(255, 139, 0, 0), Color.FromArgb(255, 205, 0, 0),
                        Color.FromArgb(255, 255, 69, 0), Color.FromArgb(255, 255, 140, 0),
                        Color.FromArgb(255, 255, 215, 0), Color.FromArgb(255, 255, 255, 224), Color.White
                    },
                    IsGradient = true,
                    MaxColorIterations = 360,
                    Gamma = 0.82,
                    ColoringMode = BuddhabrotColoringMode.Linear,
                    IsBuiltIn = true
                },
                new()
                {
                    Name = "Монохром синий",
                    Colors = new List<Color>
                    {
                        Color.Black, Color.FromArgb(255, 0, 0, 139), Color.FromArgb(255, 0, 0, 205),
                        Color.FromArgb(255, 65, 105, 225), Color.FromArgb(255, 135, 206, 235),
                        Color.FromArgb(255, 176, 224, 230), Color.White
                    },
                    IsGradient = true,
                    MaxColorIterations = 600,
                    Gamma = 1.0,
                    ColoringMode = BuddhabrotColoringMode.Sqrt,
                    IsBuiltIn = true
                }
            };
        }
    }
}
