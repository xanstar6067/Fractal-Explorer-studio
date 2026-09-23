using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using Application = System.Windows.Application;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorerWPF;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Theming;
using FractalExplorerWPF.Views;

// Автономный генератор скриншотов всех окон WPF-приложения (см.
// "Скриншоты окон для документации" в CLAUDE.md/AGENTS.md для контекста
// использования и обзора устройства). Окна открываются далеко за пределами
// экрана — пользователь их не увидит, но WPF всё равно выполняет полный
// layout/render, поэтому результат идентичен обычному запуску приложения.

internal static class Program
{
    private static string OutDir = "";

    private static int _ok;
    private static int _fail;

    private static bool _smoke;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 1 || args[0].StartsWith('-'))
        {
            Console.Error.WriteLine("Использование: dotnet run --project FractalExplorerWPF/ScreenshotGen/ScreenshotGen.csproj -- <папка-вывода> [smoke]");
            Console.Error.WriteLine("  <папка-вывода> — куда сохранять PNG (создаётся, если не существует).");
            Console.Error.WriteLine("  smoke          — только 4 быстрых скриншота для проверки, без полного набора.");
            return 2;
        }

        OutDir = Path.GetFullPath(args[0]);
        _smoke = args.Skip(1).Contains("smoke");
        Directory.CreateDirectory(OutDir);
        int exitCode = 0;
        // pack://application:,,,/ по умолчанию резолвится в сборку .exe (ScreenshotGen),
        // а ресурсы (превью и т.п.) зашиты в FractalExplorerWPF.dll. Свойство
        // ResourceAssembly к этому моменту уже зафиксировано платформой (публичный
        // сеттер бросает исключение), поэтому подменяем закрытое поле напрямую.
        ForceResourceAssembly(typeof(MainWindow).Assembly);

        // Скриншоты не должны зависеть от сохранений, палитр и выбранной темы пользователя в
        // AppData: у инструмента свой пустой каталог данных рядом с его exe.
        AppPaths.OverrideDataRoot(Path.Combine(AppContext.BaseDirectory, "ScreenshotData"));

        // Настоящий App вместо голого Application: иначе не подключаются
        // Theming/ThemeStyles.xaml и активная тема (стили типа
        // FractalPanelToggleButtonStyle), которые использует почти каждое окно.
        // App_OnStartup (создание MainWindow) не вызываем — Run() не запускаем.
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        ThemeManager.Initialize(app);
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());

        Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
        {
            try
            {
                await RunAllAsync();
                Console.WriteLine($"DONE: ok={_ok} fail={_fail}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FATAL: " + ex);
                exitCode = 1;
            }
            finally
            {
                CloseEverything();
                Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
        });

        Dispatcher.Run();
        return exitCode;
    }

    // Все состояния облачной таблицы на вымышленных данных; пути к файлам не существуют.
    private static List<CloudSyncEntry> CloudSampleEntries()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        static string Json(string category, string name, int zoom, int iterations) =>
            $"{{\"format\":\"FractalExplorerWPF\",\"version\":1,\"category\":\"{category}\",\"state\":{{\"SaveName\":\"{name}\",\"Zoom\":{zoom},\"MaxIterations\":{iterations}}}}}";
        LocalCloudSave Local(string category, string name, int zoom = 1000, int iterations = 500) =>
            new($@"C:\Samples\{category}\{name}.json", category, name, Json(category, name, zoom, iterations), name);
        CloudSave Remote(string name, int revision, double hoursAgo, string category = "Mandelbrot", int zoom = 1000, int iterations = 500) =>
            new(Guid.NewGuid(), name, revision, now.AddDays(-3), now.AddHours(-hoursAgo), Json(category, name, zoom, iterations));
        CloudLink Link(CloudSave remote, string category) => new(remote.Id, $@"{category}\{remote.Name}.json", remote.Revision, remote.Name);
        CloudSave spiral = Remote("Спираль", 3, 2), seahorse = Remote("Долина морских коньков", 1, 30),
            tree = Remote("Дерево Барнсли", 2, 5, "IFS"), lorenz = Remote("Бабочка Лоренца", 4, 1, "DynamicSystem");
        return
        [
            new(CloudEntryState.Synced, Local("Mandelbrot", "Спираль"), spiral, Link(spiral, "Mandelbrot"), "Mandelbrot"),
            new(CloudEntryState.SameName, Local("Mandelbrot", "Мини-мандельброт", 4000, 900), Remote("Мини-мандельброт", 1, 50, zoom: 2500, iterations: 1200), null, "Mandelbrot"),
            new(CloudEntryState.CloudChanged, Local("Mandelbrot", "Долина морских коньков"), seahorse with { Revision = 2 }, Link(seahorse, "Mandelbrot"), "Mandelbrot"),
            new(CloudEntryState.LocalOnly, Local("Phoenix", "Перо феникса"), null, null, "Phoenix"),
            new(CloudEntryState.CloudOnly, null, Remote("Ньютон z⁵ − 1", 1, 72, "NewtonPools"), null, "NewtonPools"),
            new(CloudEntryState.LocalChanged, Local("IFS", "Дерево Барнсли"), tree, Link(tree, "IFS") with { Hash = "old" }, "IFS"),
            new(CloudEntryState.BothChanged, Local("DynamicSystem", "Бабочка Лоренца"), lorenz with { Revision = 5 }, Link(lorenz, "DynamicSystem") with { Hash = "old" }, "DynamicSystem")
        ];
    }

    // ---------- infrastructure ----------

    private static void ForceResourceAssembly(Assembly asm)
    {
        Type appType = typeof(Application);
        foreach (FieldInfo f in appType.GetFields(BindingFlags.Static | BindingFlags.NonPublic))
        {
            if (f.FieldType == typeof(Assembly))
            {
                f.SetValue(null, asm);
                return;
            }
        }
        throw new MissingFieldException("Application", "(resource assembly backing field)");
    }

    private static void Offscreen(Window w)
    {
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = -32000;
        w.Top = -32000;
        w.ShowInTaskbar = false;
    }

    private static async Task<Window> ShowAsync(Window w, int waitMs)
    {
        Offscreen(w);
        w.Show();
        await Task.Delay(waitMs);
        await WaitForRenderIdleAsync(w);
        return w;
    }

    // Многие окна выставляют приватное поле "_isRendering" на время расчёта кадра
    // (обычный паттерн во всём проекте). Фиксированной паузы иногда не хватает —
    // например, Nova Mandelbrot успевал сняться раньше, чем движок закончил кадр.
    // Ждём явного завершения, но не дольше разумного предела; для окон без такого
    // поля (стохастическое накопление, живые симуляции) это no-op — там качество
    // кадра и так регулируется increased fixed-wait в CaptureAsync для этих кейсов.
    // Окна с живым превью (Fractal3DWindow) считают кадр лесенкой: черновик, затем полный кадр
    // и сглаживание. Между ступенями "_isRendering" ненадолго гаснет, поэтому ожидание учитывает
    // и очередь: пока стоит запрос следующей ступени, окно снимать рано.
    private static async Task WaitForRenderIdleAsync(Window win, int maxExtraMs = 10000)
    {
        if (GetMember(win, "_isRendering") is not bool) return;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < maxExtraMs)
        {
            if (GetMember(win, "_isRendering") is false && GetMember(win, "_frameRequested") is not true)
            {
                await Task.Delay(250);
                if (GetMember(win, "_isRendering") is false && GetMember(win, "_frameRequested") is not true) return;
                continue;
            }
            await Task.Delay(150);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    // Далеко за экраном DWM отдаёт PrintWindow только рамку/заголовок — сам
    // клиентский WPF-визуал (композитная поверхность) на такой позиции не
    // обновляется и выходит белым. Поэтому рамку берём через PrintWindow,
    // а содержимое клиентской области — надёжным RenderTargetBitmap
    // (он всегда корректно рендерит визуальное дерево независимо от
    // положения окна на экране) и склеиваем их в одно изображение.
    private static void Capture(Window win, string slug)
    {
        try
        {
            win.UpdateLayout();
            IntPtr hwnd = new WindowInteropHelper(win).Handle;
            if (hwnd == IntPtr.Zero) throw new InvalidOperationException("HWND отсутствует (окно ещё не показано)");
            if (!GetWindowRect(hwnd, out RECT winRect)) throw new InvalidOperationException("GetWindowRect не удался");
            int w = winRect.Right - winRect.Left;
            int h = winRect.Bottom - winRect.Top;
            if (w <= 0 || h <= 0) throw new InvalidOperationException($"некорректный размер {w}x{h}");

            using var frame = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(frame))
            {
                IntPtr hdc = g.GetHdc();
                try { PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT); }
                finally { g.ReleaseHdc(hdc); }
            }

            DpiScale dpi = VisualTreeHelper.GetDpi(win);
            int clientW = Math.Max(1, (int)Math.Ceiling(win.ActualWidth * dpi.DpiScaleX));
            int clientH = Math.Max(1, (int)Math.Ceiling(win.ActualHeight * dpi.DpiScaleY));
            var rtb = new RenderTargetBitmap(clientW, clientH, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            rtb.Render(win);

            using var clientBmp = ToGdiBitmap(rtb);

            var origin = new POINT { X = 0, Y = 0 };
            ClientToScreen(hwnd, ref origin);
            int offsetX = origin.X - winRect.Left;
            int offsetY = origin.Y - winRect.Top;

            // Рисуем в натуральном размере (без растяжения под GetClientRect) —
            // clientW/clientH уже честные физические пиксели того же окна, и любое
            // несовпадение систем координат при стрейтче даёт щель/сжатие снизу.
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(frame))
            {
                g.DrawImageUnscaled(clientBmp, offsetX, offsetY);
            }

            string path = Path.Combine(OutDir, slug + ".png");
            frame.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            _ok++;
            Console.WriteLine($"  [ok] {slug}.png ({w}x{h})");
        }
        catch (Exception ex)
        {
            _fail++;
            Console.Error.WriteLine($"  [FAIL-capture] {slug}: {ex.Message}");
        }
    }

    private static System.Drawing.Bitmap ToGdiBitmap(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;
        return new System.Drawing.Bitmap(ms);
    }

    private static async Task<Window?> CaptureAsync(Func<Window> factory, string slug, int waitMs = 1300)
    {
        try
        {
            Window win = factory();
            await ShowAsync(win, waitMs);
            Capture(win, slug);
            return win;
        }
        catch (Exception ex)
        {
            _fail++;
            Console.Error.WriteLine($"  [FAIL-open] {slug}: {ex.Message}");
            return null;
        }
    }

    private static async Task CaptureChildAsync(Window owner, Window child, string slug, int waitMs = 900)
    {
        try
        {
            child.Owner = owner;
            await ShowAsync(child, waitMs);
            Capture(child, slug);
            SafeClose(child);
        }
        catch (Exception ex)
        {
            _fail++;
            Console.Error.WriteLine($"  [FAIL-child] {slug}: {ex.Message}");
        }
    }

    // Открывает окно, показанное через публичный статический Open(...)/ShowDialog(),
    // ловит его через Application.Current.Windows во время работы вложенного цикла диспетчера.
    private static async Task CaptureModalAsync(Action openModal, string slug, int waitMs = 900)
    {
        var before = new HashSet<Window>(Application.Current!.Windows.Cast<Window>());
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(() => { _ = DetectCaptureCloseAsync(before, slug, waitMs); }));
        try
        {
            openModal();
        }
        catch (Exception ex)
        {
            _fail++;
            Console.Error.WriteLine($"  [FAIL-modal] {slug}: {ex.Message}");
        }
        await Task.Delay(50);
    }

    private static async Task DetectCaptureCloseAsync(HashSet<Window> before, string slug, int waitMs)
    {
        Window? dlg = null;
        for (int i = 0; i < 100 && dlg == null; i++)
        {
            await Task.Delay(50);
            dlg = Application.Current!.Windows.Cast<Window>().FirstOrDefault(w => !before.Contains(w));
        }

        if (dlg == null)
        {
            _fail++;
            Console.Error.WriteLine($"  [FAIL-modal-notfound] {slug}");
            return;
        }

        Offscreen(dlg);
        await Task.Delay(waitMs);
        Capture(dlg, slug);
        SafeClose(dlg);
    }

    private static void SafeClose(Window w)
    {
        try { w.Close(); } catch { /* ignore */ }
    }

    private static void CloseEverything()
    {
        foreach (Window w in Application.Current!.Windows.Cast<Window>().ToList())
            SafeClose(w);
    }

    // ---------- reflection helpers ----------

    private static object? GetMember(object obj, string name)
    {
        for (Type? t = obj.GetType(); t != null; t = t.BaseType)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
            FieldInfo? f = t.GetField(name, flags);
            if (f != null) return f.GetValue(obj);
            PropertyInfo? p = t.GetProperty(name, flags);
            if (p != null) return p.GetValue(obj);
        }
        return null;
    }

    private static object? Invoke(object obj, string method, params object?[] args)
    {
        for (Type? t = obj.GetType(); t != null; t = t.BaseType)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
            MethodInfo? m = t.GetMethod(method, flags);
            if (m != null) return m.Invoke(obj, args);
        }
        throw new MissingMethodException(obj.GetType().Name, method);
    }

    private static string Kebab(string pascal)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < pascal.Length; i++)
        {
            char c = pascal[i];
            if (char.IsUpper(c) && i > 0) sb.Append('-');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    // ---------- orchestration ----------

    private static async Task RunAllAsync()
    {
        Console.WriteLine("== Main catalog ==");
        var main = new MainWindow();
        await ShowAsync(main, 1800);
        Capture(main, "00-main-catalog");

        await CaptureChildAsync(main, new QuickSwitcherWindow(FractalCatalog.Create()), "00-quick-switcher");
        await CaptureChildAsync(main, new AboutWindow(), "00-about");

        if (_smoke)
        {
            Window? mandel = await CaptureAsync(() => new MandelbrotWindow(MandelbrotVariant.Mandelbrot), "mandelbrot-mandelbrot", 1600);
            SafeCloseIfAny(mandel);
            return;
        }

        // No live network or login during documentation capture: the table is filled with sample rows.
        using (var cloudClient = new FractalExplorerWPF.Infrastructure.Cloud.FractalCloudClient(new() { Server = "https://cloud.example.com" }))
        {
            var cloudSaves = new CloudSaveManagerWindow(connectOnLoad: false, client: cloudClient);
            cloudSaves.ShowEntriesForPreview("user@example.com", CloudSampleEntries(), "Обновлено в 12:00: на ПК 7, в облаке 7.");
            await CaptureChildAsync(main, cloudSaves, "00-cloud-saves");
            await CaptureChildAsync(main, new CloudSaveManagerWindow(connectOnLoad: false, client: cloudClient), "00-cloud-login");
        }
        CloudSyncEntry sample = CloudSampleEntries().First(e => e.State == CloudEntryState.SameName);
        await CaptureChildAsync(main, new CloudConflictWindow(new CloudCollision(CloudCollisionKind.SameName,
            CloudTransferMode.Upload, sample.Local, sample.Remote, 2)), "00-cloud-conflict");

        var themeEditor = new ThemeEditorWindow();
        await ShowAsync(themeEditor, 1200);
        Offscreen(themeEditor);
        themeEditor.Owner = main;
        Capture(themeEditor, "00-theme-editor");
        await CaptureChildAsync(themeEditor, new ThemeColorPickerWindow(Colors.DodgerBlue), "00-theme-color-picker");
        SafeClose(themeEditor);

        await CaptureChildAsync(main, new ColorPickerWindow(Colors.OrangeRed), "00-color-picker");

        await CaptureChildAsync(main, new CrashDialogWindow(
            "Произошла непредвиденная ошибка",
            "Приложению не удалось продолжить рендер текущего кадра.",
            "System.InvalidOperationException: демонстрационное сообщение для скриншота README.\n   at FractalExplorerWPF.Core.Rendering.Demo()",
            @"C:\Users\Demo\AppData\Local\FractalExplorerWPF\crash.log",
            allowContinue: true), "00-crash-dialog");

        Console.WriteLine("== Catalog entries ==");
        foreach (FractalCatalogItem item in FractalCatalog.Create())
        {
            await ProcessCatalogItemAsync(item);
        }

        Console.WriteLine("== Shared export/save managers ==");
        await CaptureSharedManagersAsync();
    }

    private static async Task ProcessCatalogItemAsync(FractalCatalogItem item)
    {
        string? key = item.LaunchKey;
        Console.WriteLine($"-- {item.DisplayName} [{key}]");

        try
        {
            if (MathematicalLaboratoryCatalog.TryParseLaunchKey(key, out MathematicalLaboratoryKind labKind))
            {
                await CaptureAsync(() => new MathematicalLaboratoryWindow(labKind), "lab-" + Kebab(labKind.ToString()), 1600);
                return;
            }

            if (Fractal3DCatalog.TryParseLaunchKey(key, out Fractal3DKind fractal3DKind))
            {
                // Кадр считает GPU. Менеджер палитр общий для всех шести видов, поэтому снимается
                // один раз — на Мандельбульбе; предпросмотр фрактала в нём отключён (null), чтобы
                // снимок не зависел от того, успела ли видеокарта посчитать кадр.
                Window? w = await CaptureAsync(() => new Fractal3DWindow(fractal3DKind),
                    "fractal3d-" + Kebab(fractal3DKind.ToString()), 1600);
                if (w != null && fractal3DKind == Fractal3DKind.Mandelbulb)
                {
                    object mgr = GetMember(w, "_paletteManager")!;
                    object palette = GetMember(w, "_palette")!;
                    await CaptureChildAsync(w, (Window)Activator.CreateInstance(
                        typeof(Fractal3DPaletteWindow), mgr, palette, null)!, "fractal3d-palette-editor");
                }
                SafeCloseIfAny(w);
                return;
            }

            if (BasinExplorerCatalog.TryParseLaunchKey(key, out BasinExplorerKind basinKind))
            {
                // Включает все 11 режимов, в том числе оптимизацию и непрерывные поля: список берётся
                // из FractalCatalog, а панели, пресеты и предпросмотр — из общего окна.
                Window? w = await CaptureAsync(() => new BasinExplorerWindow(basinKind), "basins-" + Kebab(basinKind.ToString()), 1600);
                // Общий редактор палитр (файл палитр — как у бассейнов Ньютона) с подписями циклов — один раз, на окне периодических циклов.
                if (w != null && basinKind == BasinExplorerKind.PeriodicCycles)
                {
                    var mgr = (NewtonPaletteManager)GetMember(w, "_paletteManager")!;
                    object engine = GetMember(w, "_engine")!;
                    var attractors = (IReadOnlyList<BasinAttractor>)GetMember(engine, "Attractors")!;
                    await CaptureChildAsync(w, new NewtonPaletteWindow(mgr,
                        attractors.Select(a => a.IsInfinity || a.Points.Count == 0 ? System.Numerics.Complex.Zero : a.Points[0]).ToArray(),
                        attractors.Select((a, index) => a.ShortLabel(index)).ToArray(),
                        $"Аттракторов: {attractors.Count}. Цвет фона — уходящие и нераспознанные орбиты.",
                        showGradientOption: false, $"Палитры — {BasinExplorerCatalog.GetDefinition(basinKind).Title}"), "basins-palette-editor");
                }
                SafeCloseIfAny(w);
                return;
            }

            switch (key)
            {
                case "JuliaGallery":
                    await CaptureAsync(() => new JuliaGalleryWindow(MandelbrotVariant.Julia), "julia-gallery", 2200);
                    return;
                case "JuliaBurningShipGallery":
                    await CaptureAsync(() => new JuliaGalleryWindow(MandelbrotVariant.JuliaBurningShip), "julia-burning-ship-gallery", 2200);
                    return;
                case "LSystem":
                case "Serpinsky":
                    await CaptureAsync(() => new LSystemWindow(), "lsystem", 2200);
                    return;
                case "SerpinskyChaos":
                {
                    Window? w = await CaptureAsync(() => new SerpinskyWindow(chaosOnly: true), "serpinsky-chaos", 1800);
                    if (w != null)
                    {
                        object mgr = GetMember(w, "_paletteManager")!;
                        await CaptureChildAsync(w, (Window)Activator.CreateInstance(typeof(SerpinskyPaletteWindow), mgr)!, "serpinsky-palette-editor");
                    }
                    SafeCloseIfAny(w);
                    return;
                }
                case "NewtonPools":
                {
                    Window? w = await CaptureAsync(() => new NewtonPoolsWindow(), "newton-pools", 1600);
                    if (w != null)
                    {
                        object mgr = GetMember(w, "_paletteManager")!;
                        object engine = GetMember(w, "_formulaEngine")!;
                        object roots = GetMember(engine, "Roots")!;
                        await CaptureChildAsync(w, (Window)Activator.CreateInstance(typeof(NewtonPaletteWindow), mgr, roots)!, "newton-palette-editor");
                    }
                    SafeCloseIfAny(w);
                    return;
                }
                case "Phoenix":
                {
                    Window? w = await CaptureAsync(() => new PhoenixWindow(), "phoenix", 1600);
                    if (w != null)
                    {
                        try
                        {
                            object state = Invoke(w, "CaptureState", "screenshot")!;
                            await CaptureChildAsync(w, new PhoenixParameterExplorerWindow((PhoenixState)state), "phoenix-parameter-explorer");
                        }
                        catch (Exception ex) { Console.Error.WriteLine("  [warn] phoenix-parameter-explorer: " + ex.Message); }
                    }
                    SafeCloseIfAny(w);
                    return;
                }
                case "Collatz":
                    await CaptureAsync(() => new CollatzWindow(), "collatz", 1600);
                    return;
                case "InverseCollatzTree":
                {
                    Window? w = await CaptureAsync(() => new InverseCollatzTreeWindow(), "inverse-collatz-tree", 2600);
                    if (w != null)
                    {
                        object mgr = GetMember(w, "_paletteManager")!;
                        await CaptureChildAsync(w, (Window)Activator.CreateInstance(typeof(InverseCollatzPaletteWindow), mgr)!, "inverse-collatz-palette-editor");
                    }
                    SafeCloseIfAny(w);
                    return;
                }
                case "DomainColoring":
                {
                    Window? w = await CaptureAsync(() => new DomainColoringWindow(), "domain-coloring", 1600);
                    if (w != null)
                    {
                        object mgr = GetMember(w, "_paletteManager")!;
                        await CaptureChildAsync(w, (Window)Activator.CreateInstance(typeof(DomainColoringPaletteWindow), mgr)!, "domain-coloring-palette-editor");
                    }
                    SafeCloseIfAny(w);
                    return;
                }
                case "NovaMandelbrot":
                {
                    Window? w = await CaptureAsync(() => new NovaWindow(NovaVariant.Mandelbrot), "nova-mandelbrot", 1600);
                    if (w != null)
                    {
                        try
                        {
                            object state = Invoke(w, "CaptureState", "screenshot")!;
                            await CaptureChildAsync(w, new NovaParameterSelectorWindow((NovaState)state), "nova-parameter-selector");
                        }
                        catch (Exception ex) { Console.Error.WriteLine("  [warn] nova-parameter-selector: " + ex.Message); }
                    }
                    SafeCloseIfAny(w);
                    return;
                }
                case "NovaJulia":
                    await CaptureAsync(() => new NovaWindow(NovaVariant.Julia), "nova-julia", 1600);
                    return;
                case "Buddhabrot":
                {
                    Window? w = await CaptureAsync(() => new BuddhabrotWindow(), "buddhabrot", 3000);
                    if (w != null)
                    {
                        object mgr = GetMember(w, "_palettes")!;
                        await CaptureChildAsync(w, (Window)Activator.CreateInstance(typeof(BuddhabrotPaletteWindow), mgr)!, "buddhabrot-palette-editor");
                    }
                    SafeCloseIfAny(w);
                    return;
                }
                case "Flame":
                {
                    Window? w = await CaptureAsync(() => new FlameWindow(), "flame", 3200);
                    if (w != null)
                    {
                        object transforms = GetMember(w, "_transforms")!;
                        await CaptureChildAsync(w, (Window)Activator.CreateInstance(typeof(FlameTransformEditorWindow), transforms)!, "flame-transform-editor");
                    }
                    SafeCloseIfAny(w);
                    return;
                }
                case "IFS":
                {
                    Window? w = await CaptureAsync(() => new IfsWindow(), "ifs", 1800);
                    if (w != null)
                    {
                        object transforms = GetMember(w, "_transforms")!;
                        await CaptureChildAsync(w, (Window)Activator.CreateInstance(typeof(IfsTransformEditorWindow), transforms)!, "ifs-transform-editor");
                    }
                    SafeCloseIfAny(w);
                    return;
                }
                case "IFS3D":
                    await CaptureAsync(() => new Ifs3DWindow(), "ifs3d", 2200);
                    return;
                case "ApollonianGasket":
                    await CaptureAsync(() => new ApollonianWindow(), "apollonian", 1800);
                    return;
                case "DLA":
                    await CaptureAsync(() => new DlaWindow(), "dla", 3200);
                    return;
                case "GrayScott":
                {
                    Window? w = await CaptureAsync(() => new GrayScottWindow(), "gray-scott", 2600);
                    if (w != null)
                    {
                        object mgr = GetMember(w, "_paletteManager")!;
                        await CaptureChildAsync(w, (Window)Activator.CreateInstance(typeof(GrayScottPaletteWindow), mgr)!, "gray-scott-palette-editor");
                    }
                    SafeCloseIfAny(w);
                    return;
                }
            }

            if (Enum.TryParse(key, out DynamicSystemKind dsk))
            {
                string slug = "dynsys-" + Kebab(dsk.ToString());
                Window? w = await CaptureAsync(() => new DynamicSystemWindow(dsk), slug, 1800);
                if (w != null)
                {
                    if (dsk == DynamicSystemKind.Lyapunov)
                    {
                        object store = GetMember(w, "_paletteStore")!;
                        object palettes = GetMember(w, "_palettes")!;
                        object? active = GetMember(w, "ActivePalette");
                        await CaptureChildAsync(w, (Window)Activator.CreateInstance(typeof(LyapunovPaletteWindow), store, palettes, active)!, "lyapunov-palette-editor");
                    }
                    else if (dsk == DynamicSystemKind.Lorenz)
                    {
                        object store = GetMember(w, "_paletteStore")!;
                        object palettes = GetMember(w, "_palettes")!;
                        object? active = GetMember(w, "ActivePalette");
                        await CaptureChildAsync(w, (Window)Activator.CreateInstance(typeof(DynamicPaletteWindow), store, palettes, active)!, "dynamic-palette-editor");
                    }
                }
                SafeCloseIfAny(w);
                return;
            }

            if (Enum.TryParse(key, out MandelbrotVariant variant))
            {
                string slug = "mandelbrot-" + Kebab(variant.ToString());
                Window? w = await CaptureAsync(() => new MandelbrotWindow(variant), slug, 1600);
                if (w != null && variant == MandelbrotVariant.Mandelbrot)
                {
                    object mgr = GetMember(w, "_paletteManager")!;
                    await CaptureChildAsync(w, (Window)Activator.CreateInstance(typeof(MandelbrotPaletteWindow), mgr)!, "mandelbrot-palette-editor");
                    await CaptureChildAsync(w, new JuliaConstantPickerWindow(MandelbrotVariant.Mandelbrot, -0.5m, 0.0m), "julia-constant-picker");
                }
                SafeCloseIfAny(w);
                return;
            }

            Console.Error.WriteLine($"  [FAIL-unresolved] {item.DisplayName} key={key}");
            _fail++;
        }
        catch (Exception ex)
        {
            _fail++;
            Console.Error.WriteLine($"  [FAIL-item] {item.DisplayName}: {ex}");
        }
    }

    private static void SafeCloseIfAny(Window? w)
    {
        if (w != null) SafeClose(w);
    }

    private static async Task CaptureSharedManagersAsync()
    {
        try
        {
            var mandel = new MandelbrotWindow(MandelbrotVariant.Mandelbrot);
            await ShowAsync(mandel, 1200);

            var config = new ImageExportConfiguration
            {
                FileNamePrefix = "screenshot_demo",
                RenderAsync = (request, _, _) =>
                {
                    var bmp = new WriteableBitmap(Math.Max(1, request.Width), Math.Max(1, request.Height), 96, 96, PixelFormats.Bgra32, null);
                    return Task.FromResult<BitmapSource>(bmp);
                }
            };
            await CaptureModalAsync(() => ImageExportManagerWindow.Open(mandel, config), "image-export-manager", 1000);

            var store = new MandelbrotSaveStore(MandelbrotVariant.Mandelbrot);
            var saveConfig = SaveManagerConfigurations.ForMandelbrot(mandel, store);
            await CaptureModalAsync(() => SaveManagerWindow.Open(mandel, saveConfig), "save-manager", 1000);

            SafeClose(mandel);
        }
        catch (Exception ex)
        {
            _fail++;
            Console.Error.WriteLine("  [FAIL-shared] " + ex);
        }
    }
}
