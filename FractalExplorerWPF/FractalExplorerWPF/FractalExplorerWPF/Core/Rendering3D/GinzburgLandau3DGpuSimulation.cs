using FractalExplorerWPF.Models;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed record GinzburgLandau3DVolume(GinzburgLandau3DGpuSimulation Source, long Version, long Step, int Size, int Slot)
    : IGpuVolumeTicket<GinzburgLandau3DGpuSimulation>;

/// <summary>Exact local reaction half-steps around stable explicit complex diffusion.
/// Shared publication slots retain both the display volume and lossless Re/Im checkpoints.</summary>
public sealed class GinzburgLandau3DGpuSimulation
    : GpuVolumeSimulation<GinzburgLandau3DSettings, GinzburgLandau3DField, GinzburgLandau3DVolume>
{
    private GpuStructuredBuffer? _current, _next;
    private readonly GpuFieldSlot?[] _slots = new GpuFieldSlot?[SlotCount];

    public GinzburgLandau3DGpuSimulation(Direct3DDeviceHost host, GinzburgLandau3DSettings settings)
        : this(host, settings, false) { }

    private GinzburgLandau3DGpuSimulation(Direct3DDeviceHost host, GinzburgLandau3DSettings settings, bool gateHeld)
        : base(host, GinzburgLandau3DComputeShader.CacheEntries, "GinzburgLandau3D", "Гинзбурга–Ландау 3D", 12)
        => Initialize(settings, gateHeld);

    internal static GinzburgLandau3DGpuSimulation CreateLocked(Direct3DDeviceHost host, GinzburgLandau3DSettings settings)
        => new(host, settings, true);

    private protected override int StepsPerSubmit => Math.Clamp(1_000_000 / (Size * Size * Size), 2, 32);

    private protected override void ResetCoreLocked(GinzburgLandau3DSettings settings)
    {
        int n = settings.Size;
        if (_current is null || Settings.Size != n)
        {
            DisposeResourcesCore();
            _current = new(Host.Device, n * n * n, 8); _next = new(Host.Device, n * n * n, 8);
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new(Host.Device, n, 8, Format.R32G32B32A32_Float);
        }
        Host.Context.UpdateSubresource(settings.Field?.Values ?? InitialField(settings), _current.Buffer);
        Settings = settings with { Field = null, Live = null };
        CurrentStep = settings.Field?.Step ?? 0;
        Parameters[0] = (float)settings.C1; Parameters[1] = (float)settings.C3;
        Parameters[2] = (float)(settings.TimeStep / (settings.Spacing * settings.Spacing));
        Parameters[3] = (float)(Math.Exp(settings.TimeStep) - 1);
        Parameters[4] = n; Parameters[5] = (float)settings.Boundary;
        Parameters[6] = (float)Math.Exp(settings.TimeStep);
        ConstantsDirty = true;
    }

    private protected override void StepLocked()
    {
        EvolvePass("React"); EvolvePass("Diffuse"); EvolvePass("React");
    }

    private void EvolvePass(string entry)
    {
        Dispatch(entry, _current!.View, _next!.WriteView, null);
        (_current, _next) = (_next, _current);
    }

    public void Inject(double x, double y, double z, double radius, GinzburgLandau3DBrush mode)
    {
        if (!double.IsFinite(x + y + z + radius) || x is < 0 or > 1 || y is < 0 or > 1 ||
            z is < 0 or > 1 || radius is <= 0 or > .3 || !Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(radius));
        Host.Gate.Wait();
        try
        {
            Parameters[7] = (float)mode;
            Parameters[8] = (float)x; Parameters[9] = (float)y; Parameters[10] = (float)z; Parameters[11] = (float)radius;
            ConstantsDirty = true;
            Dispatch("Inject", null, _current!.WriteView, null);
        }
        finally { Host.Gate.Release(); }
    }

    private protected override void PublishToSlotLocked(int slot)
    {
        GpuFieldSlot target = _slots[slot]!;
        Host.Context.CopyResource(target.Field.Buffer, _current!.Buffer);
        Dispatch("Publish", target.Field.View, null, target.Display.WriteView);
    }

    private protected override GinzburgLandau3DVolume CreateVolume(long version, int slot) => new(this, version, CurrentStep, Size, slot);
    private protected override ID3D11ShaderResourceView DisplayView(int slot) => _slots[slot]!.Display.View;
    private protected override GinzburgLandau3DField ReadSlotLocked(GinzburgLandau3DVolume volume) =>
        new(Size, volume.Step, ReadFloatsLocked(_slots[volume.Slot]!.Field.Buffer), true);
    private protected override GinzburgLandau3DField ReadCurrentLocked() => new(Size, CurrentStep, ReadFloatsLocked(_current!.Buffer), true);

    private void Dispatch(string entry, ID3D11ShaderResourceView? input, ID3D11UnorderedAccessView? output,
        ID3D11UnorderedAccessView? display)
    {
        var context = BindLocked(entry);
        context.CSSetShaderResource(0, input!); context.CSSetUnorderedAccessView(0, output!);
        context.CSSetUnorderedAccessView(1, display!);
        DispatchCube(GinzburgLandau3DComputeShader.GroupX, GinzburgLandau3DComputeShader.GroupY, GinzburgLandau3DComputeShader.GroupZ);
        context.CSSetShaderResource(0, null!); context.CSSetUnorderedAccessView(0, null!);
        context.CSSetUnorderedAccessView(1, null!);
    }

    internal static float[] InitialField(GinzburgLandau3DSettings s)
    {
        int n = s.Size; float[] values = new float[n * n * n * 2]; var random = new Random(s.Seed);
        for (int z = 0; z < n; z++) for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            double px = (x + .5 - n / 2.0) * s.Spacing, py = (y + .5 - n / 2.0) * s.Spacing,
                pz = (z + .5 - n / 2.0) * s.Spacing;
            double phase = 0, amplitude = 1;
            switch (s.SeedShape)
            {
                case GinzburgLandau3DSeed.Ring:
                    double radial = Math.Sqrt(px * px + py * py) - n * s.Spacing * .23;
                    phase = Math.Atan2(pz, radial); amplitude = Math.Tanh(Math.Sqrt(radial * radial + pz * pz) / 2); break;
                case GinzburgLandau3DSeed.Vortex:
                    phase = Math.Atan2(py, px); amplitude = Math.Tanh(Math.Sqrt(px * px + py * py) / 2); break;
                case GinzburgLandau3DSeed.VortexPair:
                    double left = px + n * s.Spacing * .18, right = px - n * s.Spacing * .18;
                    phase = Math.Atan2(py, left) - Math.Atan2(py, right);
                    amplitude = Math.Tanh(Math.Sqrt(left * left + py * py) / 2) * Math.Tanh(Math.Sqrt(right * right + py * py) / 2); break;
                case GinzburgLandau3DSeed.Random:
                    phase = random.NextDouble() * Math.Tau; amplitude = .5 + .5 * random.NextDouble(); break;
                case GinzburgLandau3DSeed.PlaneWave:
                    phase = Math.Tau * (x + .5) / n;
                    amplitude = Math.Sqrt(1 - Math.Pow(Math.Tau / (n * s.Spacing), 2)); break;
            }
            int i = ((z * n + y) * n + x) * 2;
            values[i] = (float)(amplitude * Math.Cos(phase)); values[i + 1] = (float)(amplitude * Math.Sin(phase));
        }
        return values;
    }

    private protected override void DisposeResourcesCore()
    {
        _current?.Dispose(); _next?.Dispose(); _current = _next = null;
        for (int i = 0; i < _slots.Length; i++) { _slots[i]?.Dispose(); _slots[i] = null; }
    }
}
