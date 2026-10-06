using FractalExplorerWPF.Models;
using Vortice.Direct3D11;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    // Превью сохранений и каталога: своё моделирование на этом же устройстве, переиспользуется,
    // пока не изменились уравнение, затравка или контрольная точка.
    private CahnHilliard3DGpuSimulation? _cahnPreview;
    private CahnHilliard3DSettings? _cahnPreviewSource;
    private CahnHilliard3DVolume? _cahnPreviewVolume;

    /// <summary>
    /// Объём состава без копий через оперативную память. Живой кадр окна берётся у его моделирования;
    /// состояние без него (файл сохранения, пресет) готовится здесь до той же стадии, что в окне.
    /// Вызывается под очередью устройства.
    /// </summary>
    private ID3D11ShaderResourceView CahnHilliardVolumeView(Fractal3DState state, CancellationToken token)
    {
        CahnHilliard3DSettings settings = state.CahnHilliard;
        settings.Validate();
        if (settings.Live is { } live)
        {
            if (!live.Source.Uses(_host))
                throw new InvalidOperationException("Живой кадр Кана–Хиллиарда 3D принадлежит другому устройству Direct3D.");
            return live.Source.ViewLocked(live);
        }

        if (_cahnPreview is null || _cahnPreviewSource is null || !_cahnPreviewSource.SameEvolution(settings))
        {
            _cahnPreviewSource = null;
            if (_cahnPreview is null) _cahnPreview = CahnHilliard3DGpuSimulation.CreateLocked(_host, settings);
            else _cahnPreview.ResetLocked(settings);
            if (settings.Field is null) _cahnPreview.AdvanceLocked(settings.WarmupSteps, token);
            _cahnPreviewVolume = _cahnPreview.PublishLocked();
            _cahnPreviewSource = settings;
        }
        return _cahnPreview.ViewLocked(_cahnPreviewVolume!);
    }

    private void DisposeCahnHilliardPreview()
    {
        _cahnPreview?.DisposeWhileLocked();
        _cahnPreview = null;
        _cahnPreviewSource = null;
        _cahnPreviewVolume = null;
    }
}
