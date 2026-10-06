using FractalExplorerWPF.Models;
using Vortice.Direct3D11;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    // Превью сохранений и каталога: своё моделирование на этом же устройстве, переиспользуется,
    // пока не изменились правило, стартовое поле или контрольная точка.
    private Turing3DGpuSimulation? _turingPreview;
    private Turing3DSettings? _turingPreviewSource;
    private Turing3DVolume? _turingPreviewVolume;

    /// <summary>
    /// Объём узора без копий через оперативную память. Живой кадр окна берётся у его моделирования;
    /// состояние без него (файл сохранения, пресет) готовится здесь до той же стадии, что в окне.
    /// Вызывается под очередью устройства.
    /// </summary>
    private ID3D11ShaderResourceView TuringVolumeView(Fractal3DState state, CancellationToken token)
    {
        Turing3DSettings settings = state.Turing;
        settings.Validate();
        if (settings.Live is { } live)
        {
            if (!live.Source.Uses(_host))
                throw new InvalidOperationException("Живой кадр узора Тьюринга 3D принадлежит другому устройству Direct3D.");
            return live.Source.ViewLocked(live);
        }

        if (_turingPreview is null || _turingPreviewSource is null || !_turingPreviewSource.SameEvolution(settings))
        {
            _turingPreviewSource = null;
            if (_turingPreview is null) _turingPreview = Turing3DGpuSimulation.CreateLocked(_host, settings);
            else _turingPreview.ResetLocked(settings);
            _turingPreview.AdvanceLocked(settings.InitialSteps, token);
            _turingPreviewVolume = _turingPreview.PublishLocked();
            _turingPreviewSource = settings;
        }
        return _turingPreview.ViewLocked(_turingPreviewVolume!);
    }

    private void DisposeTuringPreview()
    {
        _turingPreview?.DisposeWhileLocked();
        _turingPreview = null;
        _turingPreviewSource = null;
        _turingPreviewVolume = null;
    }
}
