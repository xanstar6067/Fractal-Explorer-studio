using System.Numerics;
using System.Windows;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private List<Flame3DTransform> _flameTransforms = [];

    private Flame3DSettings CaptureFlame()
    {
        if (Kind != Fractal3DKind.Flame3D) return new();
        var settings = new Flame3DSettings
        {
            Seed = ReadInt(FlameSeedBox, "Начальное число Flame", int.MinValue, int.MaxValue),
            Warmup = ReadInt(FlameWarmupBox, "Прогрев Flame", 10, 1000),
            Exposure = ReadDouble(FlameExposureBox, "Экспозиция", .05, 20),
            Gamma = ReadDouble(FlameGammaBox, "Гамма", .5, 4),
            Density = ReadDouble(FlameDensityBox, "Плотность", .05, 10),
            Vibrancy = ReadDouble(FlameVibrancyBox, "Насыщенность", 0, 2),
            Transforms = _flameTransforms.Select(t => t.Clone()).ToList()
        };
        settings.Validate();
        return settings;
    }

    private void LoadFlame(Flame3DSettings? source)
    {
        var s = source?.Clone() ?? new();
        _flameTransforms = s.Transforms;
        FlameSeedBox.Text = s.Seed.ToString();
        FlameWarmupBox.Text = s.Warmup.ToString();
        FlameExposureBox.Text = Format(s.Exposure);
        FlameGammaBox.Text = Format(s.Gamma);
        FlameDensityBox.Text = Format(s.Density);
        FlameVibrancyBox.Text = Format(s.Vibrancy);
    }

    private void FlameTransforms_OnClick(object sender, RoutedEventArgs e)
    {
        int originalPreset = PresetBox.SelectedIndex;
        Fractal3DState original = CaptureState(string.Empty);
        var editor = new Flame3DTransformEditorWindow(_flameTransforms) { Owner = this };
        editor.TransformsApplied += transforms =>
        {
            _flameTransforms = transforms.Select(t => t.Clone()).ToList();
            ClearIfsPreset();
            ScheduleRender();
        };
        editor.Randomized += () => BeginTransition(new Fractal3DPose(_orientation, 3.1f, Vector3.Zero));
        editor.ShowDialog();
        if (originalPreset >= 0 && Fractal3DRenderer.SameVolumeGeometry(original, CaptureState(string.Empty)))
        {
            _updatingUi = true;
            PresetBox.SelectedIndex = originalPreset;
            _updatingUi = false;
        }
    }
}
