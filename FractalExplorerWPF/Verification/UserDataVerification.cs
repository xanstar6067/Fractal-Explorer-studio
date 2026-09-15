using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Migrations;
using FractalExplorerWPF.Models;

// Пользовательские данные: сохранения по файлу на запись, Корзина вместо удаления, версии
// формата, шаги миграции и перенос папки Saves прежних версий. Всё — во временном каталоге
// рядом с проверочным exe; настоящие данные и настоящая Корзина не затрагиваются.
internal static partial class Program
{
    private static void VerifyUserData()
    {
        VerifySaveStoreFiles();
        VerifyTypedSaveRoundTrip();
        VerifySaveFormatUpgrades();
        VerifyUserDataMigrator();
        VerifyLegacyImport();
        Console.WriteLine("[diag] User data: per-file saves, Recycle Bin, format versions, migrations and legacy import OK");
    }

    private static void VerifySaveStoreFiles()
    {
        using var sandbox = DataSandbox.Create("store");
        var store = new FractalSaveStore<State>("Store", state => state.Name);
        Check(store.LoadSlots().Slots.Count == 0 && !Directory.Exists(store.DirectoryPath),
            "A missing folder must mean no saves and must not be created by loading.");

        SaveSlot<State> colon = store.Save(new State("a:b", new DateTime(2026, 2, 1)));
        SaveSlot<State> underscore = store.Save(new State("a_b", new DateTime(2026, 2, 2)));
        SaveSlot<State> device = store.Save(new State("CON", new DateTime(2026, 2, 3)));
        Check(Path.GetFileName(colon.FilePath) == "a_b.json" && Path.GetFileName(underscore.FilePath) == "a_b (2).json",
            "Names with the same safe file name must get distinct files.");
        Check(Path.GetFileName(device.FilePath) == "_CON.json", "Reserved device names must be escaped.");
        Check(Directory.EnumerateFiles(store.DirectoryPath).Count() == 3, "Exactly one file per save and no temporary leftovers.");
        string colonJson = File.ReadAllText(colon.FilePath);
        Check(JsonNode.Parse(colonJson)!.AsObject().First().Key == SaveFormat.VersionProperty,
            "The format version must be the first property of a save file.");

        File.WriteAllBytes(colon.PreviewPath, [1, 2, 3]);
        SaveSlot<State> replaced = store.Save(new State("a:b", new DateTime(2026, 3, 1)), colon);
        Check(replaced.FilePath == colon.FilePath, "Overwriting must keep the file name.");
        Check(sandbox.Recycled.Count == 2 &&
              sandbox.Recycled.Any(item => item.Original == colon.FilePath && File.ReadAllText(item.Stored) == colonJson) &&
              sandbox.Recycled.Any(item => item.Original == colon.PreviewPath && File.ReadAllBytes(item.Stored).SequenceEqual(new byte[] { 1, 2, 3 })),
            "Overwriting must move the previous JSON and preview to the Recycle Bin.");
        Check(!File.Exists(colon.PreviewPath), "The previous preview must not stay next to the new state.");

        string broken = Path.Combine(store.DirectoryPath, "broken.json");
        File.WriteAllText(broken, "{ not json");
        File.WriteAllText(Path.Combine(store.DirectoryPath, "array.json"), "[]");
        File.WriteAllText(Path.Combine(store.DirectoryPath, "notes.txt"), "not a save");
        SaveLoadResult<State> loaded = store.LoadSlots();
        Check(loaded.Slots.Count == 3 && loaded.DamagedFiles.Count == 2,
            "Damaged files must be reported without hiding the other saves.");
        Check(loaded.Slots.Single(slot => slot.State.Name == "a:b").State.Timestamp == new DateTime(2026, 3, 1),
            "The overwritten state must load.");

        File.WriteAllBytes(underscore.PreviewPath, [4]);
        store.Delete(loaded.Slots.Single(slot => slot.State.Name == "a_b"));
        Check(!File.Exists(underscore.FilePath) && !File.Exists(underscore.PreviewPath) && sandbox.Recycled.Count == 4,
            "Deleting must move the JSON and its preview to the Recycle Bin.");
        Check(File.Exists(broken), "Damaged files must never be touched.");

        var filtered = new FractalSaveStore<State>("Store", state => state.Name, state => state.Name != "CON");
        Check(filtered.Load().Select(state => state.Name).SequenceEqual(["a:b"]), "prepareLoaded=false must skip a save.");
        Check(store.GetPointOfInterestPreviewPath("x/y") == Path.Combine(AppPaths.PointsOfInterestRoot, "Store", "x_y.png"),
            "Points-of-interest previews must live in their own folder.");

        // Корзина недоступна — прежнее сохранение остаётся нетронутым, временных файлов нет.
        string deviceJson = File.ReadAllText(device.FilePath);
        RecycleBin.SendOverrideForTests = _ => throw new IOException("bin unavailable");
        try
        {
            bool threw = false;
            try { store.Save(new State("CON", new DateTime(2026, 4, 1)), device); }
            catch (IOException) { threw = true; }
            Check(threw && File.ReadAllText(device.FilePath) == deviceJson,
                "If the Recycle Bin refuses the old file, the save must stay untouched.");
            Check(!Directory.EnumerateFiles(store.DirectoryPath, "*.tmp").Any(), "A failed save must not leave temporary files.");
        }
        finally
        {
            sandbox.InstallRecycleBin();
        }
    }

