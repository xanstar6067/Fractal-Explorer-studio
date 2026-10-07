using FractalExplorerWPF.Models;
using Vortice.Direct3D11;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    // Превью сохранений и каталога: своё моделирование на этом же устройстве, переиспользуется,
    // пока не изменились уравнение, затравка или контрольная точка.
    private Physarum3DGpuSimulation? _physarumPreview;
    private Physarum3DSettings? _physarumPreviewSource;
    private Physarum3DVolume? _physarumPreviewVolume;

    /// <summary>
    /// Объём следа без копий через оперативную память. Живой кадр окна берётся у его моделирования;
    /// состояние без него (файл сохранения, пресет) готовится здесь до той же стадии, что в окне.
    /// Вызывается под очередью устройства.
    /// </summary>
    private ID3D11ShaderResourceView PhysarumVolumeView(Fractal3DState state, CancellationToken token)
    {
        Physarum3DSettings settings = state.Physarum;
        settings.Validate();
        if (settings.Live is { } live)
        {
            if (!live.Source.Uses(_host))
                throw new InvalidOperationException("Живой кадр Physarum 3D принадлежит другому устройству Direct3D.");
            return live.Source.ViewLocked(live);
        }

        if (_physarumPreview is null || _physarumPreviewSource is null || !_physarumPreviewSource.SameEvolution(settings))
        {
            _physarumPreviewSource = null;
            if (_physarumPreview is null) _physarumPreview = Physarum3DGpuSimulation.CreateLocked(_host, settings);
            else _physarumPreview.ResetLocked(settings);
            if (settings.Field is null) _physarumPreview.AdvanceLocked(settings.WarmupSteps, token);
            _physarumPreviewVolume = _physarumPreview.PublishLocked();
            _physarumPreviewSource = settings;
        }
        return _physarumPreview.ViewLocked(_physarumPreviewVolume!);
    }

    private void DisposePhysarumPreview()
    {
        _physarumPreview?.DisposeWhileLocked();
        _physarumPreview = null;
        _physarumPreviewSource = null;
        _physarumPreviewVolume = null;
    }
}
