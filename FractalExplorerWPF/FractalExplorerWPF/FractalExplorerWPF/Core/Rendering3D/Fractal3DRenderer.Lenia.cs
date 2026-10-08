using FractalExplorerWPF.Models;
using Vortice.Direct3D11;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    // Превью сохранений и каталога: своё моделирование на этом же устройстве, переиспользуется,
    // пока не изменились уравнение, затравка или контрольная точка.
    private Lenia3DGpuSimulation? _leniaPreview;
    private Lenia3DSettings? _leniaPreviewSource;
    private Lenia3DVolume? _leniaPreviewVolume;

    /// <summary>
    /// Объём клеток без копий через оперативную память. Живой кадр окна берётся у его моделирования;
    /// состояние без него (файл сохранения, пресет) готовится здесь до той же стадии, что в окне.
    /// Вызывается под очередью устройства.
    /// </summary>
    private ID3D11ShaderResourceView LeniaVolumeView(Fractal3DState state, CancellationToken token)
    {
        Lenia3DSettings settings = state.Lenia;
        settings.Validate();
        if (settings.Live is { } live)
        {
            if (!live.Source.Uses(_host))
                throw new InvalidOperationException("Живой кадр Lenia 3D принадлежит другому устройству Direct3D.");
            return live.Source.ViewLocked(live);
        }

        if (_leniaPreview is null || _leniaPreviewSource is null || !_leniaPreviewSource.SameEvolution(settings))
        {
            _leniaPreviewSource = null;
            if (_leniaPreview is null) _leniaPreview = Lenia3DGpuSimulation.CreateLocked(_host, settings);
            else _leniaPreview.ResetLocked(settings);
            if (settings.Field is null) _leniaPreview.AdvanceLocked(settings.WarmupSteps, token);
            _leniaPreviewVolume = _leniaPreview.PublishLocked();
            _leniaPreviewSource = settings;
        }
        return _leniaPreview.ViewLocked(_leniaPreviewVolume!);
    }

    private void DisposeLeniaPreview()
    {
        _leniaPreview?.DisposeWhileLocked();
        _leniaPreview = null;
        _leniaPreviewSource = null;
        _leniaPreviewVolume = null;
    }
}
