using FractalExplorerWPF.Models;
using Vortice.Direct3D11;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    // Превью сохранений и каталога: своё моделирование на этом же устройстве, переиспользуется,
    // пока не изменились уравнение, затравка или контрольная точка.
    private Excitable3DGpuSimulation? _excitablePreview;
    private Excitable3DSettings? _excitablePreviewSource;
    private Excitable3DVolume? _excitablePreviewVolume;

    /// <summary>
    /// Объём V без копий через оперативную память. Живой кадр окна берётся у его моделирования;
    /// состояние без него (файл сохранения, пресет) готовится здесь до той же стадии, что в окне.
    /// Вызывается под очередью устройства.
    /// </summary>
    private ID3D11ShaderResourceView ExcitableVolumeView(Fractal3DState state, CancellationToken token)
    {
        Excitable3DSettings settings = state.Excitable;
        settings.Validate();
        if (settings.Live is { } live)
        {
            if (!live.Source.Uses(_host))
                throw new InvalidOperationException("Живой кадр Возбудимая среда 3D принадлежит другому устройству Direct3D.");
            return live.Source.ViewLocked(live);
        }

        if (_excitablePreview is null || _excitablePreviewSource is null || !_excitablePreviewSource.SameEvolution(settings))
        {
            _excitablePreviewSource = null;
            if (_excitablePreview is null) _excitablePreview = Excitable3DGpuSimulation.CreateLocked(_host, settings);
            else _excitablePreview.ResetLocked(settings);
            if (settings.Field is null) _excitablePreview.AdvanceLocked(settings.InitialSteps, token);
            _excitablePreviewVolume = _excitablePreview.PublishLocked();
            _excitablePreviewSource = settings;
        }
        return _excitablePreview.ViewLocked(_excitablePreviewVolume!);
    }

    private void DisposeExcitablePreview()
    {
        _excitablePreview?.DisposeWhileLocked();
        _excitablePreview = null;
        _excitablePreviewSource = null;
        _excitablePreviewVolume = null;
    }
}
