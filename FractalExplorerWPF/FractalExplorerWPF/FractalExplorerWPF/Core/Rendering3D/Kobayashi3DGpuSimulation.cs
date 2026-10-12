using FractalExplorerWPF.Models;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Опубликованный кадр моделирования: объём фазы и температуры для рендера и точная копия фазы и температуры для сохранения.</summary>
public sealed record Kobayashi3DVolume(Kobayashi3DGpuSimulation Source, long Version, long Step, int Size, int Slot)
    : IGpuVolumeTicket<Kobayashi3DGpuSimulation>;

/// <summary>
/// Кобаяси в трёхмерной сетке целиком на ГП: явный Эйлер с ограничением шага, потоки анизотропной энергии,
/// непроницаемые границы. Публикация копирует фазу и температуру в текстуру рендера; φ/T читаются только
/// для сохранения, подбора форм и проверок.
/// </summary>
public sealed class Kobayashi3DGpuSimulation : GpuVolumeSimulation<Kobayashi3DSettings, Kobayashi3DField, Kobayashi3DVolume>
{
    private readonly GpuFieldSlot?[] _slots = new GpuFieldSlot?[SlotCount];
    private GpuStructuredBuffer? _current, _next, _flux;

    public Kobayashi3DGpuSimulation(Direct3DDeviceHost host, Kobayashi3DSettings settings) : this(host, settings, false) { }

    private Kobayashi3DGpuSimulation(Direct3DDeviceHost host, Kobayashi3DSettings settings, bool gateHeld)
        : base(host, Kobayashi3DComputeShader.CacheEntries, "Kobayashi3D", "Кобаяси 3D", 16) =>
        Initialize(settings, gateHeld);

    /// <summary>Для рендера, который уже держит очередь устройства.</summary>
    internal static Kobayashi3DGpuSimulation CreateLocked(Direct3DDeviceHost host, Kobayashi3DSettings settings) =>
        new(host, settings, true);

    /// <summary>
    /// Порция подачи: на мелкой сетке шагов больше, чтобы ожидание между порциями не
    /// становилось дольше самой работы; на 64³ и 128³ — 16 шагов.
    /// </summary>
    private protected override int StepsPerSubmit
    {
        get { int n = Size; return Math.Clamp(4_000_000 / (n * n * n), 16, 128); }
    }

    private protected override void ResetCoreLocked(Kobayashi3DSettings settings)
    {
        int size = settings.Size;
        if (_current is null || Settings.Size != size)
        {
            DisposeResourcesCore();
            ID3D11Device device = Host.Device;
            int cells = size * size * size;
            _current = new(device, cells, 8); _next = new(device, cells, 8);
            _flux = new(device, cells, 16);
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new(device, size, 8, Format.R32G32_Float);
        }
        float[] values = settings.Field?.Values ?? InitialField(settings);
        Host.Context.UpdateSubresource(values, _current.Buffer);
        Settings = settings with { Field = null };
        CurrentStep = settings.Field?.Step ?? 0;
        Parameters[0] = (float)settings.InterfaceWidth; Parameters[1] = (float)settings.Anisotropy;
        Parameters[2] = (float)settings.Mobility; Parameters[3] = (float)settings.ThermalDiffusion;
        Parameters[4] = (float)settings.LatentHeat; Parameters[5] = (float)settings.EffectiveTimeStep;
        Parameters[6] = (float)settings.Noise; Parameters[7] = BitConverter.Int32BitsToSingle(settings.Seed);
        Parameters[8] = size;
        ConstantsDirty = true;
    }

    private protected override void StepLocked()
    {
        Parameters[9] = BitConverter.Int32BitsToSingle(unchecked((int)CurrentStep));
        Parameters[10] = BitConverter.Int32BitsToSingle(unchecked((int)(CurrentStep >> 32)));
        ConstantsDirty = true;
        Dispatch("Flux", _current!.View, _flux!.WriteView, null);
        Dispatch("Evolve", _current!.View, _next!.WriteView, null);
        (_current, _next) = (_next, _current);
    }

    public void Inject(double x, double y, double z, double radius)
    {
        ValidateBrush(x, y, z, radius);
        Host.Gate.Wait();
        try
        {
            Parameters[12] = (float)x; Parameters[13] = (float)y; Parameters[14] = (float)z; Parameters[15] = (float)radius;
            ConstantsDirty = true;
            Dispatch("Inject", _current!.View, _next!.WriteView, null);
            (_current, _next) = (_next, _current);
        }
        finally { Host.Gate.Release(); }
    }

    private protected override void PublishToSlotLocked(int slot)
    {
        GpuFieldSlot target = _slots[slot]!;
        Host.Context.CopyResource(target.Field.Buffer, _current!.Buffer);
        Dispatch("Publish", target.Field.View, null, target.Display.WriteView);
    }

    private protected override Kobayashi3DVolume CreateVolume(long version, int slot) => new(this, version, CurrentStep, Size, slot);

