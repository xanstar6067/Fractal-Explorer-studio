using FractalExplorerWPF.Models;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>GPU publication ticket: a stable trail texture plus exact agent/trail checkpoint.</summary>
public sealed record Physarum3DVolume(Physarum3DGpuSimulation Source, long Version, long Step, int Size, int Slot)
    : IGpuVolumeTicket<Physarum3DGpuSimulation>;

/// <summary>3D sensory agents deposit an atomic integer trail. Diffusion/decay runs in a separate pass.
/// Two publication slots preserve the displayed agents and trail while the simulation advances.</summary>
public sealed class Physarum3DGpuSimulation : GpuVolumeSimulation<Physarum3DSettings, Physarum3DField, Physarum3DVolume>
{
    private readonly Slot?[] _slots = new Slot?[SlotCount];
    private GpuStructuredBuffer? _current, _next, _agents, _deposits;

    public Physarum3DGpuSimulation(Direct3DDeviceHost host, Physarum3DSettings settings) : this(host, settings, false) { }

    private Physarum3DGpuSimulation(Direct3DDeviceHost host, Physarum3DSettings settings, bool gateHeld)
        : base(host, Physarum3DComputeShader.CacheEntries, "Physarum3D", "Physarum 3D", 12) =>
        Initialize(settings, gateHeld);

    /// <summary>Для рендера, который уже держит очередь устройства.</summary>
    internal static Physarum3DGpuSimulation CreateLocked(Direct3DDeviceHost host, Physarum3DSettings settings) =>
        new(host, settings, true);

    private protected override int StepsPerSubmit => 8;

    private protected override void ResetCoreLocked(Physarum3DSettings settings)
    {
        int size = settings.Size;
        if (_current is null || Settings.Size != size || Settings.AgentCount != settings.AgentCount)
        {
            DisposeResourcesCore();
            ID3D11Device device = Host.Device;
            int cells = size * size * size;
            _current = new(device, cells, 4); _next = new(device, cells, 4);
            _agents = new(device, settings.AgentCount, 32); _deposits = new(device, cells, 4);
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new(device, size, settings.AgentCount);
        }
        float[] values = settings.Field?.Values ?? new float[size * size * size];
        float[] agents = settings.Field?.Agents ?? InitialAgents(settings);
        Host.Context.UpdateSubresource(agents, _agents!.Buffer);
        Host.Context.UpdateSubresource(new uint[size * size * size], _deposits!.Buffer);
        Host.Context.UpdateSubresource(values, _current.Buffer);
        Settings = settings with { Field = null };
        CurrentStep = settings.Field?.Step ?? 0;
        Parameters[0] = size; Parameters[1] = settings.AgentCount;
        Parameters[3] = BitConverter.Int32BitsToSingle(settings.Seed);
        Parameters[4] = (float)settings.SensorDistance;
        Parameters[5] = (float)(settings.SensorAngle*Math.PI/180);
        Parameters[6] = (float)(settings.TurnAngle*Math.PI/180); Parameters[7] = (float)settings.Speed;
        Parameters[8] = (float)settings.Deposit; Parameters[9] = (float)settings.Diffusion;
        Parameters[10] = (float)settings.Decay; Parameters[11] = 0;
        ConstantsDirty = true;
    }

    private protected override void StepLocked()
    {
        Parameters[2] = CurrentStep & 0xffffff; ConstantsDirty = true;
        Dispatch("MoveAgents", _current!.View, null, null);
        Dispatch("Diffuse", _current!.View, _next!.WriteView, null);
        (_current, _next) = (_next, _current);
    }

    private protected override void PublishToSlotLocked(int slot)
    {
        Slot target = _slots[slot]!;
        Host.Context.CopyResource(target.Field.Buffer, _current!.Buffer);
        Host.Context.CopyResource(target.Agents.Buffer, _agents!.Buffer);
        Dispatch("Publish", target.Field.View, null, target.Display.WriteView);
    }

    private protected override Physarum3DVolume CreateVolume(long version, int slot) => new(this, version, CurrentStep, Size, slot);

    /// <summary>Текстура следа опубликованного кадра.</summary>
    private protected override ID3D11ShaderResourceView DisplayView(int slot) => _slots[slot]!.Display.View;

