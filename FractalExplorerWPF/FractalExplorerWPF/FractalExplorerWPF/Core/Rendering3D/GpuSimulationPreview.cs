using Vortice.Direct3D11;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Объём моделирования на ГП для кадра рендера. Живой кадр окна берётся у его моделирования без
/// копий через оперативную память; состояние без него (файл сохранения, пресет, превью каталога)
/// готовится здесь до той же стадии, что в окне. Своё моделирование переиспользуется, пока не
/// изменились уравнение, затравка или контрольная точка. Все методы вызываются под очередью устройства.
/// <paramref name="warmupSteps"/> — шаги подготовки или null, если готовое поле не продолжается.
/// </summary>
internal sealed class GpuSimulationPreview<TSettings, TField, TVolume>(
    string title,
    Func<Direct3DDeviceHost, TSettings, GpuVolumeSimulation<TSettings, TField, TVolume>> createLocked,
    Func<TSettings, TVolume?> live,
    Func<TSettings, int?> warmupSteps,
    Func<TSettings, TSettings, bool> sameEvolution)
    where TSettings : class, IGpuSimulationSettings, new()
    where TVolume : class, IGpuVolumeTicket<object>
{
    private GpuVolumeSimulation<TSettings, TField, TVolume>? _simulation;
    private TSettings? _source;
    private TVolume? _volume;

    public ID3D11ShaderResourceView View(Direct3DDeviceHost host, TSettings settings, CancellationToken token)
    {
        settings.Validate();
        if (live(settings) is { } frame)
        {
            var owner = (GpuVolumeSimulation<TSettings, TField, TVolume>)frame.Source;
            if (!owner.Uses(host))
                throw new InvalidOperationException($"Живой кадр {title} принадлежит другому устройству Direct3D.");
            return owner.ViewLocked(frame);
        }

        if (_simulation is null || _source is null || !sameEvolution(_source, settings))
        {
            _source = null;
            if (_simulation is null) _simulation = createLocked(host, settings);
            else _simulation.ResetLocked(settings);
            if (warmupSteps(settings) is int steps) _simulation.AdvanceLocked(steps, token);
            _volume = _simulation.PublishLocked();
            _source = settings;
        }
        return _simulation.ViewLocked(_volume!);
    }

    public void DisposeLocked()
    {
        _simulation?.DisposeWhileLocked();
        _simulation = null;
        _source = null;
        _volume = null;
    }
}
