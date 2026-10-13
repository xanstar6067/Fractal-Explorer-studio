using FractalExplorerWPF.Models;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed record SwiftHohenberg3DVolume(SwiftHohenberg3DGpuSimulation Source, long Version, long Step, int Size, int Slot)
    : IGpuVolumeTicket<SwiftHohenberg3DGpuSimulation>;

/// <summary>Periodic Fourier-Galerkin simulation with alias-free cubic projection and ETD1 steps.
/// Uses the shared two-channel FFT, device queue, publication slots and checkpoint infrastructure.</summary>
public sealed class SwiftHohenberg3DGpuSimulation
    : GpuVolumeSimulation<SwiftHohenberg3DSettings, SwiftHohenberg3DField, SwiftHohenberg3DVolume>
{
    private readonly GpuFieldSlot?[] _slots = new GpuFieldSlot?[SlotCount];
    private readonly bool? _tiledFft;
    private GpuStructuredBuffer? _current;
    private GpuComplexBuffer _work = null!, _scratch = null!;
    private GpuFft3D _fft = null!;

    public SwiftHohenberg3DGpuSimulation(Direct3DDeviceHost host, SwiftHohenberg3DSettings settings) : this(host, settings, false) { }

    internal SwiftHohenberg3DGpuSimulation(Direct3DDeviceHost host, SwiftHohenberg3DSettings settings, bool gateHeld, bool? tiledFft = null)
        : base(host, SwiftHohenberg3DComputeShader.CacheEntries, "SwiftHohenberg3D", "Свифта–Хоэнберга 3D", 12)
    {
        _tiledFft = tiledFft; Initialize(settings, gateHeld);
    }

    internal static SwiftHohenberg3DGpuSimulation CreateLocked(Direct3DDeviceHost host, SwiftHohenberg3DSettings settings)
        => new(host, settings, true);
    private protected override int StepsPerSubmit => 4;
    private protected override void CreateResourcesLocked() => _fft = new(Host, _tiledFft);

    private protected override void ResetCoreLocked(SwiftHohenberg3DSettings settings)
    {
        int n = settings.Size;
        if (_current is null || Settings.Size != n)
        {
            DisposeFields();
            _current = new(Host.Device, n * n * n, 4);
            _work = new(Host.Device, n); _scratch = new(Host.Device, n);
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new(Host.Device, n, 4, Format.R32_Float);
        }
        Host.Context.UpdateSubresource(settings.Field?.Values ?? InitialField(settings), _current.Buffer);
        Settings = settings with { Field = null, Live = null }; CurrentStep = settings.Field?.Step ?? 0;
        Parameters[0] = (float)settings.R; Parameters[1] = (float)settings.Quadratic;
        Parameters[2] = (float)(settings.WaveNumber * settings.WaveNumber); Parameters[3] = (float)settings.TimeStep;
        Parameters[4] = n; Parameters[5] = (float)(Math.Tau / (n * settings.Spacing)); ConstantsDirty = true;
        // Fresh seeds and brush edits enter the Galerkin band once. Disk checkpoints are not modified.
        if (settings.Field is null) ProjectLocked();
    }

    private protected override void StepLocked()
    {
        PrepareSpectrum(); SpectralPass("Evolve"); FinishSpectrum();
    }

    private void ProjectLocked()
    {
        PrepareSpectrum(); SpectralPass("Filter"); FinishSpectrum();
    }

    private void PrepareSpectrum()
    {
        Dispatch("Prepare", _current!.View, work: _work.WriteView);
        _fft.Transform(Size, false, ref _work, ref _scratch);
    }

    private void SpectralPass(string entry)
    {
        Dispatch(entry, spectrum: _work.View, work: _scratch.WriteView);
        (_work, _scratch) = (_scratch, _work);
    }

    private void FinishSpectrum()
    {
        _fft.Transform(Size, true, ref _work, ref _scratch);
        Dispatch("Finish", output: _current!.WriteView, spectrum: _work.View);
    }

    public void Inject(double x, double y, double z, double radius, double value)
    {
        if (!double.IsFinite(x + y + z + radius + value) || x is < 0 or > 1 || y is < 0 or > 1 ||
            z is < 0 or > 1 || radius is <= 0 or > .3 || value is < -1 or > 1)
            throw new ArgumentOutOfRangeException(nameof(radius));
        Host.Gate.Wait();
        try
        {
            Parameters[6] = (float)value; Parameters[8] = (float)x; Parameters[9] = (float)y;
            Parameters[10] = (float)z; Parameters[11] = (float)radius; ConstantsDirty = true;
            Dispatch("Inject", output: _current!.WriteView); ProjectLocked();
        }
        finally { Host.Gate.Release(); }
    }

    private protected override void PublishToSlotLocked(int slot)
    {
        GpuFieldSlot target = _slots[slot]!;
        Host.Context.CopyResource(target.Field.Buffer, _current!.Buffer);
        Dispatch("Publish", target.Field.View, display: target.Display.WriteView);
    }
    private protected override SwiftHohenberg3DVolume CreateVolume(long version, int slot) => new(this, version, CurrentStep, Size, slot);
    private protected override ID3D11ShaderResourceView DisplayView(int slot) => _slots[slot]!.Display.View;
    private protected override SwiftHohenberg3DField ReadSlotLocked(SwiftHohenberg3DVolume volume) =>
        new(Size, volume.Step, ReadFloatsLocked(_slots[volume.Slot]!.Field.Buffer), true);
    private protected override SwiftHohenberg3DField ReadCurrentLocked() => new(Size, CurrentStep, ReadFloatsLocked(_current!.Buffer), true);

    private void Dispatch(string entry, ID3D11ShaderResourceView? input = null, ID3D11UnorderedAccessView? output = null,
        ID3D11UnorderedAccessView? display = null, ID3D11ShaderResourceView? spectrum = null, ID3D11UnorderedAccessView? work = null)
    {
        var context = BindLocked(entry);
        context.CSSetShaderResource(0, input!); context.CSSetShaderResource(1, spectrum!);
        context.CSSetUnorderedAccessView(0, output!); context.CSSetUnorderedAccessView(1, display!); context.CSSetUnorderedAccessView(2, work!);
        DispatchCube(SwiftHohenberg3DComputeShader.GroupX, SwiftHohenberg3DComputeShader.GroupY, SwiftHohenberg3DComputeShader.GroupZ);
        context.CSSetShaderResource(0, null!); context.CSSetShaderResource(1, null!);
        context.CSSetUnorderedAccessView(0, null!); context.CSSetUnorderedAccessView(1, null!); context.CSSetUnorderedAccessView(2, null!);
    }

    internal static float[] InitialField(SwiftHohenberg3DSettings s)
    {
        int n = s.Size;
        int frequency = Math.Clamp((int)Math.Round(n * s.Spacing * s.WaveNumber / Math.Tau), 1, n / 4 - 1);
        int diagonal = Math.Clamp((int)Math.Round(frequency / Math.Sqrt(2)), 1, n / 4 - 1);
        int hexX = Math.Clamp((int)Math.Round(frequency / 2.0), 1, (n / 4 - 1) / 2);
        int hexY = Math.Clamp((int)Math.Round(Math.Sqrt(3) * hexX), 1, n / 4 - 1);
        var random = new Random(s.Seed); float[] values = new float[n * n * n];
        for (int z = 0; z < n; z++) for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            double px = Math.Tau * x / n, py = Math.Tau * y / n, pz = Math.Tau * z / n;
            double lattice = Math.Cos(diagonal * px) * Math.Cos(diagonal * py) +
                Math.Cos(diagonal * py) * Math.Cos(diagonal * pz) + Math.Cos(diagonal * pz) * Math.Cos(diagonal * px);
            double u = s.SeedShape switch
            {
                SwiftHohenberg3DSeed.Layers => .3 * Math.Cos(frequency * pz),
                SwiftHohenberg3DSeed.TiltedLayers => .3 * Math.Cos(diagonal * (px + pz)),
                SwiftHohenberg3DSeed.HexagonalColumns => .2 * (Math.Cos(2 * hexX * px) +
                    Math.Cos(-hexX * px + hexY * py) + Math.Cos(-hexX * px - hexY * py)),
                SwiftHohenberg3DSeed.CubicSpots => .2 * lattice,
                SwiftHohenberg3DSeed.Localized => .35 * lattice * Math.Exp(-
                    (Math.Pow((x + .5) / n - .5, 2) + Math.Pow((y + .5) / n - .5, 2) + Math.Pow((z + .5) / n - .5, 2)) / .025),
                SwiftHohenberg3DSeed.Uniform => .2,
                _ => 0
            };
            values[(z * n + y) * n + x] = (float)(u + s.Noise * (2 * random.NextDouble() - 1));
        }
        return values;
    }

    private protected override void DisposeResourcesCore() { DisposeFields(); _fft?.Dispose(); }
    private void DisposeFields()
    {
        _current?.Dispose(); _current = null;
        _work?.Dispose(); _scratch?.Dispose(); _work = _scratch = null!;
        for (int i = 0; i < _slots.Length; i++) { _slots[i]?.Dispose(); _slots[i] = null; }
    }
}
