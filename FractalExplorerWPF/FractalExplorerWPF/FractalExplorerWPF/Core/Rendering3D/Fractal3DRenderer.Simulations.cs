using FractalExplorerWPF.Models;
using Vortice.Direct3D11;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Объёмы моделирований на ГП (<see cref="GpuVolumeSimulation{TSettings, TField, TVolume}"/>): живой кадр
/// окна или собственное превью рендера для сохранений, пресетов и каталога.
/// </summary>
public sealed partial class Fractal3DRenderer
{
    private readonly GpuSimulationPreview<GrayScott3DSettings, GrayScott3DField, GrayScott3DVolume> _grayScottPreview = new(
        "Gray–Scott 3D", GrayScott3DGpuSimulation.CreateLocked, s => s.Live,
        s => s.Field is null ? s.InitialSteps : null, (a, b) => a.SameEvolution(b));

    private readonly GpuSimulationPreview<Turing3DSettings, Turing3DField, Turing3DVolume> _turingPreview = new(
        "узора Тьюринга 3D", Turing3DGpuSimulation.CreateLocked, s => s.Live,
        s => s.InitialSteps, (a, b) => a.SameEvolution(b));

    private readonly GpuSimulationPreview<CahnHilliard3DSettings, CahnHilliard3DField, CahnHilliard3DVolume> _cahnHilliardPreview = new(
        "Кана–Хиллиарда 3D", CahnHilliard3DGpuSimulation.CreateLocked, s => s.Live,
        s => s.Field is null ? s.WarmupSteps : null, (a, b) => a.SameEvolution(b));

    private readonly GpuSimulationPreview<Lenia3DSettings, Lenia3DField, Lenia3DVolume> _leniaPreview = new(
        "Lenia 3D", Lenia3DGpuSimulation.CreateLocked, s => s.Live,
        s => s.Field is null ? s.WarmupSteps : null, (a, b) => a.SameEvolution(b));

    private readonly GpuSimulationPreview<Physarum3DSettings, Physarum3DField, Physarum3DVolume> _physarumPreview = new(
        "Physarum 3D", Physarum3DGpuSimulation.CreateLocked, s => s.Live,
        s => s.Field is null ? s.WarmupSteps : null, (a, b) => a.SameEvolution(b));

    private readonly GpuSimulationPreview<Kobayashi3DSettings, Kobayashi3DField, Kobayashi3DVolume> _kobayashiPreview = new(
        "Кобаяси 3D", Kobayashi3DGpuSimulation.CreateLocked, s => s.Live,
        s => s.Field is null ? s.InitialSteps : null, (a, b) => a.SameEvolution(b));

    private readonly GpuSimulationPreview<Excitable3DSettings, Excitable3DField, Excitable3DVolume> _excitablePreview = new(
        "свитковых волн 3D", Excitable3DGpuSimulation.CreateLocked, s => s.Live,
        s => s.Field is null ? s.InitialSteps : null, (a, b) => a.SameEvolution(b));

    private readonly GpuSimulationPreview<GinzburgLandau3DSettings, GinzburgLandau3DField, GinzburgLandau3DVolume> _ginzburgLandauPreview = new(
        "Гинзбурга–Ландау 3D", GinzburgLandau3DGpuSimulation.CreateLocked, s => s.Live,
        s => s.Field is null ? s.WarmupSteps : null, (a, b) => a.SameEvolution(b));

    private readonly GpuSimulationPreview<SwiftHohenberg3DSettings, SwiftHohenberg3DField, SwiftHohenberg3DVolume> _swiftHohenbergPreview = new(
        "Свифта–Хоэнберга 3D", SwiftHohenberg3DGpuSimulation.CreateLocked, s => s.Live,
        s => s.Field is null ? s.WarmupSteps : null, (a, b) => a.SameEvolution(b));

    /// <summary>Объём моделирования для кадра или null, если режим не моделируется на ГП. Под очередью устройства.</summary>
    private ID3D11ShaderResourceView? SimulationVolumeView(Fractal3DState state, CancellationToken token) => state.Kind switch
    {
        Fractal3DKind.GrayScott3D => _grayScottPreview.View(_host, state.GrayScott, token),
        Fractal3DKind.Turing3D => _turingPreview.View(_host, state.Turing, token),
        Fractal3DKind.CahnHilliard3D => _cahnHilliardPreview.View(_host, state.CahnHilliard, token),
        Fractal3DKind.Lenia3D => _leniaPreview.View(_host, state.Lenia, token),
        Fractal3DKind.Physarum3D => _physarumPreview.View(_host, state.Physarum, token),
        Fractal3DKind.Kobayashi3D => _kobayashiPreview.View(_host, state.Kobayashi, token),
        Fractal3DKind.Excitable3D => _excitablePreview.View(_host, state.Excitable, token),
        Fractal3DKind.GinzburgLandau3D => _ginzburgLandauPreview.View(_host, state.GinzburgLandau, token),
        Fractal3DKind.SwiftHohenberg3D => _swiftHohenbergPreview.View(_host, state.SwiftHohenberg, token),
        _ => null
    };

    private void DisposeSimulationPreviews()
    {
        _grayScottPreview.DisposeLocked();
        _turingPreview.DisposeLocked();
        _cahnHilliardPreview.DisposeLocked();
        _leniaPreview.DisposeLocked();
        _physarumPreview.DisposeLocked();
        _kobayashiPreview.DisposeLocked();
        _excitablePreview.DisposeLocked();
        _ginzburgLandauPreview.DisposeLocked();
        _swiftHohenbergPreview.DisposeLocked();
    }
}
