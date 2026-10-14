using System.IO;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;

internal static partial class Program
{
    /// <summary>
    /// Хранилища пользовательских палитр всех режимов (кроме Мандельброта — у него своя подробная
    /// проверка в <see cref="VerifyPalettePersistence"/>) и списки избранного и недавних: файл,
    /// который не прочитался, не перезаписывается, причина видна окну и попадает в журнал.
    /// </summary>
    private static void VerifyPaletteFileProtection()
    {
        // Имя файла, создание хранилища и действия над ним: предупреждение, сохранение текущего
        // списка, «все палитры встроенные».
        (string File, Func<(string? Warning, Action Save, Func<bool> OnlyBuiltIns)> Open)[] stores =
        [
            ("buddhabrot_palettes.json", () => { var m = new BuddhabrotPaletteManager(); return (m.LoadWarning, m.Save, () => m.Palettes.All(p => p.IsBuiltIn)); }),
            ("custom_palettes_domain_coloring.json", () => { var m = new DomainColoringPaletteManager(); return (m.LoadWarning, m.SaveCustomPalettes, () => m.Palettes.All(p => p.IsBuiltIn)); }),
            ("custom_palettes_gray_scott.json", () => { var m = new GrayScottPaletteManager(); return (m.LoadWarning, m.SaveCustomPalettes, () => m.Palettes.All(p => p.IsBuiltIn)); }),
            ("custom_palettes_inverse_collatz.json", () => { var m = new InverseCollatzPaletteManager(); return (m.LoadWarning, m.SaveCustomPalettes, () => m.Palettes.All(p => p.IsBuiltIn)); }),
            ("newton_palettes.json", () => { var m = new NewtonPaletteManager(); return (m.LoadWarning, () => { m.ReloadCustomPalettes(); m.SaveCustomPalettes(); }, () => m.Palettes.All(p => p.IsBuiltIn)); }),
            ("serpinsky_palettes.json", () => { var m = new SerpinskyPaletteManager(); return (m.LoadWarning, m.SaveCustomPalettes, () => m.Palettes.All(p => p.IsBuiltIn)); }),
            ("custom_palettes_fractal3d.json", () => { var m = new Fractal3DPaletteManager(); return (m.LoadWarning, m.SaveCustomPalettes, () => m.Palettes.All(p => p.IsBuiltIn)); }),
            ("custom_palettes_ifs3d.json", () => { var m = new Ifs3DPaletteManager(); return (m.LoadWarning, m.SaveCustomPalettes, () => m.Palettes.All(p => p.IsBuiltIn)); }),
            ("lyapunov_palettes.json", () => DynamicStore(new DynamicPaletteStore(DynamicSystemKind.Lyapunov))),
            ("hopalong_palettes.json", () => DynamicStore(new DynamicPaletteStore(DynamicSystemKind.Hopalong))),
            ("turing_palettes.json", () => DynamicStore(new DynamicPaletteStore("turing_palettes.json", TuringPalettes.All())))
        ];

        static (string?, Action, Func<bool>) DynamicStore(DynamicPaletteStore store)
        {
            List<DynamicPalette> palettes = store.Load();
            return (store.LoadWarning, () => store.Save(palettes), () => palettes.All(p => p.IsBuiltIn));
        }

        using var sandbox = DataSandbox.Create("palette-files");
        foreach ((string fileName, var open) in stores)
        {
            string path = AppPaths.GetPaletteFile(fileName);
            if (File.Exists(path)) File.Delete(path);
            var (warning, save, onlyBuiltIns) = open();
            Check(warning is null, $"{fileName}: a missing palette file is not a load error.");
            save();
            Check(File.Exists(path) && File.ReadAllText(path).Trim() == "[]" &&
                  !Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp").Any(),
                $"{fileName}: saving an empty library writes the file atomically.");

            foreach (string damaged in new[] { "{ damaged", "null" })
            {
                File.WriteAllText(path, damaged);
                (warning, save, onlyBuiltIns) = open();
                Check(warning?.Contains(path) == true && onlyBuiltIns(),
                    $"{fileName}: a damaged file ({damaged}) is reported and nothing of it is loaded.");
                save();
                Check(File.ReadAllText(path) == damaged,
                    $"{fileName}: saving after a failed load must not overwrite the damaged file.");
            }

            File.WriteAllText(path, "[]");
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                (warning, save, onlyBuiltIns) = open();
            save();
            Check(warning is not null && File.ReadAllText(path) == "[]" &&
                  File.ReadAllText(CrashLogger.LogFilePath).Contains(path),
                $"{fileName}: an unreadable file blocks saving and is recorded in the diagnostic log.");
            Check(open().Warning is null, $"{fileName}: a restored file loads again in a new store.");
        }

        foreach ((string name, Action lockedLoad, Action save, Func<bool> loads) in new (string, Action, Action, Func<bool>)[]
                 {
                     ("favorite_fractals.txt", () => FavoriteFractalsStore.Load(), () => FavoriteFractalsStore.Save(["Other"]),
                         () => FavoriteFractalsStore.Load().SetEquals(["Kept"])),
                     ("recent_fractals.txt", () => RecentFractalsStore.Load(), () => RecentFractalsStore.Save(["Other"]),
                         () => RecentFractalsStore.Load().SequenceEqual(["Kept"]))
                 })
        {
            string path = AppPaths.GetSettingsFile(name);
            File.WriteAllLines(AppPaths.EnsureDirectoryFor(path), ["Kept"]);
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                lockedLoad();
            save();
            Check(loads(), $"{name}: a list that failed to load is not overwritten by the next save.");
            save();
            Check(File.ReadAllLines(path).SequenceEqual(["Other"]), $"{name}: after a successful load saving works again.");
        }
    }
}
