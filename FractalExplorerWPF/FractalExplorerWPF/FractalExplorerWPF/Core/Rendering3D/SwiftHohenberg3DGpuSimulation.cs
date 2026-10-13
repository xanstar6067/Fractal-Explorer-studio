using FractalExplorerWPF.Models;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed record SwiftHohenberg3DVolume(SwiftHohenberg3DGpuSimulation Source, long Version, long Step, int Size, int Slot)
    : IGpuVolumeTicket<SwiftHohenberg3DGpuSimulation>;

/// <summary>Periodic Fourier-Galerkin simulation with alias-free cubic projection and ETD1 steps.
/// Uses the shared two-channel FFT, device queue, publication slots and checkpoint infrastructure.</summary>
public sealed class SwiftHohenberg3DGpuSimulation
    : GpuSpectralScalarSimulation<SwiftHohenberg3DSettings, SwiftHohenberg3DField, SwiftHohenberg3DVolume>
{
    public SwiftHohenberg3DGpuSimulation(Direct3DDeviceHost host, SwiftHohenberg3DSettings settings) : this(host, settings, false) { }

    internal SwiftHohenberg3DGpuSimulation(Direct3DDeviceHost host, SwiftHohenberg3DSettings settings, bool gateHeld, bool? tiledFft = null)
        : base(host, SwiftHohenberg3DComputeShader.CacheEntries, "SwiftHohenberg3D", "Свифта–Хоэнберга 3D", 12, Format.R32_Float, tiledFft)
    {
        Initialize(settings, gateHeld);
    }

    internal static SwiftHohenberg3DGpuSimulation CreateLocked(Direct3DDeviceHost host, SwiftHohenberg3DSettings settings)
        => new(host, settings, true);
    private protected override void ResetCoreLocked(SwiftHohenberg3DSettings settings)
    {
        int n = settings.Size;
        Settings = settings with { Field = null, Live = null };
        Parameters[0] = (float)settings.R; Parameters[1] = (float)settings.Quadratic;
        Parameters[2] = (float)(settings.WaveNumber * settings.WaveNumber); Parameters[3] = (float)settings.TimeStep;
        Parameters[4] = n; Parameters[5] = (float)(Math.Tau / (n * settings.Spacing)); ConstantsDirty = true;
        // Fresh seeds and brush edits enter the Galerkin band once. Disk checkpoints are not modified.
        UploadScalarFieldLocked(settings.Field?.Values ?? InitialField(settings), settings.Field?.Step ?? 0, settings.Field is null);
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
            DispatchScalarLocked("Inject", output: FieldBuffer.WriteView); ProjectLocked();
        }
        finally { Host.Gate.Release(); }
    }

    private protected override SwiftHohenberg3DVolume CreateVolume(long version, int slot) => new(this, version, CurrentStep, Size, slot);
    private protected override SwiftHohenberg3DField ReadSlotLocked(SwiftHohenberg3DVolume volume) =>
        new(Size, volume.Step, ReadScalarSlotLocked(volume), true);
    private protected override SwiftHohenberg3DField ReadCurrentLocked() => new(Size, CurrentStep, ReadScalarCurrentLocked(), true);

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

}
