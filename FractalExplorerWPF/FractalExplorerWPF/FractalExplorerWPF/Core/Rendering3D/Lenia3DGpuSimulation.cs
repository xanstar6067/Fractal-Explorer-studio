using FractalExplorerWPF.Models;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Опубликованный кадр моделирования: объём клеток для рендера и точная копия клеток для сохранения.</summary>
public sealed record Lenia3DVolume(Lenia3DGpuSimulation Source, long Version, long Step, int Size, int Slot, double Time)
    : IGpuVolumeTicket<Lenia3DGpuSimulation>;

/// <summary>
/// GPU periodic Lenia: normalized radial convolution via FFT and clipped Euler growth.
/// Published slots keep the shown frame intact.
/// Caller and renderer share Direct3DDeviceHost; only checkpoints read back to the CPU.
/// </summary>
public sealed class Lenia3DGpuSimulation : GpuVolumeSimulation<Lenia3DSettings, Lenia3DField, Lenia3DVolume>
{
    private readonly GpuFieldSlot?[] _slots = new GpuFieldSlot?[SlotCount];
    private readonly bool? _tiledFft;
    private GpuStructuredBuffer? _current;
    private GpuFft3D _fft = null!;
    private GpuComplexBuffer _work = null!, _scratch = null!, _kernel = null!;
    private double _time;

    public Lenia3DGpuSimulation(Direct3DDeviceHost host, Lenia3DSettings settings) : this(host, settings, false) { }

    internal Lenia3DGpuSimulation(Direct3DDeviceHost host, Lenia3DSettings settings, bool gateHeld, bool? tiledFft = null)
        : base(host, Lenia3DComputeShader.CacheEntries, "Lenia3D", "Lenia 3D", 12)
    {
        _tiledFft = tiledFft;
        Initialize(settings, gateHeld);
    }

    /// <summary>Для рендера, который уже держит очередь устройства.</summary>
    internal static Lenia3DGpuSimulation CreateLocked(Direct3DDeviceHost host, Lenia3DSettings settings) =>
        new(host, settings, true);

    public double Time => _time;

    // Keep submission short enough to interleave camera strips and observe cancellation.
    // Two GPU batches overlap; the CPU waits only for the older batch.
    private protected override int StepsPerSubmit => 4;

    private protected override void CreateResourcesLocked() => _fft = new(Host, _tiledFft);

    private protected override void ResetCoreLocked(Lenia3DSettings settings)
    {
        int size = settings.Size;
        if (_current is null || Settings.Size != size)
        {
            DisposeFields();
            ID3D11Device device = Host.Device;
            _current = new(device, size * size * size, 4);
            _work = new(device, size); _scratch = new(device, size); _kernel = new(device, size);
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new(device, size, 4, Format.R32_Float);
        }
        float[] values = settings.Field?.Values ?? Lenia3DMath.InitialField(settings);
        Host.Context.UpdateSubresource(values, _current.Buffer);
        Settings = settings with { Field = null };
        CurrentStep = settings.Field?.Step ?? 0;
        _time = settings.Field?.Time ?? 0;
        Parameters[0] = (float)settings.TimeStep;
        Parameters[1] = (float)settings.GrowthMean; Parameters[2] = (float)settings.GrowthWidth;
        Parameters[3] = (float)settings.Growth; Parameters[4] = size;
        // Kernel origin is at index zero (wrapped negative offsets); no fftshift needed.
        float[] kernel = Lenia3DMath.Kernel(settings), packed = new float[kernel.Length * 4];
        for (int i = 0; i < kernel.Length; i++) packed[i*4] = kernel[i];
        Host.Context.UpdateSubresource(packed, _work.Buffer);
        _fft.Transform(size, false, ref _work, ref _scratch);
        Host.Context.CopyResource(_kernel.Buffer, _work.Buffer);
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

    private protected override Lenia3DVolume CreateVolume(long version, int slot) =>
        new(this, version, CurrentStep, Size, slot, _time);

    /// <summary>Текстура клеток опубликованного кадра.</summary>
    private protected override ID3D11ShaderResourceView DisplayView(int slot) => _slots[slot]!.Display.View;

    private protected override Lenia3DField ReadSlotLocked(Lenia3DVolume volume) =>
        new(Size, volume.Step, ReadFloatsLocked(_slots[volume.Slot]!.Field.Buffer), volume.Time, true);

    private protected override Lenia3DField ReadCurrentLocked() =>
        new(Size, CurrentStep, ReadFloatsLocked(_current!.Buffer), _time, true);

    private void Dispatch(string entry, ID3D11ShaderResourceView? input,
        ID3D11UnorderedAccessView? output, ID3D11UnorderedAccessView? display,
        ID3D11ShaderResourceView? spectrum = null, ID3D11UnorderedAccessView? work = null)
    {
        ID3D11DeviceContext context = BindLocked(entry);
        context.CSSetShaderResource(0, input!);
        context.CSSetShaderResource(1, spectrum!);
        context.CSSetShaderResource(2, _kernel.View);
        context.CSSetUnorderedAccessView(2, work!);
        context.CSSetUnorderedAccessView(0, output!);
        context.CSSetUnorderedAccessView(1, display!);
        DispatchCube(Lenia3DComputeShader.GroupX, Lenia3DComputeShader.GroupY, Lenia3DComputeShader.GroupZ);
        context.CSSetShaderResource(0, null!);
        context.CSSetShaderResource(1, null!);
        context.CSSetShaderResource(2, null!);
        context.CSSetUnorderedAccessView(2, null!);
        context.CSSetUnorderedAccessView(0, null!);
        context.CSSetUnorderedAccessView(1, null!);
    }

    private protected override void DisposeResourcesCore()
    {
        DisposeFields();
        _fft?.Dispose();
    }

    private void DisposeFields()
    {
        _current?.Dispose(); _current = null;
        _kernel?.Dispose(); _kernel = null!;
        _work?.Dispose(); _scratch?.Dispose(); _work = _scratch = null!;
        for (int i = 0; i < _slots.Length; i++) { _slots[i]?.Dispose(); _slots[i] = null; }
    }
}
