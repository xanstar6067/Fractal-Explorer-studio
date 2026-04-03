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
                Name = "Legacy built-in",
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
            Palettes.Add(new LyapunovColorPalette
            {
                Name = "Классическая Lyapunov",
                Mode = LyapunovColoringMode.Diverging,
                Colors = defaultBuiltIn.Colors.ToList(),
                ExponentRange = 2.0,
                ZeroBandWidth = 0.05,
                IsBuiltIn = true
            });
            Palettes.Add(new LyapunovColorPalette
            {
                Name = "Absolute / Spectral",
                Mode = LyapunovColoringMode.Absolute,
                Colors = new List<Color> { Color.Black, Color.MidnightBlue, Color.Cyan, Color.Yellow, Color.White },
                ExponentRange = 2.0,
                ZeroBandWidth = 0.05,
                IsBuiltIn = true
            });
            Palettes.Add(new LyapunovColorPalette
            {
                Name = "Zero band",
                Mode = LyapunovColoringMode.ZeroBandHighlight,
                Colors = new List<Color> { Color.DarkBlue, Color.CornflowerBlue, Color.White, Color.Orange, Color.DarkRed },
                ExponentRange = 2.0,
                ZeroBandWidth = 0.03,
                IsBuiltIn = true
            });
            Palettes.Add(new LyapunovColorPalette
            {
                Name = "Histogram equalized",
                Mode = LyapunovColoringMode.HistogramEqualized,
                Colors = new List<Color> { Color.Black, Color.DarkViolet, Color.DeepSkyBlue, Color.Lime, Color.Yellow, Color.White },
                ExponentRange = 2.0,
                ZeroBandWidth = 0.05,
                IsBuiltIn = true
            });
            Palettes.Add(new LyapunovColorPalette
            {
                Name = "Закат (Diverging)",
                Mode = LyapunovColoringMode.Diverging,
                Colors = new List<Color>
                {
                    Color.FromArgb(25, 25, 112),
                    Color.FromArgb(139, 0, 139),
                    Color.FromArgb(220, 20, 60),
                    Color.FromArgb(255, 140, 0),
                    Color.FromArgb(255, 215, 0)
                },
                ExponentRange = 2.2,
                ZeroBandWidth = 0.04,
                IsBuiltIn = true
            });
            Palettes.Add(new LyapunovColorPalette
            {
                Name = "Океан (Absolute)",
                Mode = LyapunovColoringMode.Absolute,
                Colors = new List<Color>
                {
                    Color.Black,
                    Color.FromArgb(0, 20, 40),
                    Color.FromArgb(0, 100, 150),
                    Color.FromArgb(100, 200, 255),
                    Color.White
                },
                ExponentRange = 2.0,
                ZeroBandWidth = 0.05,
                IsBuiltIn = true
            });
            Palettes.Add(new LyapunovColorPalette
            {
                Name = "Лава (Zero band)",
                Mode = LyapunovColoringMode.ZeroBandHighlight,
                Colors = new List<Color>
                {
                    Color.FromArgb(139, 0, 0),
                    Color.FromArgb(205, 0, 0),
                    Color.FromArgb(255, 69, 0),
                    Color.FromArgb(255, 140, 0),
                    Color.FromArgb(255, 255, 224)
                },
                ExponentRange = 1.9,
                ZeroBandWidth = 0.02,
                IsBuiltIn = true
            });
            Palettes.Add(new LyapunovColorPalette
            {
                Name = "Неон (Histogram)",
                Mode = LyapunovColoringMode.HistogramEqualized,
                Colors = new List<Color>
                {
                    Color.Black,
                    Color.FromArgb(75, 0, 75),
                    Color.Magenta,
                    Color.Cyan,
                    Color.Lime,
                    Color.Yellow,
                    Color.White
                },
                ExponentRange = 2.4,
                ZeroBandWidth = 0.05,
                IsBuiltIn = true
            });
            Palettes.Add(new LyapunovColorPalette
            {
                Name = "Лес (Diverging)",
                Mode = LyapunovColoringMode.Diverging,
                Colors = new List<Color>
                {
                    Color.FromArgb(0, 39, 0),
                    Color.FromArgb(34, 139, 34),
                    Color.FromArgb(124, 252, 0),
                    Color.FromArgb(173, 255, 47),
                    Color.White
                },
                ExponentRange = 2.1,
                ZeroBandWidth = 0.035,
                IsBuiltIn = true
            });
            Palettes.Add(new LyapunovColorPalette
            {
                Name = "Космос (Absolute)",
                Mode = LyapunovColoringMode.Absolute,
                Colors = new List<Color>
                {
                    Color.Black,
                    Color.FromArgb(25, 25, 112),
                    Color.FromArgb(72, 61, 139),
                    Color.FromArgb(138, 43, 226),
                    Color.FromArgb(255, 20, 147),
                    Color.FromArgb(255, 182, 193),
                    Color.White
                },
                ExponentRange = 2.5,
                ZeroBandWidth = 0.05,
                IsBuiltIn = true
            });
            Palettes.Add(new LyapunovColorPalette
            {
                Name = "Монохром синий (Zero band)",
                Mode = LyapunovColoringMode.ZeroBandHighlight,
                Colors = new List<Color>
                {
                    Color.FromArgb(0, 0, 139),
                    Color.FromArgb(0, 0, 205),
                    Color.FromArgb(65, 105, 225),
                    Color.FromArgb(135, 206, 235),
                    Color.White
                },
                ExponentRange = 1.8,
                ZeroBandWidth = 0.025,
                IsBuiltIn = true
            });
            Palettes.Add(new LyapunovColorPalette
            {
                Name = "Золото (Histogram)",
                Mode = LyapunovColoringMode.HistogramEqualized,
                Colors = new List<Color>
                {
                    Color.Black,
                    Color.FromArgb(85, 65, 0),
                    Color.FromArgb(205, 173, 0),
                    Color.FromArgb(255, 215, 0),
                    Color.FromArgb(255, 248, 220),
                    Color.White
                },
                ExponentRange = 2.0,
                ZeroBandWidth = 0.05,
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
    }
}
