using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

// Палитры бассейнов: раскраска по периоду, общий файл палитр и окно редактора.
internal static partial class Program
{
    private static void VerifyBasinPalettes()
    {
        using var sandbox = DataSandbox.Create("basin-palettes");
        var builtIns = new NewtonPaletteManager().Palettes;
        NewtonColorPalette grayscale = builtIns.Single(p => p.Name == "Оттенки серого");
        NewtonColorPalette classic = builtIns.Single(p => p.Name == "Классика");

        // Раскраска по периоду раньше брала оттенки золотого угла и не зависела от палитры.
        foreach (BasinExplorerState preset in new[]
                 {
                     BasinExplorerCatalog.GetPresets(BasinExplorerKind.ComplexLogistic).Single(s => s.LogisticPlane == LogisticPlaneMode.Parameter),
                     BasinExplorerCatalog.GetPresets(BasinExplorerKind.PeriodicCycles).First(s => s.ColoringMode == BasinColoringMode.Period)
                 })
        {
            BasinExplorerState gray = preset.Clone(), colored = preset.Clone();
            gray.Palette = grayscale.Clone(grayscale.Name);
            colored.Palette = classic.Clone(classic.Name);
            BasinExplorerEngine engine = BasinExplorerWindow.CreateEngine(gray);
            Check(engine.PeriodColors.Length >= engine.MaxPeriod &&
                  engine.PeriodColors.SequenceEqual(NewtonPaletteManager.AdjustColors(gray.Palette, engine.PeriodColors.Length)),
                $"{preset.Kind}: period colors must come from the palette.");
            Check(!RenderBasinFrame(engine, 40, 30).AsSpan().SequenceEqual(RenderBasinFrame(BasinExplorerWindow.CreateEngine(colored), 40, 30)),
                $"{preset.Kind} «{preset.SaveName}»: period coloring must depend on the palette.");
        }

        // Каждое окно держит свой менеджер, а файл палитр общий: давно открытое окно не должно
        // затирать палитры, созданные в другом.
        var first = new NewtonPaletteManager();
        var second = new NewtonPaletteManager();
        first.Palettes.Add(new NewtonColorPalette { Name = "Из первого окна", RootColors = [System.Windows.Media.Colors.Red] });
        first.SaveCustomPalettes();
        second.ActivePalette = second.Palettes[3];
        second.ReloadCustomPalettes();
        second.Palettes.Add(new NewtonColorPalette { Name = "Из второго окна", RootColors = [System.Windows.Media.Colors.Blue] });
        second.SaveCustomPalettes();
        var reloaded = new NewtonPaletteManager();
        Check(reloaded.Palettes.Any(p => p.Name == "Из первого окна") && reloaded.Palettes.Any(p => p.Name == "Из второго окна") &&
              ReferenceEquals(second.ActivePalette, second.Palettes[3]),
            "Palette managers of different windows must not overwrite each other's palettes.");
        first.ActivePalette = first.Palettes.Single(p => p.Name == "Из первого окна");
        first.ReloadCustomPalettes();
        Check(first.ActivePalette.Name == "Из первого окна" && first.Palettes.Contains(first.ActivePalette),
            "Reloading must keep the active custom palette selected.");

        var themeStyles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == themeStyles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = themeStyles });

        // Окно бассейнов: собственный заголовок, флажок градиента виден, но выключен и объяснён.
        var manager = new NewtonPaletteManager();
        var basinDialog = new NewtonPaletteWindow(manager, [0, 1], ["Период 1", "Период 2"], "heading", showGradientOption: false,
            windowTitle: "Палитры — Бассейны комплексного логистического отображения");
        try
        {
            Check(basinDialog.Title == "Палитры — Бассейны комплексного логистического отображения" &&
                  (string)((GroupBox)basinDialog.FindName("EditorGroup")).Header == "Редактор палитры",
                "Basin palette dialog must not be titled as the Newton palette dialog.");
            var gradient = (CheckBox)basinDialog.FindName("GradientBox");
            Check(gradient.Visibility == Visibility.Visible && !gradient.IsEnabled &&
                  ((FrameworkElement)basinDialog.FindName("GradientNote")).Visibility == Visibility.Visible,
                "Basin palette dialog must show the gradient flag as inactive and explain why.");

            // «Новая» сразу пишет файл: закрытие окна без «Сохранить» не должно терять палитру.
            typeof(NewtonPaletteWindow).GetMethod("New_OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(basinDialog, [basinDialog, new RoutedEventArgs()]);
            Check(new NewtonPaletteManager().Palettes.Any(p => p.Name.StartsWith("Новая палитра", StringComparison.Ordinal)),
                "A new palette must be persisted immediately.");
        }
        finally { basinDialog.Close(); }

        var newtonDialog = new NewtonPaletteWindow(new NewtonPaletteManager(), [0, 1, 2]);
        try
        {
            Check(newtonDialog.Title == "Палитры бассейнов Ньютона" &&
                  ((FrameworkElement)newtonDialog.FindName("GradientNote")).Visibility == Visibility.Collapsed,
                "Newton pools keep their own title and an active gradient flag.");
        }
        finally { newtonDialog.Close(); }
        Console.WriteLine("[diag] Basin palettes: period colors from palette, shared palette file, dialog titles and gradient flag OK");
    }
}
