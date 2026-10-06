using FractalExplorerWPF.Models;
using Vortice.Direct3D11;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    // Превью сохранений и каталога: своё моделирование на этом же устройстве, переиспользуется,
    // пока не изменились уравнение, затравка или контрольная точка.
    private GrayScott3DGpuSimulation? _grayPreview;
    private GrayScott3DSettings? _grayPreviewSource;
    private GrayScott3DVolume? _grayPreviewVolume;

    /// <summary>
    /// Объём V без копий через оперативную память. Живой кадр окна берётся у его моделирования;
    /// состояние без него (файл сохранения, пресет) готовится здесь до той же стадии, что в окне.
    /// Вызывается под очередью устройства.
    /// </summary>
    private ID3D11ShaderResourceView GrayScottVolumeView(Fractal3DState state, CancellationToken token)
    {
        GrayScott3DSettings settings = state.GrayScott;
        settings.Validate();
        if (settings.Live is { } live)
        {
            if (!live.Source.Uses(_host))
                throw new InvalidOperationException("Живой кадр Gray–Scott 3D принадлежит другому устройству Direct3D.");
            return live.Source.ViewLocked(live);
        }

        if (_grayPreview is null || _grayPreviewSource is null || !_grayPreviewSource.SameEvolution(settings))
        {
            _grayPreviewSource = null;
            if (_grayPreview is null) _grayPreview = GrayScott3DGpuSimulation.CreateLocked(_host, settings);
            else _grayPreview.ResetLocked(settings);
            if (settings.Field is null) _grayPreview.AdvanceLocked(settings.InitialSteps, token);
            _grayPreviewVolume = _grayPreview.PublishLocked();
            _grayPreviewSource = settings;
        }
        return _grayPreview.ViewLocked(_grayPreviewVolume!);
    }

    private void DisposeGrayScottPreview()
    {
        _grayPreview?.DisposeWhileLocked();
        _grayPreview = null;
        _grayPreviewSource = null;
        _grayPreviewVolume = null;
    }
}
