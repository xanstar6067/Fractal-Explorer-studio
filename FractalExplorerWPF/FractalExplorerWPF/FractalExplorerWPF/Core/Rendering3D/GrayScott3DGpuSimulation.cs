using FractalExplorerWPF.Models;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Опубликованный кадр моделирования: объём V для рендера и точная копия U/V для сохранения.</summary>
public sealed record GrayScott3DVolume(GrayScott3DGpuSimulation Source, long Version, long Step, int Size, int Slot)
    : IGpuVolumeTicket<GrayScott3DGpuSimulation>;

/// <summary>
/// Gray–Scott в трёхмерной сетке целиком на ГП: явный Эйлер, dt = 1, шеститочечный лапласиан,
/// периодические границы. Публикация копирует V в текстуру рендера, U/V читаются только для
/// сохранения и проверок; общий цикл — <see cref="GpuVolumeSimulation{TSettings, TField, TVolume}"/>.
/// </summary>
public sealed class GrayScott3DGpuSimulation : GpuVolumeSimulation<GrayScott3DSettings, GrayScott3DField, GrayScott3DVolume>
{
    private readonly GpuFieldSlot?[] _slots = new GpuFieldSlot?[SlotCount];
    private GpuStructuredBuffer? _current, _next;

    public GrayScott3DGpuSimulation(Direct3DDeviceHost host, GrayScott3DSettings settings) : this(host, settings, false) { }

    private GrayScott3DGpuSimulation(Direct3DDeviceHost host, GrayScott3DSettings settings, bool gateHeld)
        : base(host, GrayScott3DComputeShader.CacheEntries, "GrayScott3D", "Gray–Scott 3D", 12) =>
        Initialize(settings, gateHeld);

    /// <summary>Для рендера, который уже держит очередь устройства.</summary>
    internal static GrayScott3DGpuSimulation CreateLocked(Direct3DDeviceHost host, GrayScott3DSettings settings) =>
        new(host, settings, true);

    private protected override bool ViewRequiresCurrentFrame => false;

    /// <summary>
    /// Порция подачи: на мелкой сетке шагов больше, чтобы ожидание между порциями не
    /// становилось дольше самой работы; на 64³ и 128³ — 16 шагов.
    /// </summary>
    private protected override int StepsPerSubmit
    {
        get { int n = Size; return Math.Clamp(4_000_000 / (n * n * n), 16, 128); }
    }

    private protected override void ResetCoreLocked(GrayScott3DSettings settings)
    {
        int size = settings.Size;
        if (_current is null || Settings.Size != size)
        {
            DisposeResourcesCore();
            ID3D11Device device = Host.Device;
            _current = new(device, size * size * size, 8); _next = new(device, size * size * size, 8);
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new(device, size, 8, Format.R32_Float);
        }
        float[] values = settings.Field?.Values ?? InitialField(settings);
        Host.Context.UpdateSubresource(values, _current.Buffer);
        Settings = settings with { Field = null };
        CurrentStep = settings.Field?.Step ?? 0;
        Parameters[0] = (float)settings.DiffusionU; Parameters[1] = (float)settings.DiffusionV;
        Parameters[2] = (float)settings.Feed; Parameters[3] = (float)settings.Kill; Parameters[4] = size;
        ConstantsDirty = true;
    }

    private protected override void StepLocked()
    {
        Dispatch("Evolve", _current!.View, _next!.WriteView, null);
        (_current, _next) = (_next, _current);
    }

    public void Inject(double x, double y, double z, double radius)
    {
        ValidateBrush(x, y, z, radius);
        Host.Gate.Wait();
        try
        {
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

    private protected override GrayScott3DVolume CreateVolume(long version, int slot) => new(this, version, CurrentStep, Size, slot);

    private protected override ID3D11ShaderResourceView DisplayView(int slot) => _slots[slot]!.Display.View;

    private protected override GrayScott3DField ReadSlotLocked(GrayScott3DVolume volume) =>
        new(Size, volume.Step, ReadFloatsLocked(_slots[volume.Slot]!.Field.Buffer), true);

    private protected override GrayScott3DField ReadCurrentLocked() =>
        new(Size, CurrentStep, ReadFloatsLocked(_current!.Buffer), true);

    private void Dispatch(string entry, ID3D11ShaderResourceView? input,
        ID3D11UnorderedAccessView? output, ID3D11UnorderedAccessView? display)
    {
        ID3D11DeviceContext context = BindLocked(entry);
        context.CSSetShaderResource(0, input!);
        context.CSSetUnorderedAccessView(0, output!);
        context.CSSetUnorderedAccessView(1, display!);
        DispatchCube(GrayScott3DComputeShader.GroupX, GrayScott3DComputeShader.GroupY, GrayScott3DComputeShader.GroupZ);
        context.CSSetShaderResource(0, null!);
        context.CSSetUnorderedAccessView(0, null!);
        context.CSSetUnorderedAccessView(1, null!);
    }

    internal static float[] InitialField(GrayScott3DSettings s)
    {
        int n = s.Size; var field = new float[n * n * n * 2]; var random = new Random(s.Seed);
        for (int z = 0; z < n; z++) for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            double px = (x + .5) / n * 2 - 1, py = (y + .5) / n * 2 - 1, pz = (z + .5) / n * 2 - 1;
            bool active = s.SeedShape switch
            {
                GrayScott3DSeed.Noise => Math.Abs(px) < .72 && Math.Abs(py) < .72 && Math.Abs(pz) < .72,
                GrayScott3DSeed.Ring => Math.Pow(Math.Sqrt(px * px + pz * pz) - .4, 2) + py * py < .0144,
                GrayScott3DSeed.Spheres => Math.Pow(Math.Abs(px) - .25, 2) + Math.Pow(Math.Abs(py) - .25, 2) + Math.Pow(Math.Abs(pz) - .25, 2) < .0225,
                _ => false
            };
            int i = ((z * n + y) * n + x) * 2;
            field[i] = active ? .5f : 1f;
            field[i + 1] = active ? (float)(.25 + (random.NextDouble() - .5) * .12) : 0;
        }
        return field;
    }

    internal static void ValidateBrush(double x, double y, double z, double radius)
    {
        if (!double.IsFinite(x + y + z + radius) || x is < 0 or > 1 || y is < 0 or > 1 || z is < 0 or > 1 || radius is <= 0 or > .3)
            throw new ArgumentOutOfRangeException(nameof(radius));
    }

    private protected override void DisposeResourcesCore()
    {
        _current?.Dispose(); _next?.Dispose(); _current = _next = null;
        for (int i = 0; i < _slots.Length; i++) { _slots[i]?.Dispose(); _slots[i] = null; }
    }
}
