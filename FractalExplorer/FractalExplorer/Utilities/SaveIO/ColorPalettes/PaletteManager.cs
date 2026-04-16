using FractalExplorer.Utilities.JsonConverters;
using System.Text.Json;

namespace FractalExplorer.Utilities.SaveIO.ColorPalettes
{
    /// <summary>
    /// Управляет коллекцией цветовых палитр, включая загрузку, сохранение и создание встроенных палитр.
    /// </summary>
    public class PaletteManager
    {
        protected virtual string CustomPalettesFileName => "custom_palettes_mandelbrot.json";
        protected virtual string PaletteSubjectName => "Мандельброта";

        /// <summary>
        /// Список всех доступных палитр (встроенных и пользовательских).
        /// </summary>
        public List<Palette> Palettes { get; private set; }

        /// <summary>
        /// Текущая активная палитра, используемая для рендеринга.
        /// </summary>
        public Palette ActivePalette { get; set; }

        public PaletteManager()
        {
            Palettes = new List<Palette>();
            LoadPalettes();
            ActivePalette ??= Palettes.FirstOrDefault();
        }

        private void LoadPalettes()
        {
            Palettes.Clear();
            Palettes.AddRange(CreateBuiltInPalettes());

            string filePath = Path.Combine(Application.StartupPath, "Saves", CustomPalettesFileName);

            if (File.Exists(filePath))
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    var options = new JsonSerializerOptions();
                    options.Converters.Add(new JsonColorConverter());
                    var customPalettes = JsonSerializer.Deserialize<List<Palette>>(json, options);
                    if (customPalettes != null)
                    {
                        Palettes.AddRange(customPalettes);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось загрузить палитры для {PaletteSubjectName}: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        #region Public Methods for Palette Management

        /// <summary>
        /// Безопасно добавляет новую палитру в коллекцию, избегая дубликатов.
        /// </summary>
        /// <param name="palette">Палитра для добавления.</param>
        public void AddPalette(Palette palette)
        {
            if (palette != null && !Palettes.Contains(palette))
            {
                Palettes.Add(palette);
            }
        }

        /// <summary>
        /// Удаляет указанную пользовательскую палитру из коллекции. Встроенные палитры удалить нельзя.
        /// </summary>
        /// <param name="paletteToRemove">Палитра для удаления.</param>
        /// <returns>true, если удаление прошло успешно; иначе false.</returns>
        public bool RemovePalette(Palette paletteToRemove)
        {
            // Нельзя удалять встроенные палитры
            if (paletteToRemove == null || paletteToRemove.IsBuiltIn)
            {
                return false;
            }

            // Удаляем из нашего основного списка
            return Palettes.Remove(paletteToRemove);
        }

        #endregion

        public void SaveCustomPalettes()
        {
            try
            {
                var customPalettes = Palettes.Where(p => !p.IsBuiltIn).ToList();
                var options = new JsonSerializerOptions { WriteIndented = true };
                options.Converters.Add(new JsonColorConverter());
                string json = JsonSerializer.Serialize(customPalettes, options);

                // ИЗМЕНЕНО: Создание директории "Saves", если она не существует
                string savesDirectory = Path.Combine(Application.StartupPath, "Saves");
                Directory.CreateDirectory(savesDirectory);

                // ИЗМЕНЕНО: Формирование полного пути для сохранения
                string filePath = Path.Combine(savesDirectory, CustomPalettesFileName);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось сохранить палитры для {PaletteSubjectName}: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Создает коллекцию встроенных (предопределенных) цветовых палитр.
        /// </summary>
        /// <returns>Коллекция встроенных палитр.</returns>
        protected virtual IEnumerable<Palette> CreateBuiltInPalettes()
        {
            return new List<Palette>
            {
                // Оригинальные палитры
                new("Стандартный серый", new List<Color>(), true, true, 800, 1.0, false),
                new("Ультрафиолет", new List<Color> { Color.Black, Color.DarkViolet, Color.Violet, Color.White }, true, true, 1000, 1.2, false),
                new("Огонь", new List<Color> { Color.Black, Color.DarkRed, Color.Red, Color.Orange, Color.Yellow, Color.White }, true, true, 400, 0.9, false),
                new("Лёд", new List<Color> { Color.Black, Color.DarkBlue, Color.Blue, Color.Cyan, Color.White }, true, true, 500, 1.2, false),
                new("Огонь и лед", new List<Color> { Color.Black, Color.DarkBlue, Color.Cyan, Color.White, Color.Yellow, Color.Red, Color.DarkRed }, true, true, 700, 1.0, false),
                new("Психоделика", new List<Color> { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta }, false, true, 6, 1.0, false),
                new("Черно-белый", new List<Color> { Color.Black, Color.White }, true, true, 500, 1.0, false),
                new("Зеленый", new List<Color> { Color.Black, Color.FromArgb(255, 0, 128, 0), Color.FromArgb(255, 0, 204, 0), Color.FromArgb(255, 0, 234, 0), Color.FromArgb(255, 60, 255, 60), Color.FromArgb(255, 145, 255, 145), Color.FromArgb(255, 213, 255, 213), Color.White }, true, true, 120, 1.0, false),
                new("Сепия", new List<Color> { Color.FromArgb(20, 10, 0), Color.FromArgb(255, 240, 192) }, true, true, 500, 1.0, false),

                // Новые палитры с белым фоном
                new("Белый ультрафиолет", new List<Color> { Color.White, Color.Lavender, Color.Violet, Color.DarkViolet, Color.Indigo, Color.Black }, true, true, 400, 1.2, false),
                new("Белый огонь", new List<Color> { Color.White, Color.LightYellow, Color.Yellow, Color.Orange, Color.Red, Color.DarkRed, Color.Maroon }, true, true, 400, 0.9, false),
                new("Белый лед", new List<Color> { Color.White, Color.LightCyan, Color.Cyan, Color.DeepSkyBlue, Color.Blue, Color.DarkBlue, Color.Navy }, true, true, 500, 1.2, false),
                new("Белый зеленый", new List<Color> { Color.White, Color.FromArgb(255, 230, 255, 230), Color.FromArgb(255, 180, 255, 180), Color.FromArgb(255, 120, 255, 120), Color.FromArgb(255, 60, 220, 60), Color.FromArgb(255, 0, 180, 0), Color.FromArgb(255, 0, 120, 0), Color.Black }, true, true, 420, 1.0, false),
                new("Бело-черный", new List<Color> { Color.White, Color.Black }, true, true, 500, 1.0, false),

                // Новые оригинальные палитры
                new("Закат", new List<Color> { Color.Black, Color.FromArgb(255, 25, 25, 112), Color.FromArgb(255, 75, 0, 130), Color.FromArgb(255, 139, 0, 139), Color.FromArgb(255, 220, 20, 60), Color.FromArgb(255, 255, 140, 0), Color.FromArgb(255, 255, 215, 0), Color.White }, true, true, 600, 1.1, false),
                new("Океан", new List<Color> { Color.Black, Color.FromArgb(255, 0, 20, 40), Color.FromArgb(255, 0, 50, 80), Color.FromArgb(255, 0, 100, 150), Color.FromArgb(255, 0, 150, 200), Color.FromArgb(255, 100, 200, 255), Color.FromArgb(255, 200, 240, 255), Color.White }, true, true, 450, 1.0, false),
                new("Золото", new List<Color> { Color.Black, Color.FromArgb(255, 85, 65, 0), Color.FromArgb(255, 139, 115, 0), Color.FromArgb(255, 205, 173, 0), Color.FromArgb(255, 255, 215, 0), Color.FromArgb(255, 255, 235, 128), Color.FromArgb(255, 255, 248, 220), Color.White }, true, true, 300, 0.8, false),
                new("Медь", new List<Color> { Color.Black, Color.FromArgb(255, 72, 61, 20), Color.FromArgb(255, 138, 54, 15), Color.FromArgb(255, 184, 115, 51), Color.FromArgb(255, 205, 127, 50), Color.FromArgb(255, 240, 147, 43), Color.FromArgb(255, 255, 200, 124), Color.White }, true, true, 280, 0.9, false),
                new("Неон", new List<Color> { Color.Black, Color.FromArgb(255, 75, 0, 75), Color.FromArgb(255, 255, 0, 255), Color.FromArgb(255, 0, 255, 255), Color.FromArgb(255, 0, 255, 0), Color.FromArgb(255, 255, 255, 0), Color.FromArgb(255, 255, 100, 255), Color.White }, true, true, 350, 1.3, false),
                new("Радуга", new List<Color> { Color.Black, Color.FromArgb(255, 148, 0, 211), Color.FromArgb(255, 75, 0, 130), Color.FromArgb(255, 0, 0, 255), Color.FromArgb(255, 0, 255, 0), Color.FromArgb(255, 255, 255, 0), Color.FromArgb(255, 255, 127, 0), Color.FromArgb(255, 255, 0, 0), Color.White }, true, true, 350, 1.0, false),
                new("Аметист", new List<Color> { Color.Black, Color.FromArgb(255, 25, 25, 112), Color.FromArgb(255, 72, 61, 139), Color.FromArgb(255, 123, 104, 238), Color.FromArgb(255, 147, 112, 219), Color.FromArgb(255, 221, 160, 221), Color.FromArgb(255, 238, 203, 238), Color.White }, true, true, 520, 1.1, false),
                new("Лес", new List<Color> { Color.Black, Color.FromArgb(255, 0, 39, 0), Color.FromArgb(255, 0, 69, 0), Color.FromArgb(255, 34, 139, 34), Color.FromArgb(255, 50, 205, 50), Color.FromArgb(255, 124, 252, 0), Color.FromArgb(255, 173, 255, 47), Color.White }, true, true, 380, 1.0, false),
                new("Космос", new List<Color> { Color.Black, Color.FromArgb(255, 25, 25, 112), Color.FromArgb(255, 72, 61, 139), Color.FromArgb(255, 138, 43, 226), Color.FromArgb(255, 255, 20, 147), Color.FromArgb(255, 255, 105, 180), Color.FromArgb(255, 255, 182, 193), Color.White }, true, true, 650, 1.2, false),
                new("Бирюза", new List<Color> { Color.Black, Color.FromArgb(255, 0, 100, 100), Color.FromArgb(255, 0, 139, 139), Color.FromArgb(255, 72, 209, 204), Color.FromArgb(255, 175, 238, 238), Color.FromArgb(255, 224, 255, 255), Color.White }, true, true, 420, 1.0, false),
                new("Лава", new List<Color> { Color.Black, Color.FromArgb(255, 139, 0, 0), Color.FromArgb(255, 205, 0, 0), Color.FromArgb(255, 255, 69, 0), Color.FromArgb(255, 255, 140, 0), Color.FromArgb(255, 255, 215, 0), Color.FromArgb(255, 255, 255, 224), Color.White }, true, true, 250, 0.7, false),
                new("Монохром синий", new List<Color> { Color.Black, Color.FromArgb(255, 0, 0, 139), Color.FromArgb(255, 0, 0, 205), Color.FromArgb(255, 65, 105, 225), Color.FromArgb(255, 135, 206, 235), Color.FromArgb(255, 176, 224, 230), Color.White }, true, true, 480, 1.0, false),
                new("Монохром красный", new List<Color> { Color.Black, Color.FromArgb(255, 139, 0, 0), Color.FromArgb(255, 205, 0, 0), Color.FromArgb(255, 220, 20, 60), Color.FromArgb(255, 255, 105, 180), Color.FromArgb(255, 255, 182, 193), Color.White }, true, true, 320, 1.0, false)
            };
        }
    }
}