    /// <summary>След и агенты опубликованного кадра.</summary>
    private protected override Physarum3DField ReadSlotLocked(Physarum3DVolume volume)
    {
        Slot slot = _slots[volume.Slot]!;
        return new(Size, volume.Step, ReadFloatsLocked(slot.Field.Buffer), ReadFloatsLocked(slot.Agents.Buffer));
    }

    private protected override Physarum3DField ReadCurrentLocked() =>
        new(Size, CurrentStep, ReadFloatsLocked(_current!.Buffer), ReadFloatsLocked(_agents!.Buffer));

    private void Dispatch(string entry, ID3D11ShaderResourceView? input,
        ID3D11UnorderedAccessView? output, ID3D11UnorderedAccessView? display)
    {
        ID3D11DeviceContext context = BindLocked(entry);
        context.CSSetShaderResource(0, input!);
        context.CSSetUnorderedAccessView(0, output!);
        context.CSSetUnorderedAccessView(1, display!);
        context.CSSetUnorderedAccessView(2, entry == "MoveAgents" ? _agents!.WriteView : null!);
        context.CSSetUnorderedAccessView(3, entry != "Publish" ? _deposits!.WriteView : null!);
        if (entry == "MoveAgents") context.Dispatch(((uint)Settings.AgentCount + 127) / 128, 1, 1);
        else DispatchCube(Physarum3DComputeShader.GroupX, Physarum3DComputeShader.GroupY, Physarum3DComputeShader.GroupZ);
        context.CSSetShaderResource(0, null!);
        context.CSSetUnorderedAccessView(0, null!);
        context.CSSetUnorderedAccessView(1, null!);
        context.CSSetUnorderedAccessView(2, null!); context.CSSetUnorderedAccessView(3, null!);
    }

    internal static float[] InitialAgents(Physarum3DSettings s)
    {
        var agents = new float[s.AgentCount*8]; var random = new Random(s.Seed);
        System.Numerics.Vector3 Direction()
        {
            double z=random.NextDouble()*2-1, phi=random.NextDouble()*2*Math.PI;
            double r=Math.Sqrt(1-z*z);
            return new((float)(r*Math.Cos(phi)), (float)z, (float)(r*Math.Sin(phi)));
        }
        for(int i=0;i<s.AgentCount;i++)
        {
            var radial=Direction(); var heading=Direction();
            var position=radial * (float)(.4*Math.Cbrt(random.NextDouble()));
            if(s.SeedShape==Physarum3DSeed.Shell) position=radial*(float)(.36+.025*random.NextDouble());
            if(s.SeedShape==Physarum3DSeed.Torus)
            {
                double phi=random.NextDouble()*2*Math.PI;
                position=new System.Numerics.Vector3((float)(.29*Math.Cos(phi)),0,(float)(.29*Math.Sin(phi)))+radial*.075f;
            }
            if(s.SeedShape==Physarum3DSeed.TwinStars)
                position=radial*(float)(.13*Math.Cbrt(random.NextDouble()))+new System.Numerics.Vector3(i%2==0 ? -.22f : .22f,0,0);
            position=(position+new System.Numerics.Vector3(.5f))*s.Size;
            int at=i*8;
            agents[at]=position.X; agents[at+1]=position.Y; agents[at+2]=position.Z;
            agents[at+4]=heading.X; agents[at+5]=heading.Y; agents[at+6]=heading.Z;
        }
        return agents;
    }

    private protected override void DisposeResourcesCore()
    {
        _current?.Dispose(); _next?.Dispose(); _current = _next = null;
        _agents?.Dispose(); _deposits?.Dispose(); _agents = _deposits = null;
        for (int i = 0; i < _slots.Length; i++) { _slots[i]?.Dispose(); _slots[i] = null; }
    }

    /// <summary>Опубликованный кадр: точные агенты, след и его текстура R32_Float для рендера.</summary>
    private sealed class Slot : IDisposable
    {
        public Slot(ID3D11Device device, int side, int agentCount)
        {
            Field = new(device, side * side * side, 4);
            try
            {
                Agents = new(device, agentCount, 32);
                Display = new(device, side, Format.R32_Float);
            }
            catch { Agents?.Dispose(); Field.Dispose(); throw; }
        }

        public GpuStructuredBuffer Field { get; }
        public GpuStructuredBuffer Agents { get; } = null!;
        public GpuVolumeTexture Display { get; } = null!;

        public void Dispose() { Display.Dispose(); Field.Dispose(); Agents.Dispose(); }
    }
}