    // Состояние проходит SerializeToNode + отметку версии + JsonObject.Deserialize без потерь:
    // сериализация загруженного совпадает с сериализацией исходного.
    private static void VerifyTypedSaveRoundTrip()
    {
        using var sandbox = DataSandbox.Create("typed");
        JsonSerializerOptions options = JsonOptionsFactory.Create();

        MandelbrotState mandelbrot = PresetManager.GetMandelbrotPresets(MandelbrotVariant.Mandelbrot)[0];
        mandelbrot.Timestamp = new DateTime(2026, 9, 14, 23, 46, 58, DateTimeKind.Local).AddTicks(4213849);
        var mandelbrotStore = new MandelbrotSaveStore(MandelbrotVariant.Mandelbrot);
        mandelbrotStore.Save(mandelbrot);
        MandelbrotState loadedMandelbrot = mandelbrotStore.Load().Single();
        Check(JsonSerializer.Serialize(loadedMandelbrot, options) == JsonSerializer.Serialize(mandelbrot, options),
            "A Mandelbrot state must survive the per-file save losslessly.");

        foreach (PhoenixState phoenix in PresetManager.GetPhoenixPresets()) new PhoenixSaveStore().Save(phoenix);
        List<PhoenixState> phoenixes = new PhoenixSaveStore().Load();
        Check(phoenixes.Count == PresetManager.GetPhoenixPresets().Count &&
              PresetManager.GetPhoenixPresets().All(preset => phoenixes.Any(state =>
                  JsonSerializer.Serialize(state, options) == JsonSerializer.Serialize(preset, options))),
            "Phoenix states must survive the per-file save losslessly.");

        // Каталог окна динамических систем задаёт Kind при чтении, как делал прежний список.
        var lorenz = new DynamicSystemState { Kind = DynamicSystemKind.Henon, SaveName = "Lorenz", Timestamp = DateTime.Now };
        new DynamicSystemSaveStore(DynamicSystemKind.Lorenz).Save(lorenz);
        Check(new DynamicSystemSaveStore(DynamicSystemKind.Lorenz).Load().Single().Kind == DynamicSystemKind.Lorenz,
            "Dynamic system saves must be normalized to the store's kind.");
    }

    private static void VerifySaveFormatUpgrades()
    {
        using var sandbox = DataSandbox.Create("format");
        var store = new FractalSaveStore<State>("Format", state => state.Name);
        Directory.CreateDirectory(store.DirectoryPath);
        string old = Path.Combine(store.DirectoryPath, "old.json");
        File.WriteAllText(old, """{ "Title": "Old", "Timestamp": "2026-01-01T00:00:00" }""");

        Check(SaveFormat.CurrentVersion == 1, "The shipped save format must start at version 1.");
        SaveFormat.UpgradesOverrideForTests =
        [
            new SaveFormatUpgrade("Title → Name", (category, save) =>
            {
                Check(category == "Format", "An upgrade must receive the save category.");
                save["Name"] = save["Title"]?.DeepClone();
                save.Remove("Title");
            })
        ];
        Check(SaveFormat.CurrentVersion == 2, "Each upgrade must raise the current format version.");

        SaveSlot<State> slot = store.LoadSlots().Slots.Single();
        Check(slot.State.Name == "Old", "A version-1 file must be upgraded in memory on load.");
        Check(File.ReadAllText(old).Contains("\"Title\""), "Loading must not rewrite the file on disk.");
        store.Save(slot.State, slot);
        Check(SaveFormat.ReadVersion(JsonNode.Parse(File.ReadAllText(old))!.AsObject()) == 2,
            "Saving must write the current format version.");
        Check(store.LoadSlots().Slots.Single().State.Name == "Old", "An already-upgraded file must not be upgraded twice.");

        string future = Path.Combine(store.DirectoryPath, "future.json");
        File.WriteAllText(future, $$"""{ "{{SaveFormat.VersionProperty}}": 9, "Name": "Future", "Timestamp": "2026-01-01T00:00:00", "Unknown": 1 }""");
        Check(store.Load().Any(state => state.Name == "Future"), "A file from a newer format must still load best-effort.");

        SaveFormat.UpgradesOverrideForTests = [new SaveFormatUpgrade("broken", (_, _) => throw new FormatException("upgrade bug"))];
        File.WriteAllText(Path.Combine(store.DirectoryPath, "v1.json"), """{ "Name": "V1", "Timestamp": "2026-01-01T00:00:00" }""");
        SaveLoadResult<State> result = store.LoadSlots();
        Check(result.DamagedFiles.Count == 1 && result.Slots.Count == 2,
            "A failing upgrade must mark only that file as unreadable.");
    }

