using FractalExplorerWPF.Models;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Опубликованный кадр: возбуждение, круговые координаты фазы и вихревые нити для рендера,
/// точная копия знаковых U/V для сохранения.
/// </summary>
public sealed record Excitable3DVolume(Excitable3DGpuSimulation Source, long Version, long Step, int Size, int Slot)
    : IGpuVolumeTicket<Excitable3DGpuSimulation>;

/// <summary>Two-field excitable medium on the shared D3D11 device. Whole steps, two immutable
/// publication slots, bounded submissions and lossless checkpoints follow the volume pipeline.
/// Barkley/FHN/AP use explicit Euler; Oregonator treats destruction semi-implicitly to preserve
/// positive concentrations at its stiff small-q rest state. No-flux or periodic six-neighbour stencil.</summary>
public sealed class Excitable3DGpuSimulation : GpuVolumeSimulation<Excitable3DSettings, Excitable3DField, Excitable3DVolume>
{
    private readonly GpuFieldSlot?[] _slots = new GpuFieldSlot?[SlotCount];
    private GpuStructuredBuffer? _current, _next;

    public Excitable3DGpuSimulation(Direct3DDeviceHost host, Excitable3DSettings settings) : this(host, settings, false) { }

    private Excitable3DGpuSimulation(Direct3DDeviceHost host, Excitable3DSettings settings, bool gateHeld)
        : base(host, Excitable3DComputeShader.CacheEntries, "Excitable3D", "свитковых волн 3D", 24) =>
        Initialize(settings, gateHeld);

    /// <summary>Для рендера, который уже держит очередь устройства.</summary>
    internal static Excitable3DGpuSimulation CreateLocked(Direct3DDeviceHost host, Excitable3DSettings settings) =>
        new(host, settings, true);

    private protected override int StepsPerSubmit
    {
        get { int n = Size; return Math.Clamp(4_000_000 / (n * n * n), 16, 128); }
    }

    private protected override void ResetCoreLocked(Excitable3DSettings settings)
    {
        int size = settings.Size;
        if (_current is null || Settings.Size != size)
        {
            DisposeResourcesCore();
            ID3D11Device device = Host.Device;
            _current = new(device, size * size * size, 8); _next = new(device, size * size * size, 8);
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new(device, size, 8, Format.R32G32B32A32_Float);
        }
        float[] values = settings.Field?.Values ?? InitialField(settings);
        Host.Context.UpdateSubresource(values, _current.Buffer);
        Settings = settings with { Field = null };
        CurrentStep = settings.Field?.Step ?? 0;
        Parameters[0] = (float)settings.DiffusionU; Parameters[1] = (float)settings.DiffusionV;
        Parameters[2] = (float)settings.A; Parameters[3] = (float)settings.B;
        Parameters[4] = size; Parameters[5] = (float)settings.TimeStep;
        Parameters[6] = (float)(1 / (settings.Spacing * settings.Spacing)); Parameters[7] = (float)settings.Model;
        Parameters[12] = (float)settings.Epsilon; Parameters[13] = (float)settings.Mu1;
        Parameters[14] = (float)settings.Mu2; Parameters[15] = (float)settings.Boundary;
        var phase = PhaseCenter(settings);
        Parameters[16] = phase.U; Parameters[17] = phase.V;
        Parameters[18] = settings.Model == Excitable3DModel.FitzHughNagumo ? .25f : 1f;
        Parameters[19] = settings.Model == Excitable3DModel.FitzHughNagumo ? .5f : 0;
        Parameters[20] = settings.Model == Excitable3DModel.FitzHughNagumo ? 2 : 1;
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

    private protected override Excitable3DVolume CreateVolume(long version, int slot) => new(this, version, CurrentStep, Size, slot);

    /// <summary>Текстура возбуждения, фазы и нитей.</summary>
    private protected override ID3D11ShaderResourceView DisplayView(int slot) => _slots[slot]!.Display.View;

    private protected override Excitable3DField ReadSlotLocked(Excitable3DVolume volume) =>
        new(Size, volume.Step, ReadFloatsLocked(_slots[volume.Slot]!.Field.Buffer), true);

    private protected override Excitable3DField ReadCurrentLocked() =>
        new(Size, CurrentStep, ReadFloatsLocked(_current!.Buffer), true);

    private void Dispatch(string entry, ID3D11ShaderResourceView? input,
        ID3D11UnorderedAccessView? output, ID3D11UnorderedAccessView? display)
    {
        ID3D11DeviceContext context = BindLocked(entry);
        context.CSSetShaderResource(0, input!);
        context.CSSetUnorderedAccessView(0, output!);
        context.CSSetUnorderedAccessView(1, display!);
        DispatchCube(Excitable3DComputeShader.GroupX, Excitable3DComputeShader.GroupY, Excitable3DComputeShader.GroupZ);
        context.CSSetShaderResource(0, null!);
        context.CSSetUnorderedAccessView(0, null!);
        context.CSSetUnorderedAccessView(1, null!);
    }

    internal static (float U, float V) PhaseCenter(Excitable3DSettings s) => s.Model switch
    {
        Excitable3DModel.Oregonator => (.1f, .1f),
        Excitable3DModel.FitzHughNagumo => (0, 0),
        Excitable3DModel.AlievPanfilov => (.4f, 1f),
        _ => (.5f, .35f)
    };

    internal static (double U, double V) Rest(Excitable3DSettings s)
    {
        if (s.Model == Excitable3DModel.Oregonator)
        {
            double c = 1 - s.B - s.A;
            double u = 2 * s.B * (1 + s.A) / (Math.Sqrt(c*c + 4*s.B*(1+s.A)) - c);
            return (u,u);
        }
        if (s.Model == Excitable3DModel.FitzHughNagumo)
        {
            double lo = -3, hi = 3;
            for (int i=0; i<80; i++)
            {
                double u=(lo+hi)/2;
                if (u-u*u*u/3-(u+s.A)/s.B > 0) lo=u; else hi=u;
            }
            double rest=(lo+hi)/2; return (rest,(rest+s.A)/s.B);
        }
        return (0,0);
    }

    internal static float[] InitialField(Excitable3DSettings s)
    {
        int n=s.Size; var field=new float[n*n*n*2]; var rest=Rest(s);
        double highU=s.Model == Excitable3DModel.FitzHughNagumo ? 2 : 1;
        double highV=s.Model switch { Excitable3DModel.Oregonator => .3, Excitable3DModel.FitzHughNagumo => 1, Excitable3DModel.AlievPanfilov => 2, _ => .7 };
        for(int z=0;z<n;z++) for(int y=0;y<n;y++) for(int x=0;x<n;x++)
        {
            double px=(x+.5)/n*2-1, py=(y+.5)/n*2-1, pz=(z+.5)/n*2-1;
            bool excited=false, refractory=false;
            switch(s.SeedShape)
            {
                case Excitable3DSeed.Scroll: excited=px<0; refractory=py<0; break;
                case Excitable3DSeed.Ring: excited=pz<0; refractory=Math.Sqrt(px*px+py*py)<.45; break;
                case Excitable3DSeed.CrossedScrolls:
                    excited=pz<0 ? px<0 : py<0; refractory=pz<0 ? py<0 : px>0; break;
                case Excitable3DSeed.Sphere: excited=px*px+py*py+pz*pz<.06; break;
            }
            int i=((z*n+y)*n+x)*2;
            field[i]=(float)(excited ? highU : rest.U);
            field[i+1]=(float)(refractory ? highV : rest.V);
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
