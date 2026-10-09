using FractalExplorerWPF.Models;
using Vortice.Direct3D11;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    // Превью сохранений и каталога: своё моделирование на этом же устройстве, переиспользуется,
    // пока не изменились уравнение, затравка или контрольная точка.
    private Kobayashi3DGpuSimulation? _kobPreview;
    private Kobayashi3DSettings? _kobPreviewSource;
    private Kobayashi3DVolume? _kobPreviewVolume;

    /// <summary>
    /// Объём фазы и температуры без копий через оперативную память. Живой кадр окна берётся у его моделирования;
    /// состояние без него (файл сохранения, пресет) готовится здесь до той же стадии, что в окне.
    /// Вызывается под очередью устройства.
    /// </summary>
    private ID3D11ShaderResourceView KobayashiVolumeView(Fractal3DState state, CancellationToken token)
    {
        Kobayashi3DSettings settings = state.Kobayashi;
        settings.Validate();
        if (settings.Live is { } live)
        {
            if (!live.Source.Uses(_host))
                throw new InvalidOperationException("Живой кадр Кобаяси 3D принадлежит другому устройству Direct3D.");
            return live.Source.ViewLocked(live);
        }

        if (_kobPreview is null || _kobPreviewSource is null || !_kobPreviewSource.SameEvolution(settings))
        {
            _kobPreviewSource = null;
            if (_kobPreview is null) _kobPreview = Kobayashi3DGpuSimulation.CreateLocked(_host, settings);
            else _kobPreview.ResetLocked(settings);
            if (settings.Field is null) _kobPreview.AdvanceLocked(settings.InitialSteps, token);
            _kobPreviewVolume = _kobPreview.PublishLocked();
            _kobPreviewSource = settings;
        }
        return _kobPreview.ViewLocked(_kobPreviewVolume!);
    }

    private void DisposeKobayashiPreview()
    {
        _kobPreview?.DisposeWhileLocked();
        _kobPreview = null;
        _kobPreviewSource = null;
        _kobPreviewVolume = null;
    }
}
