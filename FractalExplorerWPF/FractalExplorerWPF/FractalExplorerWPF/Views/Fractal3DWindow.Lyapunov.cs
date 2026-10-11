using System.Windows;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private Lyapunov3DSettings CaptureLyapunov()
    {
        if (Kind != Fractal3DKind.Lyapunov3D) return new();
        var settings = new Lyapunov3DSettings {
            Pattern = LyapunovPatternBox.Text, Size = LyapunovSizeBox.SelectedIndex switch { 0 => 32, 2 => 128, _ => 64 },
            Transient = ReadInt(LyapunovTransientBox, "Прогрев", 0, 4096),
            Samples = ReadInt(LyapunovSamplesBox, "Итераций измерения", 16, 4096),
            InitialX = ReadDouble(LyapunovInitialBox, "Начальное x", .000001, .999999),
            MinA = ReadDouble(LyapunovMinABox, "Минимум A", 0, 4), MaxA = ReadDouble(LyapunovMaxABox, "Максимум A", 0, 4),
            MinB = ReadDouble(LyapunovMinBBox, "Минимум B", 0, 4), MaxB = ReadDouble(LyapunovMaxBBox, "Максимум B", 0, 4),
            MinC = ReadDouble(LyapunovMinCBox, "Минимум C", 0, 4), MaxC = ReadDouble(LyapunovMaxCBox, "Максимум C", 0, 4),
            Region = (Lyapunov3DRegion)Math.Max(0, LyapunovRegionBox.SelectedIndex),
            Level = ReadDouble(LyapunovLevelBox, "Уровень λ", -2, 1),
            BandWidth = ReadDouble(LyapunovBandBox, "Полуширина полосы", .005, .5),
            ColorRange = ReadDouble(LyapunovRangeBox, "Диапазон окраски", .05, 4),
            CutAxis = Math.Max(0, LyapunovCutBox.SelectedIndex), CutPosition = LyapunovCutSlider.Value,
            SliceOnly = LyapunovSliceBox.IsChecked == true
        };
        settings.Validate();
        return settings;
    }

    private void LoadLyapunov(Lyapunov3DSettings? settings)
    {
        if (Kind != Fractal3DKind.Lyapunov3D) return;
        var s = settings ?? new(); s.Validate();
        LyapunovPatternBox.Text = s.Sequence;
        LyapunovSizeBox.SelectedIndex = s.Size switch { 32 => 0, 128 => 2, _ => 1 };
        LyapunovTransientBox.Text = s.Transient.ToString(); LyapunovSamplesBox.Text = s.Samples.ToString();
        LyapunovInitialBox.Text = Format(s.InitialX);
        LyapunovMinABox.Text = Format(s.MinA); LyapunovMaxABox.Text = Format(s.MaxA);
        LyapunovMinBBox.Text = Format(s.MinB); LyapunovMaxBBox.Text = Format(s.MaxB);
        LyapunovMinCBox.Text = Format(s.MinC); LyapunovMaxCBox.Text = Format(s.MaxC);
        LyapunovRegionBox.SelectedIndex = (int)s.Region;
        LyapunovLevelBox.Text = Format(s.Level); LyapunovBandBox.Text = Format(s.BandWidth);
        LyapunovRangeBox.Text = Format(s.ColorRange);
        LyapunovCutBox.SelectedIndex = s.CutAxis; LyapunovCutSlider.Value = s.CutPosition;
        LyapunovSliceBox.IsChecked = s.SliceOnly;
        UpdateLyapunovCutLabel();
    }

    private void LyapunovView_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingUi || Kind != Fractal3DKind.Lyapunov3D) return;
        if (LyapunovSliceBox.IsChecked == true && LyapunovCutBox.SelectedIndex == 0)
        {
            _updatingUi = true;
            LyapunovCutBox.SelectedIndex = 3;
            _updatingUi = false;
        }
        UpdateLyapunovCutLabel(); ScheduleRender();
    }

    private void UpdateLyapunovCutLabel()
    {
        int axis = LyapunovCutBox.SelectedIndex;
        LyapunovCutSlider.IsEnabled = axis > 0;
        if (axis <= 0) { LyapunovCutText.Text = ""; return; }
        try
        {
            var s = CaptureLyapunov();
            double min = axis == 1 ? s.MinA : axis == 2 ? s.MinB : s.MinC;
            double max = axis == 1 ? s.MaxA : axis == 2 ? s.MaxB : s.MaxC;
            LyapunovCutText.Text = $"{(char)('A'+axis-1)} = {min+(max-min)*(s.CutPosition+1)*.5:G7}";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { LyapunovCutText.Text = "Проверьте параметры поля."; }
    }

    private bool IsCurrentLyapunovFrame(Fractal3DState state)
    {
        if (Kind != Fractal3DKind.Lyapunov3D) return true;
        try
        {
            var current = CaptureLyapunov();
            return (current with { Pattern = current.Sequence }) == (state.Lyapunov with { Pattern = state.Lyapunov.Sequence });
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { return false; }
    }
}
