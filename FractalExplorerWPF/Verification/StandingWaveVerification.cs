using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Core.WaveMath;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyStandingWavesAsync()
    {
        VerifyCatalogData();
        VerifyCatalogSearch();
        const Fractal3DKind kind = Fractal3DKind.StandingWaves;
        var state = Fractal3DCatalog.CreateDefaultState(kind);
        var wave = new StandingWaveSettings
        {
            Modes = [new(2, 1, 1)],
            View = StandingWaveView.Slice, Axis = StandingWaveAxis.X
        };
        double frequency = StandingWave.Frequency(wave, wave.Modes[0]);
        Check(Math.Abs(frequency - Math.Sqrt(6) / 4) < 1e-12, "Частота моды куба 2×2×2.");
        state.StandingWave = wave;

        EnsureWindowThemeStyles();
        var window = new Fractal3DWindow(kind);
        try
        {
            window.LoadState(state);
            var editor = (StandingWaveEditor)window.FindName("WaveEditor");
            Check(editor.Visibility == Visibility.Visible, "Панель стоячих волн видима.");
            Check(((FrameworkElement)window.FindName("InitialPreparationLayer")).Visibility == Visibility.Visible,
                "Стоячие волны используют общий экран подготовки.");
            ((Button)editor.FindName("AddButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(editor.Capture().Modes.Length == 2, "Добавление моды.");
            var length = (TextBox)editor.FindName("LengthXBox");
            length.Text = "9";
            NumericInput.CommitAll(editor);
            Check(window.CaptureState("wave").StandingWave?.LengthX == 4, "Размер резонатора ограничен при вводе.");
            length.Text = "NaN";
            NumericInput.CommitAll(editor);
            Check(window.CaptureState("wave").StandingWave?.LengthX == 4, "Неконечный размер восстанавливается.");
        }
        finally { window.Close(); }

        var store = new Fractal3DSaveStore(kind);
        state.SaveName = $"Standing waves {Guid.NewGuid():N}";
        state.StandingWave = wave with { Time = .43, Cutaway = true, CutPosition = .31, Resolution = 96 };
        store.Save(state);
        var saved = store.Load().Single(scene => scene.SaveName == state.SaveName).StandingWave!;
        Check(saved.Modes.SequenceEqual(wave.Modes) && (saved with { Modes = state.StandingWave.Modes }) == state.StandingWave,
            "Время, моды и параметры вида переживают сохранение.");
        Check(!ReferenceEquals(state.Clone().StandingWave?.Modes, state.StandingWave.Modes),
            "Копия кадра не делит редактируемый список мод с оригиналом.");

        using var renderer = new Fractal3DRenderer();
        foreach (var preset in Fractal3DCatalog.GetPresets(kind))
        {
            var pixels = Pixels(await renderer.RenderAsync(preset, 144, 120, null, CancellationToken.None));
            Check(HasFractal3DStructure(pixels, 144, 120), $"Видны детали пресета «{preset.SaveName}».");
        }

        byte[] node = await Slice(wave);
        byte[] antinode = await Slice(wave with { CutPosition = .25 });
        byte[] opposite = await Slice(wave with { CutPosition = .25, Time = .5 / frequency });
        byte[] zero = await Slice(wave with { Modes = [new(2, 1, 1, 0)] });
        int nodeError = node.Zip(zero, (left, right) => Math.Abs(left - right)).Max();
        Check(nodeError <= 2, $"Срединное сечение по X совпадает с узлом моды (2,1,1). Разница каналов: {nodeError}.");
        Check(antinode.Zip(zero, (left, right) => Math.Abs(left - right)).Max() > 80 &&
              antinode.Zip(opposite, (left, right) => Math.Abs(left - right)).Max() > 80,
            "Пучность видима, через полпериода знак поля меняется.");
        Console.WriteLine("PASS (standing-waves): frequency, nodal slice, sign change, presets, numeric editor and saves.");

        async Task<byte[]> Slice(StandingWaveSettings settings)
        {
            state.StandingWave = settings;
            state.CameraYaw = 90;
            state.CameraPitch = 0;
            return Pixels(await renderer.RenderAsync(state, 104, 88, null, CancellationToken.None));
        }
    }
}
