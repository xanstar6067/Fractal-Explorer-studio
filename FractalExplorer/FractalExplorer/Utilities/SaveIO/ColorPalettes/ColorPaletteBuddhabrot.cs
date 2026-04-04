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
        public Guid Id { get; set; } = Guid.NewGuid();
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
                Id = Guid.NewGuid(),
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
                Id = CreateDeterministicBuiltInId("Стандартный Ч/Б"),
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
                Id = CreateDeterministicBuiltInId("Классический Буддаброт"),
                Name = "Классический Буддаброт",
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
                    foreach (BuddhabrotColorPalette palette in loaded.Where(p => p != null))
                    {
                        if (palette.Id == Guid.Empty)
                        {
                            palette.Id = Guid.NewGuid();
                        }
                    }
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
            var palettes = new List<BuddhabrotColorPalette>
            {
                new()
                {
                    Name = "Стандартный серый",
                    Colors = new List<Color> { Color.Black, Color.DimGray, Color.Silver, Color.White },
                    IsGradient = true,
                    MaxColorIterations = 500,
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
                    Name = "Огонь",
                    Colors = new List<Color> { Color.Black, Color.DarkRed, Color.OrangeRed, Color.Gold, Color.White },
                    IsGradient = true,
                    MaxColorIterations = 420,
                    Gamma = 0.95,
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
                    Name = "Психоделика",
                    Colors = new List<Color> { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta },
                    IsGradient = false,
                    MaxColorIterations = 24,
                    Gamma = 1.0,
                    ColoringMode = BuddhabrotColoringMode.Linear,
                    IsBuiltIn = true
                },
                new()
                {
                    Name = "Черно-белый",
                    Colors = new List<Color> { Color.Black, Color.White },
                    IsGradient = true,
                    MaxColorIterations = 500,
                    Gamma = 1.0,
                    ColoringMode = BuddhabrotColoringMode.Linear,
                    IsBuiltIn = true
                },
                new()
                {
                    Name = "Зеленый",
                    Colors = new List<Color> { Color.Black, Color.FromArgb(255, 0, 128, 0), Color.FromArgb(255, 0, 204, 0), Color.FromArgb(255, 60, 255, 60), Color.FromArgb(255, 213, 255, 213), Color.White },
                    IsGradient = true,
                    MaxColorIterations = 320,
                    Gamma = 1.0,
                    ColoringMode = BuddhabrotColoringMode.Logarithmic,
                    IsBuiltIn = true
                },
                new()
                {
                    Name = "Сепия",
                    Colors = new List<Color> { Color.FromArgb(20, 10, 0), Color.FromArgb(255, 240, 192) },
                    IsGradient = true,
                    MaxColorIterations = 460,
                    Gamma = 1.0,
                    ColoringMode = BuddhabrotColoringMode.Linear,
                    IsBuiltIn = true
                },
                new()
                {
                    Name = "Белый ультрафиолет",
                    Colors = new List<Color> { Color.White, Color.Lavender, Color.Violet, Color.DarkViolet, Color.Indigo, Color.Black },
                    IsGradient = true,
                    MaxColorIterations = 520,
                    Gamma = 1.12,
                    ColoringMode = BuddhabrotColoringMode.Logarithmic,
                    IsBuiltIn = true
                },
                new()
                {
                    Name = "Белый огонь",
                    Colors = new List<Color> { Color.White, Color.LightYellow, Color.Yellow, Color.Orange, Color.Red, Color.DarkRed, Color.Maroon },
                    IsGradient = true,
                    MaxColorIterations = 420,
                    Gamma = 0.95,
                    ColoringMode = BuddhabrotColoringMode.Logarithmic,
                    IsBuiltIn = true
                },
                new()
                {
                    Name = "Белый лед",
                    Colors = new List<Color> { Color.White, Color.LightCyan, Color.Cyan, Color.DeepSkyBlue, Color.Blue, Color.DarkBlue, Color.Navy },
                    IsGradient = true,
                    MaxColorIterations = 560,
                    Gamma = 1.1,
                    ColoringMode = BuddhabrotColoringMode.Sqrt,
                    IsBuiltIn = true
                },
                new()
                {
                    Name = "Белый зеленый",
                    Colors = new List<Color> { Color.White, Color.FromArgb(255, 180, 255, 180), Color.FromArgb(255, 60, 220, 60), Color.FromArgb(255, 0, 120, 0), Color.Black },
                    IsGradient = true,
                    MaxColorIterations = 340,
                    Gamma = 1.0,
                    ColoringMode = BuddhabrotColoringMode.Logarithmic,
                    IsBuiltIn = true
                },
                new()
                {
                    Name = "Бело-черный",
                    Colors = new List<Color> { Color.White, Color.Black },
                    IsGradient = true,
                    MaxColorIterations = 500,
                    Gamma = 1.0,
                    ColoringMode = BuddhabrotColoringMode.Linear,
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
                    Name = "Золото",
                    Colors = new List<Color>
                    {
                        Color.Black, Color.FromArgb(255, 85, 65, 0), Color.FromArgb(255, 139, 115, 0),
                        Color.FromArgb(255, 205, 173, 0), Color.FromArgb(255, 255, 215, 0),
                        Color.FromArgb(255, 255, 248, 220), Color.White
                    },
                    IsGradient = true,
                    MaxColorIterations = 380,
                    Gamma = 0.9,
                    ColoringMode = BuddhabrotColoringMode.Logarithmic,
                    IsBuiltIn = true
                },
                new()
                {
                    Name = "Медь",
                    Colors = new List<Color>
                    {
                        Color.Black, Color.FromArgb(255, 72, 61, 20), Color.FromArgb(255, 138, 54, 15),
                        Color.FromArgb(255, 184, 115, 51), Color.FromArgb(255, 240, 147, 43),
                        Color.FromArgb(255, 255, 200, 124), Color.White
                    },
                    IsGradient = true,
                    MaxColorIterations = 360,
                    Gamma = 0.95,
                    ColoringMode = BuddhabrotColoringMode.Linear,
                    IsBuiltIn = true
                },
                new()
                {
                    Name = "Неон",
                    Colors = new List<Color>
                    {
                        Color.Black, Color.FromArgb(255, 75, 0, 75), Color.FromArgb(255, 255, 0, 255),
                        Color.FromArgb(255, 0, 255, 255), Color.FromArgb(255, 0, 255, 0),
                        Color.FromArgb(255, 255, 255, 0), Color.FromArgb(255, 255, 100, 255), Color.White
                    },
                    IsGradient = true,
                    MaxColorIterations = 640,
                    Gamma = 1.25,
                    ColoringMode = BuddhabrotColoringMode.Sqrt,
                    IsBuiltIn = true
                },
                new()
                {
                    Name = "Радуга",
                    Colors = new List<Color>
                    {
                        Color.Black, Color.FromArgb(255, 148, 0, 211), Color.FromArgb(255, 75, 0, 130),
                        Color.FromArgb(255, 0, 0, 255), Color.FromArgb(255, 0, 255, 0),
                        Color.FromArgb(255, 255, 255, 0), Color.FromArgb(255, 255, 127, 0),
                        Color.FromArgb(255, 255, 0, 0), Color.White
                    },
                    IsGradient = true,
                    MaxColorIterations = 660,
                    Gamma = 1.05,
                    ColoringMode = BuddhabrotColoringMode.Sqrt,
                    IsBuiltIn = true
                },
                new()
                {
                    Name = "Аметист",
                    Colors = new List<Color>
                    {
                        Color.Black, Color.FromArgb(255, 25, 25, 112), Color.FromArgb(255, 72, 61, 139),
                        Color.FromArgb(255, 123, 104, 238), Color.FromArgb(255, 221, 160, 221),
                        Color.FromArgb(255, 238, 203, 238), Color.White
                    },
                    IsGradient = true,
                    MaxColorIterations = 620,
                    Gamma = 1.12,
                    ColoringMode = BuddhabrotColoringMode.Logarithmic,
                    IsBuiltIn = true
                },
                new()
                {
                    Name = "Лес",
                    Colors = new List<Color>
                    {
                        Color.Black, Color.FromArgb(255, 0, 39, 0), Color.FromArgb(255, 0, 69, 0),
                        Color.FromArgb(255, 34, 139, 34), Color.FromArgb(255, 50, 205, 50),
                        Color.FromArgb(255, 173, 255, 47), Color.White
                    },
                    IsGradient = true,
                    MaxColorIterations = 500,
                    Gamma = 1.0,
                    ColoringMode = BuddhabrotColoringMode.Logarithmic,
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
                },
                new()
                {
                    Name = "Монохром красный",
                    Colors = new List<Color>
                    {
                        Color.Black, Color.FromArgb(255, 139, 0, 0), Color.FromArgb(255, 205, 0, 0),
                        Color.FromArgb(255, 220, 20, 60), Color.FromArgb(255, 255, 105, 180),
                        Color.FromArgb(255, 255, 182, 193), Color.White
                    },
                    IsGradient = true,
                    MaxColorIterations = 560,
                    Gamma = 1.0,
                    ColoringMode = BuddhabrotColoringMode.Sqrt,
                    IsBuiltIn = true
                }
            };

            foreach (BuddhabrotColorPalette palette in palettes)
            {
                palette.Id = CreateDeterministicBuiltInId(palette.Name);
            }

            return palettes;
        }

        private static Guid CreateDeterministicBuiltInId(string name)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes($"builtin-buddhabrot:{name}");
            byte[] hash = System.Security.Cryptography.SHA256.HashData(bytes);
            return new Guid(hash.AsSpan(0, 16));
        }
    }
}
