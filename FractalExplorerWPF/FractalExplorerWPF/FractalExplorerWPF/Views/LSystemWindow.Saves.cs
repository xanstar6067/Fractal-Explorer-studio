using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class LSystemWindow
{
    public LSystemState CaptureState(string name)
    {
        if (!TryReadDefinition(out var definition, out double duration))
            throw new InvalidOperationException("Проверьте параметры L-системы.");
        // Validate syntax without expanding a potentially large grammar on the UI thread.
        LSystemEngine.ParseRules(definition.RulesText);
        return new()
        {
            SaveName = name, Timestamp = DateTime.Now, Definition = definition.Clone(),
            ViewZoom = _viewZoom, PanX = _panX, PanY = _panY, AnimationDurationSeconds = duration
        };
    }

    public void LoadState(LSystemState state)
    {
        if (state.Definition is null || !double.IsFinite(state.ViewZoom) || state.ViewZoom is < .02 or > 1000 ||
            !double.IsFinite(state.PanX) || !double.IsFinite(state.PanY) ||
            !double.IsFinite(state.AnimationDurationSeconds) || state.AnimationDurationSeconds is < .5 or > 120)
            throw new InvalidOperationException("Некорректные параметры сохранённого вида.");
        Randomizer.CancelWork();
        StopAnimation();
        _buildCts?.Cancel();
        _randomUndo.Clear();
        Randomizer.SetUndoAvailable(false);
        PresetBox.SelectedIndex = -1;
        ApplyPreset(new LSystemPreset
        {
            Id = "saved", Name = state.SaveName, Description = $"Сохранение: {state.SaveName}",
            Definition = state.Definition.Clone()
        });
        _initializing = true;
        AnimationDurationBox.Text = state.AnimationDurationSeconds.ToString("G17", CultureInfo.InvariantCulture);
        _initializing = false;
        _animationDurationSeconds = state.AnimationDurationSeconds;
        _viewZoom = state.ViewZoom; _panX = state.PanX; _panY = state.PanY;
        UpdatePreviewTransform();
        _ = BuildSceneAsync(false);
    }

    public async Task<BitmapSource> RenderStatePreviewAsync(LSystemState state, int width, int height,
        CancellationToken token, IProgress<int>? progress = null)
    {
        var copy = state.Clone();
        var scene = await Task.Run(() => LSystemEngine.BuildScene(copy.Definition, token), token);
        return await RenderExportBitmapAsync(scene, copy.Definition, width, height,
            copy.ViewZoom, copy.PanX, copy.PanY, token, progress);
    }

    public BitmapSource? CaptureCurrentPreview(int width, int height)
    {
        if (!_hasRenderedFrame || _isBuilding || _isAnimating || _isFrameRendering || _redrawTimer.IsEnabled ||
            !TryReadDefinition(out var definition, out _)) return null;
        var options = JsonOptionsFactory.Create();
        if (JsonSerializer.Serialize(definition, options) != JsonSerializer.Serialize(_activeDefinition, options))
            return null;
        return SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, CanvasImage);
    }

    private void Saves_OnClick(object sender, RoutedEventArgs e) =>
        SaveManagerWindow.Open(this, SaveManagerConfigurations.ForLSystem(this, _saveStore));
}
