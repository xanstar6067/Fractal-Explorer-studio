using System.Windows;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;

using static FractalExplorerWPF.Controls.PanelFields;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private void RememberPreference(object sender)
    {
        if (_updatingUi || sender is not FrameworkElement control) return;
        try
        {
            // Читается только изменённое поле. Незавершённый ввод в другом поле
            // или параметры конкретного фрактала не мешают сохранить предпочтение.
            Action<Fractal3DPreferences>? update = control.Name switch
            {
                nameof(NavigationModeBox) => p => p.NavigationMode = SelectedNavigationMode,
                nameof(GameSpeedSlider) => p => p.GameMovementSpeed = GameSpeedSlider.Value,
                nameof(GameSensitivitySlider) => p => p.GameMouseSensitivity = GameSensitivitySlider.Value,
                nameof(MotionQualityBox) => p => p.MotionQuality = SelectedMotionQuality,
                nameof(MotionResolutionBox) => p => p.MotionResolution = SelectedMotionResolution,
                nameof(RotationInertiaBox) => p => p.RotationInertia = RotationInertiaBox.IsChecked == true,
                nameof(AutoRotateBox) => p => p.AutoRotate = AutoRotateBox.IsChecked == true,
                nameof(AutoRotateSpeedBox) => p => p.AutoRotateSpeed = ReadDouble(AutoRotateSpeedBox, "Скорость автовращения", -720, 720),
                nameof(MaxStepsBox) => p => p.MaxSteps = ReadInt(MaxStepsBox, "Шагов луча", 16, 1024),
                nameof(DetailBox) => p => p.Detail = ReadDouble(DetailBox, "Детализация", 0.05, 8),
                nameof(MaxDistanceBox) => p => p.MaxDistance = ReadDouble(MaxDistanceBox, "Дальность трассировки", 1, 1000),
                nameof(SsaaBox) => p => p.Ssaa = SelectedSsaa,
                nameof(SoftShadowsBox) => p => p.SoftShadows = SoftShadowsBox.IsChecked == true,
                nameof(ShadowSharpnessBox) => p => p.ShadowSharpness = ReadDouble(ShadowSharpnessBox, "Жёсткость теней", 1, 128),
                nameof(AmbientOcclusionBox) => p => p.AmbientOcclusion = AmbientOcclusionBox.IsChecked == true,
                nameof(AoStrengthBox) => p => p.AoStrength = ReadDouble(AoStrengthBox, "Сила затенения", 0, 1),
                _ => null
            };
            if (update is not null) Fractal3DPreferencesStore.Update(update);
        }
        catch (InvalidOperationException)
        {
            // Пустое поле, знак или число вне диапазона при вводе не заменяют последнюю настройку.
        }
    }
}
