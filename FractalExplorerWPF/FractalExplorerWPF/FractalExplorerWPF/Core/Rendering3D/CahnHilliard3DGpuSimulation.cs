using FractalExplorerWPF.Models;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Опубликованный кадр моделирования: объём состава для рендера и точная копия состава для сохранения.</summary>
public sealed record CahnHilliard3DVolume(CahnHilliard3DGpuSimulation Source, long Version, long Step, int Size, int Slot, double Time)
    : IGpuVolumeTicket<CahnHilliard3DGpuSimulation>;

/// <summary>
/// GPU pseudospectral Cahn–Hilliard: stabilized semi-implicit Euler, periodic XYZ, dx=1.
/// Two complex channels transform c and c³−c together; the fourth-order term is implicit.
/// Zero mode is pinned to the checkpoint mass. Published slots keep the shown frame intact.
/// Caller and renderer share Direct3DDeviceHost; only checkpoints read back to the CPU.
/// </summary>
public sealed class CahnHilliard3DGpuSimulation : GpuVolumeSimulation<CahnHilliard3DSettings, CahnHilliard3DField, CahnHilliard3DVolume>
{
    private readonly GpuFieldSlot?[] _slots = new GpuFieldSlot?[SlotCount];
    private readonly bool? _tiledFft;
    private GpuStructuredBuffer? _current;
    private GpuFft3D _fft = null!;
    private GpuComplexBuffer _work = null!, _scratch = null!;
    private double _time;
    private float _mean;

    public CahnHilliard3DGpuSimulation(Direct3DDeviceHost host, CahnHilliard3DSettings settings) : this(host, settings, false) { }

    internal CahnHilliard3DGpuSimulation(Direct3DDeviceHost host, CahnHilliard3DSettings settings, bool gateHeld, bool? tiledFft = null)
        : base(host, CahnHilliard3DComputeShader.CacheEntries, "CahnHilliard3D", "Кана–Хиллиарда 3D", 12)
    {
        _tiledFft = tiledFft;
        Initialize(settings, gateHeld);
    }

    /// <summary>Для рендера, который уже держит очередь устройства.</summary>
    internal static CahnHilliard3DGpuSimulation CreateLocked(Direct3DDeviceHost host, CahnHilliard3DSettings settings) =>
        new(host, settings, true);

    public double Time => _time;

    // Keep submission short enough to interleave camera strips and observe cancellation.
    // Two GPU batches overlap; the CPU waits only for the older batch.
    private protected override int StepsPerSubmit => 4;

    private protected override void CreateResourcesLocked() => _fft = new(Host, _tiledFft);

    private protected override void ResetCoreLocked(CahnHilliard3DSettings settings)
    {
        int size = settings.Size;
        if (_current is null || Settings.Size != size)
        {
            DisposeFields();
            ID3D11Device device = Host.Device;
            _current = new(device, size * size * size, 4);
            _work = new(device, size); _scratch = new(device, size);
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new(device, size, 4, Format.R32_Float);
        }
        float[] values = settings.Field?.Values ?? InitialField(settings);
        Host.Context.UpdateSubresource(values, _current.Buffer);
        Settings = settings with { Field = null };
        CurrentStep = settings.Field?.Step ?? 0;
        _time = settings.Field?.Time ?? 0;
        _mean = (float)(settings.Field?.Mean ?? settings.Mean);
        Parameters[0] = (float)(settings.TimeStep * settings.Mobility);
        Parameters[1] = (float)settings.Kappa; Parameters[2] = 3;
        Parameters[3] = _mean; Parameters[4] = size;
        ConstantsDirty = true;
    }

    private protected override void StepLocked()
    {
        Dispatch("Prepare", _current!.View, null, null, work: _work.WriteView);
        _fft.Transform(Size, false, ref _work, ref _scratch);
        Dispatch("Evolve", null, null, null, _work.View, _scratch.WriteView);
        (_work, _scratch) = (_scratch, _work);
        _fft.Transform(Size, true, ref _work, ref _scratch);
        Dispatch("Finish", null, _current.WriteView, null, _work.View);
        _time += Settings.TimeStep;
    }

    private protected override void PublishToSlotLocked(int slot)
    {
        GpuFieldSlot target = _slots[slot]!;
        Host.Context.CopyResource(target.Field.Buffer, _current!.Buffer);
        Dispatch("Publish", target.Field.View, null, target.Display.WriteView);
    }

    private protected override CahnHilliard3DVolume CreateVolume(long version, int slot) =>
        new(this, version, CurrentStep, Size, slot, _time);

    /// <summary>Текстура состава опубликованного кадра.</summary>
    private protected override ID3D11ShaderResourceView DisplayView(int slot) => _slots[slot]!.Display.View;

    private protected override CahnHilliard3DField ReadSlotLocked(CahnHilliard3DVolume volume) =>
        new(Size, volume.Step, ReadFloatsLocked(_slots[volume.Slot]!.Field.Buffer), volume.Time, _mean, true);

    private protected override CahnHilliard3DField ReadCurrentLocked() =>
        new(Size, CurrentStep, ReadFloatsLocked(_current!.Buffer), _time, _mean, true);

    private void Dispatch(string entry, ID3D11ShaderResourceView? input,
        ID3D11UnorderedAccessView? output, ID3D11UnorderedAccessView? display,
        ID3D11ShaderResourceView? spectrum = null, ID3D11UnorderedAccessView? work = null)
    {
        ID3D11DeviceContext context = BindLocked(entry);
        context.CSSetShaderResource(0, input!);
        context.CSSetShaderResource(1, spectrum!);
        context.CSSetUnorderedAccessView(2, work!);
        context.CSSetUnorderedAccessView(0, output!);
        context.CSSetUnorderedAccessView(1, display!);
        DispatchCube(CahnHilliard3DComputeShader.GroupX, CahnHilliard3DComputeShader.GroupY, CahnHilliard3DComputeShader.GroupZ);
        context.CSSetShaderResource(0, null!);
        context.CSSetShaderResource(1, null!);
        context.CSSetUnorderedAccessView(2, null!);
        context.CSSetUnorderedAccessView(0, null!);
        context.CSSetUnorderedAccessView(1, null!);
    }

    internal static float[] InitialField(CahnHilliard3DSettings s)
    {
        int n = s.Size; var field = new float[n*n*n]; var random = new Random(s.Seed);
        double total = 0;
        for (int z = 0; z < n; z++) for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            double px = 4*Math.PI*x/n, py = 4*Math.PI*y/n, pz = 4*Math.PI*z/n;
            double shape = s.SeedShape switch
            {
                CahnHilliard3DSeed.Gyroid => .25*(Math.Sin(px)*Math.Cos(py)+Math.Sin(py)*Math.Cos(pz)+Math.Sin(pz)*Math.Cos(px)),
                CahnHilliard3DSeed.Layers => .25*Math.Sin(pz),
                _ => 0
            };
            int i = (z*n+y)*n+x;
            field[i] = (float)(shape+s.Noise*(2*random.NextDouble()-1)); total += field[i];
        }
        double correction = s.Mean-total/field.Length;
        for (int i = 0; i < field.Length; i++) field[i] = (float)(field[i]+correction);
        return field;
    }

    private protected override void DisposeResourcesCore()
    {
        DisposeFields();
        _fft?.Dispose();
    }

    private void DisposeFields()
    {
        _current?.Dispose(); _current = null;
        _work?.Dispose(); _scratch?.Dispose(); _work = _scratch = null!;
        for (int i = 0; i < _slots.Length; i++) { _slots[i]?.Dispose(); _slots[i] = null; }
    }
}