    /// <summary>Текстура фазы и температуры опубликованного кадра.</summary>
    private protected override ID3D11ShaderResourceView DisplayView(int slot) => _slots[slot]!.Display.View;

    private protected override Kobayashi3DField ReadSlotLocked(Kobayashi3DVolume volume) =>
        new(Size, volume.Step, ReadFloatsLocked(_slots[volume.Slot]!.Field.Buffer), true);

    private protected override Kobayashi3DField ReadCurrentLocked() =>
        new(Size, CurrentStep, ReadFloatsLocked(_current!.Buffer), true);

    private void Dispatch(string entry, ID3D11ShaderResourceView? input,
        ID3D11UnorderedAccessView? output, ID3D11UnorderedAccessView? display)
    {
        ID3D11DeviceContext context = BindLocked(entry);
        context.CSSetShaderResource(0, input!);
        context.CSSetShaderResource(1, entry == "Evolve" ? _flux!.View : null!);
        context.CSSetUnorderedAccessView(0, output!);
        context.CSSetUnorderedAccessView(1, display!);
        DispatchCube(Kobayashi3DComputeShader.GroupX, Kobayashi3DComputeShader.GroupY, Kobayashi3DComputeShader.GroupZ);
        context.CSSetShaderResource(0, null!);
        context.CSSetShaderResource(1, null!);
        context.CSSetUnorderedAccessView(0, null!);
        context.CSSetUnorderedAccessView(1, null!);
    }

    internal static float[] InitialField(Kobayashi3DSettings s)
    {
        int n = s.Size; var field = new float[n * n * n * 2];
        var nuclei = s.SeedShape == Kobayashi3DSeed.RandomSpheres ? RandomNuclei(s) : [];
        for (int z = 0; z < n; z++) for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            double px = x + .5 - n / 2.0, py = y + .5 - n / 2.0, pz = z + .5 - n / 2.0;
            double distance = s.SeedShape switch
            {
                Kobayashi3DSeed.EightSpheres => Math.Sqrt(Math.Pow(Math.Abs(px) - n * s.SeedSpread, 2) +
                    Math.Pow(Math.Abs(py) - n * s.SeedSpread, 2) + Math.Pow(Math.Abs(pz) - n * s.SeedSpread, 2)) - n * s.SeedRadius,
                Kobayashi3DSeed.Ring => Math.Sqrt(Math.Pow(Math.Sqrt(px * px + pz * pz) - n * s.SeedSpread, 2) + py * py) - n * s.SeedRadius,
                Kobayashi3DSeed.RandomSpheres => double.PositiveInfinity,
                _ => Math.Sqrt(px * px + py * py + pz * pz) - n * s.SeedRadius
            };
            foreach (var c in nuclei)
                distance = Math.Min(distance, Math.Sqrt(Math.Pow(px - n * c.X, 2) +
                    Math.Pow(py - n * c.Y, 2) + Math.Pow(pz - n * c.Z, 2)) - n * c.Radius);
            int i = ((z * n + y) * n + x) * 2;
            field[i] = s.SeedShape == Kobayashi3DSeed.Empty ? 0 :
                (float)(.5 * (1 - Math.Tanh(distance / (Math.Sqrt(2) * s.InterfaceWidth))));
            field[i + 1] = (float)-s.Undercooling;
        }
        return field;
    }

    // Fixed seed and normalized coordinates reproduce the same layout on any grid.
    // Best-of-24 placement spreads nuclei without an unbounded rejection loop.
    private static (double X, double Y, double Z, double Radius)[] RandomNuclei(Kobayashi3DSettings s)
    {
        var random = new Random(s.Seed);
        var result = new (double X, double Y, double Z, double Radius)[s.SeedCount];
        double Coordinate() => (random.NextDouble() * 2 - 1) * s.SeedSpread;
        for (int i = 0; i < result.Length; i++)
        {
            double best = -1;
            for (int trial = 0; trial < 24; trial++)
            {
                var c = (X: Coordinate(), Y: Coordinate(), Z: Coordinate(), Radius: s.SeedRadius * (.85 + .3 * random.NextDouble()));
                double separation = double.PositiveInfinity;
                for (int j = 0; j < i; j++)
                    separation = Math.Min(separation, Math.Pow(c.X - result[j].X, 2) +
                        Math.Pow(c.Y - result[j].Y, 2) + Math.Pow(c.Z - result[j].Z, 2));
                if (separation > best) { best = separation; result[i] = c; }
            }
        }
        return result;
    }

    internal static void ValidateBrush(double x, double y, double z, double radius)
    {
        if (!double.IsFinite(x + y + z + radius) || x is < 0 or > 1 || y is < 0 or > 1 || z is < 0 or > 1 || radius is <= 0 or > .3)
            throw new ArgumentOutOfRangeException(nameof(radius));
    }

    private protected override void DisposeResourcesCore()
    {
        _current?.Dispose(); _next?.Dispose(); _flux?.Dispose(); _current = _next = _flux = null;
        for (int i = 0; i < _slots.Length; i++) { _slots[i]?.Dispose(); _slots[i] = null; }
    }
}