    private static void VerifyUserDataMigrator()
    {
        using var sandbox = DataSandbox.Create("migrator");
        Check(UserDataMigrator.CurrentVersion == 1, "The shipped data layout must be version 1.");

        var calls = new List<int>();
        string settings = AppPaths.EnsureDirectoryFor(AppPaths.GetSettingsFile("test.txt"));
        File.WriteAllText(settings, "old");
        IUserDataMigration[] two =
        [
            new TestMigration(1, _ => calls.Add(1)),
            new TestMigration(2, context => { calls.Add(2); context.RewriteFile(settings, "new"); })
        ];

        UserDataMigrationResult first = UserDataMigrator.Run(two);
        Check(first.Error is null && first.StartVersion == 0 && first.FinalVersion == 2 && calls.SequenceEqual([1, 2]),
            "Pending migrations must run in order.");
        Check(File.ReadAllText(settings) == "new", "RewriteFile must write the new contents.");
        string backup = Directory.EnumerateFiles(Path.Combine(AppPaths.DataRoot, "Backups"), "test.txt", SearchOption.AllDirectories).Single();
        Check(File.ReadAllText(backup) == "old" && backup.Contains("v1-v2"), "RewriteFile must back up the previous contents per step.");

        calls.Clear();
        UserDataMigrationResult again = UserDataMigrator.Run(two);
        Check(again.StartVersion == 2 && again.FinalVersion == 2 && calls.Count == 0 && !again.HasMessageForUser,
            "Applied migrations must not run again.");

        IUserDataMigration[] failing = [.. two, new TestMigration(3, _ => { calls.Add(3); throw new IOException("disk"); })];
        UserDataMigrationResult failed = UserDataMigrator.Run(failing);
        Check(failed.Error is not null && failed.FinalVersion == 2 && calls.SequenceEqual([3]),
            "A failing step must stop at the last good version.");
        calls.Clear();
        UserDataMigrationResult retried = UserDataMigrator.Run([.. two, new TestMigration(3, _ => calls.Add(3))]);
        Check(retried.Error is null && retried.FinalVersion == 3 && calls.SequenceEqual([3]),
            "A failed step must be retried on the next start.");

        calls.Clear();
        UserDataMigrationResult newer = UserDataMigrator.Run(two);
        Check(newer.FinalVersion == 3 && calls.Count == 0 && newer.Notes.Count == 1 && newer.Error is null,
            "Data from a newer version must be left alone with a notice.");

        bool rejected = false;
        try { UserDataMigrator.Run([new TestMigration(2, _ => { })]); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Migration versions must be contiguous from 1.");

        File.WriteAllText(UserDataMigrator.VersionFilePath, "{ broken");
        UserDataMigrationResult corrupt = UserDataMigrator.Run([.. two, new TestMigration(3, _ => calls.Add(99))]);
        Check(corrupt.Error is not null && !calls.Contains(99), "A corrupt version file must not restart migrations.");
    }

    private static void VerifyLegacyImport()
    {
        using var sandbox = DataSandbox.Create("legacy");
        JsonSerializerOptions options = JsonOptionsFactory.Create();
        string legacy = Path.Combine(sandbox.Root, "OldExe", "Saves");
        string previews = Path.Combine(legacy, "SavePrevData");
        const string format = "yyyyMMdd_HHmmss_fffffff";

        MandelbrotState Mandelbrot(string name, DateTime timestamp)
        {
            MandelbrotState state = PresetManager.GetMandelbrotPresets(MandelbrotVariant.Mandelbrot)[1];
            state.SaveName = name;
            state.Timestamp = timestamp;
            return state;
        }

        var valley = Mandelbrot("Долина: 1", new DateTime(2026, 9, 14, 23, 46, 58, DateTimeKind.Local).AddTicks(4213849));
        var second = Mandelbrot("2", new DateTime(2026, 9, 4, 12, 5, 33, DateTimeKind.Local).AddTicks(7273476));
        var gasket = new ApollonianState { SaveName = "A1", Timestamp = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Local) };
        Directory.CreateDirectory(Path.Combine(previews, "Mandelbrot"));
        Directory.CreateDirectory(Path.Combine(previews, "ApollonianGasket"));
        Directory.CreateDirectory(Path.Combine(previews, "Mandelbrot", "PointsOfInterest"));
        // Ровно так прежние хранилища писали список, а контроллер — имя превью.
        File.WriteAllText(Path.Combine(legacy, "Mandelbrot_saves.json"), JsonSerializer.Serialize(new List<MandelbrotState> { valley, second }, options));
        File.WriteAllText(Path.Combine(legacy, "Apollonian_saves.json"), JsonSerializer.Serialize(new List<ApollonianState> { gasket }, options));
        File.WriteAllBytes(Path.Combine(previews, "Mandelbrot", $"Долина_ 1_{valley.Timestamp.ToString(format)}.png"), [11]);
        // Смещённая метка (смена пояса): единственное превью с этим именем всё равно находится.
        File.WriteAllBytes(Path.Combine(previews, "Mandelbrot", $"2_{second.Timestamp.AddHours(-1).ToString(format)}.png"), [22]);
        File.WriteAllBytes(Path.Combine(previews, "ApollonianGasket", $"A1_{gasket.Timestamp.ToString(format)}.png"), [33]);
        File.WriteAllBytes(Path.Combine(previews, "Mandelbrot", "PointsOfInterest", "Лоза_00010101_000000_0000000.png"), [44]);
        File.WriteAllText(Path.Combine(legacy, "Broken_saves.json"), "{ oops");
        File.WriteAllText(Path.Combine(legacy, "Phoenix_saves.json.tmp"), "[]");
        File.WriteAllText(Path.Combine(legacy, "custom_palettes_mandelbrot.json"), "[]");
        File.WriteAllText(Path.Combine(legacy, "themes.json"), "[]");
        File.WriteAllText(Path.Combine(legacy, "theme-preferences.json"), "{}");
        File.WriteAllText(Path.Combine(legacy, "favorite_fractals.txt"), "legacy");
        Dictionary<string, byte[]> legacySnapshot = Directory.EnumerateFiles(legacy, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);

        // Уже есть данные новой версии: одноимённое сохранение и файл настроек.
        var mandelbrotStore = new MandelbrotSaveStore(MandelbrotVariant.Mandelbrot);
        mandelbrotStore.Save(Mandelbrot("2", new DateTime(2026, 9, 15, 9, 0, 0, DateTimeKind.Local)));
        File.WriteAllText(AppPaths.EnsureDirectoryFor(AppPaths.GetSettingsFile("favorite_fractals.txt")), "keep");

        var migration = new Migration001ImportLegacyExeFolder(legacy);
        UserDataMigrationResult result = UserDataMigrator.Run([migration]);
        Check(result.Error is null && result.FinalVersion == 1, $"Legacy import must complete: {result.Error}");
        Check(result.Notes.Any(note => note.Contains("сохранений — 3") && note.Contains("превью — 3") && note.Contains("файлов палитр, тем и настроек — 3")),
            "The import summary must count saves, previews and auxiliary files: " + string.Join(" | ", result.Notes));
        Check(result.Notes.Any(note => note.Contains("Broken_saves.json")), "A broken legacy list must be reported.");

        SaveLoadResult<MandelbrotState> mandelbrots = mandelbrotStore.LoadSlots();
        Check(mandelbrots.DamagedFiles.Count == 0 &&
              mandelbrots.Slots.Select(slot => slot.State.SaveName).Order().SequenceEqual(["2", "2 (2)", "Долина: 1"]),
            "Legacy saves must become one file each, renaming a clash with existing data.");
        SaveSlot<MandelbrotState> valleySlot = mandelbrots.Slots.Single(slot => slot.State.SaveName == "Долина: 1");
        Check(Path.GetFileName(valleySlot.FilePath) == "Долина_ 1.json" && File.ReadAllBytes(valleySlot.PreviewPath).SequenceEqual(new byte[] { 11 }),
            "A legacy preview must be placed next to its save.");
        Check(JsonSerializer.Serialize(valleySlot.State, options) == JsonSerializer.Serialize(valley, options),
            "An imported save must load exactly as the legacy list stored it.");
        Check(File.ReadAllBytes(mandelbrots.Slots.Single(slot => slot.State.SaveName == "2 (2)").PreviewPath).SequenceEqual(new byte[] { 22 }),
            "A preview with a shifted timestamp must still be found.");
        Check(!File.Exists(mandelbrots.Slots.Single(slot => slot.State.SaveName == "2").PreviewPath),
            "The existing save must not receive a legacy preview.");
        SaveSlot<ApollonianState> gasketSlot = new ApollonianSaveStore().LoadSlots().Slots.Single();
        Check(gasketSlot.State.SaveName == "A1" && File.ReadAllBytes(gasketSlot.PreviewPath).SequenceEqual(new byte[] { 33 }),
            "The Apollonian preview folder alias must be honoured.");

        Check(File.Exists(AppPaths.GetPaletteFile("custom_palettes_mandelbrot.json")) &&
              File.Exists(AppPaths.GetThemeFile("themes.json")) &&
              File.Exists(AppPaths.GetSettingsFile("theme-preferences.json")),
            "Palettes, themes and settings must be copied into their folders.");
        Check(File.ReadAllText(AppPaths.GetSettingsFile("favorite_fractals.txt")) == "keep", "Existing settings must not be overwritten.");
        Check(!Directory.Exists(AppPaths.PointsOfInterestRoot) && !Directory.Exists(AppPaths.GetSavesDirectory("Phoenix")),
            "Point-of-interest caches and temporary files must not be imported.");
        Check(legacySnapshot.All(pair => File.Exists(pair.Key) && File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)) &&
              Directory.EnumerateFiles(legacy, "*", SearchOption.AllDirectories).Count() == legacySnapshot.Count,
            "The legacy folder must stay untouched.");
        Check(sandbox.Recycled.Count == 0, "Importing must not recycle anything.");

        // Шаг прервался и выполняется повторно — дубликатов нет.
        migration.Apply(new UserDataMigrationContext(AppPaths.DataRoot, Path.Combine(AppPaths.DataRoot, "Backups", "rerun")));
        Check(mandelbrotStore.LoadSlots().Slots.Count == 3 && new ApollonianSaveStore().Load().Count == 1,
            "Re-running the import must not duplicate saves.");
    }

    private sealed class TestMigration(int version, Action<UserDataMigrationContext> apply) : IUserDataMigration
    {
        public int Version => version;
        public string Description => $"test {version}";
        public void Apply(UserDataMigrationContext context) => apply(context);
    }

    /// <summary>
    /// Временный каталог данных и подменённая Корзина: удаляемые файлы переносятся в
    /// <c>_RecycleBin</c> и запоминаются, чтобы проверить, что ничего не стёрто бесследно.
    /// </summary>
    private sealed class DataSandbox : IDisposable
    {
        private readonly string _previousDataRoot;

        private DataSandbox(string root)
        {
            Root = root;
            _previousDataRoot = AppPaths.DataRoot;
        }

        public static string BaseDirectory => Path.Combine(AppContext.BaseDirectory, "VerificationData");

        public string Root { get; }
        public List<(string Original, string Stored)> Recycled { get; } = [];

        public static DataSandbox Create(string label)
        {
            var sandbox = new DataSandbox(Path.Combine(BaseDirectory, $"{label}_{Guid.NewGuid():N}"));
            Directory.CreateDirectory(sandbox.Root);
            AppPaths.OverrideDataRoot(Path.Combine(sandbox.Root, "Data"));
            sandbox.InstallRecycleBin();
            return sandbox;
        }

        public void InstallRecycleBin() => RecycleBin.SendOverrideForTests = path =>
        {
            string bin = Directory.CreateDirectory(Path.Combine(Root, "_RecycleBin")).FullName;
            string stored = Path.Combine(bin, $"{Recycled.Count:D3}_{Path.GetFileName(path)}");
            File.Move(path, stored);
            Recycled.Add((path, stored));
        };

        public void Dispose()
        {
            RecycleBin.SendOverrideForTests = null;
            SaveFormat.UpgradesOverrideForTests = null;
            AppPaths.OverrideDataRoot(_previousDataRoot);
            // Удаляется только собственный временный каталог этой проверки.
            string fullPath = Path.GetFullPath(Root);
            if (!fullPath.StartsWith(Path.GetFullPath(BaseDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected verification directory.");
            if (Directory.Exists(fullPath)) Directory.Delete(fullPath, true);
        }
    }
}
