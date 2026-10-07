using System.Runtime.InteropServices;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Vortice.D3DCompiler;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>GPU publication ticket: a stable trail texture plus exact agent/trail checkpoint.</summary>
public sealed record Physarum3DVolume(Physarum3DGpuSimulation Source, long Version, long Step, int Size, int Slot);

/// <summary>3D sensory agents deposit an atomic integer trail. Diffusion/decay runs in a separate pass.
/// Two publication slots preserve the displayed agents and trail while the simulation advances.</summary>
public sealed class Physarum3DGpuSimulation : IDisposable
{
    private const double FenceTimeoutSeconds = 15;

    private readonly Direct3DDeviceHost _host;
    private readonly Dictionary<string, ID3D11ComputeShader> _shaders = [];
    private readonly ID3D11Query[] _fences = new ID3D11Query[2];
    private readonly bool[] _fencePending = new bool[2];
    private readonly Slot?[] _slots = new Slot?[2];
    private readonly long[] _slotVersions = new long[2];
    private readonly float[] _parameters = new float[12];
    private ID3D11Buffer _constants = null!;
    private ID3D11Buffer? _staging;
    private FieldBuffer? _current, _next;
    private FieldBuffer? _agents, _deposits;
    private int _fenceIndex, _lastSlot = 1;
    private bool _constantsDirty = true;
    private long _step, _version;
    private bool _disposed;

    public Physarum3DGpuSimulation(Direct3DDeviceHost host, Physarum3DSettings settings) : this(host, settings, false) { }

    private Physarum3DGpuSimulation(Direct3DDeviceHost host, Physarum3DSettings settings, bool gateHeld)
    {
        settings.Validate();
        _host = host.AddRef();
        bool entered = false, created = false;
        try
        {
            if (!gateHeld) { _host.Gate.Wait(); entered = true; }
            ID3D11Device device = _host.Device;
            foreach (var entry in Physarum3DComputeShader.CacheEntries)
                _shaders[entry.EntryPoint] = device.CreateComputeShader(ShaderBytecodeCache.GetOrCompile(entry,
                    e => Compiler.Compile(e.Source, e.EntryPoint, "Physarum3D", e.Profile)).Span);
            _constants = device.CreateBuffer(new BufferDescription { ByteWidth = 48, Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer, CPUAccessFlags = CpuAccessFlags.Write });
            for (int i = 0; i < _fences.Length; i++)
                _fences[i] = device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));
            ResetLocked(settings);
            created = true;
        }
        finally
        {
            if (!created) DisposeResources();
            if (entered) _host.Gate.Release();
            // The last reference destroys the device together with its gate: only after the gate is free.
            if (!created) _host.Release();
        }
    }

    /// <summary>Для рендера, который уже держит очередь устройства.</summary>
    internal static Physarum3DGpuSimulation CreateLocked(Direct3DDeviceHost host, Physarum3DSettings settings) =>
        new(host, settings, true);

    internal bool Uses(Direct3DDeviceHost host) => ReferenceEquals(_host, host);

    public Physarum3DSettings Settings { get; private set; } = new();
    public string DeviceName => "ГП · " + _host.AdapterName;
    public int Size => Settings.Size;
    public long Step => Volatile.Read(ref _step);

    /// <summary>Новое уравнение и поле (сохранённое или стартовая затравка); ресурсы переживают смену при той же сетке.</summary>
    public void Reset(Physarum3DSettings settings)
    {
        settings.Validate();
        _host.Gate.Wait();
        try { ResetLocked(settings); }
        finally { _host.Gate.Release(); }
    }

    internal void ResetLocked(Physarum3DSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        settings.Validate();
        int size = settings.Size;
        if (_current is null || Settings.Size != size || Settings.AgentCount != settings.AgentCount)
        {
            DisposeFields();
            ID3D11Device device = _host.Device;
            _current = new(device, size * size * size); _next = new(device, size * size * size);
            _agents = new(device, settings.AgentCount, 32); _deposits = new(device, size * size * size);
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new(device, size, settings.AgentCount);
        }
        float[] values = settings.Field?.Values ?? new float[size * size * size];
        float[] agents = settings.Field?.Agents ?? InitialAgents(settings);
        _host.Context.UpdateSubresource(agents, _agents!.Buffer);
        _host.Context.UpdateSubresource(new uint[size * size * size], _deposits!.Buffer);
        _host.Context.UpdateSubresource(values, _current.Buffer);
        Settings = settings with { Field = null };
        _step = settings.Field?.Step ?? 0;
        Array.Clear(_slotVersions);
        _parameters[0] = size; _parameters[1] = settings.AgentCount;
        _parameters[3] = BitConverter.Int32BitsToSingle(settings.Seed);
        _parameters[4] = (float)settings.SensorDistance;
        _parameters[5] = (float)(settings.SensorAngle*Math.PI/180);
        _parameters[6] = (float)(settings.TurnAngle*Math.PI/180); _parameters[7] = (float)settings.Speed;
        _parameters[8] = (float)settings.Deposit; _parameters[9] = (float)settings.Diffusion;
        _parameters[10] = (float)settings.Decay; _parameters[11] = 0;
        _constantsDirty = true;
    }

    /// <summary>
    /// Ставит шаги в очередь ГП порциями и не держит устройство между ними: полосы кадра идут
    /// вперемежку. Ждёт не конца работы, а предпоследней порции — видеокарта не простаивает,
    /// пока поток спит. Отмена прекращает подачу новых порций; уже поданные шаги — целые шаги,
    /// поэтому поле остаётся согласованным. Возвращает число поданных шагов.
    /// </summary>
    public int Advance(int steps, CancellationToken token, IProgress<int>? progress = null)
    {
        if (steps < 0) throw new ArgumentOutOfRangeException(nameof(steps));
        int done = 0;
        while (done < steps && !token.IsCancellationRequested)
        {
            int count = Math.Min(StepsPerSubmit, steps - done);
            if (!TryEnter(token)) break;
            try { SubmitStepsLocked(count); }
            finally { _host.Gate.Release(); }
            done += count; progress?.Report(done);
            WaitForFence(_fenceIndex ^ 1, token, holdingGate: false);
        }
        return done;
    }

    /// <summary>Подготовка под взятой очередью: отмена бросает исключение, поле остаётся целым.</summary>
    internal void AdvanceLocked(int steps, CancellationToken token)
    {
        for (int done = 0; done < steps;)
        {
            token.ThrowIfCancellationRequested();
            int count = Math.Min(StepsPerSubmit, steps - done);
            SubmitStepsLocked(count);
            done += count;
            WaitForFence(_fenceIndex ^ 1, token, holdingGate: true);
        }
        token.ThrowIfCancellationRequested();
    }

    /// <summary>Публикует текущее поле для рендера и сохранения, не трогая слот кадра <paramref name="keep"/>.</summary>
    public Physarum3DVolume Publish(Physarum3DVolume? keep = null)
    {
        _host.Gate.Wait();
        try { return PublishLocked(keep); }
        finally { _host.Gate.Release(); }
    }

    internal Physarum3DVolume PublishLocked(Physarum3DVolume? keep = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int index = IsCurrent(keep) ? keep!.Slot ^ 1 : _lastSlot ^ 1;
        Slot slot = _slots[index]!;
        _host.Context.CopyResource(slot.Field.Buffer, _current!.Buffer);
        _host.Context.CopyResource(slot.Agents.Buffer, _agents!.Buffer);
        Dispatch("Publish", slot.Field.View, null, slot.DisplayWrite);
        _version++;
        _slotVersions[index] = _version;
        _lastSlot = index;
        return new(this, _version, _step, Settings.Size, index);
    }

    private bool IsCurrent(Physarum3DVolume? volume) =>
        volume is not null && ReferenceEquals(volume.Source, this) && volume.Size == Settings.Size &&
        _slotVersions[volume.Slot] == volume.Version;

    /// <summary>Текстура следа опубликованного кадра; вызывается рендером под очередью устройства.</summary>
    internal ID3D11ShaderResourceView ViewLocked(Physarum3DVolume volume)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(volume.Source, this)) throw new ArgumentException("Кадр другого моделирования.");
        if (!IsCurrent(volume)) throw new InvalidOperationException("Кадр Physarum 3D уже заменён.");
        return _slots[volume.Slot]!.DisplayView;
    }

    /// <summary>След и агенты опубликованного кадра — контрольная точка для сохранения.</summary>
    public Physarum3DField ReadCheckpoint(Physarum3DVolume volume)
    {
        if (!ReferenceEquals(volume.Source, this)) throw new ArgumentException("Кадр другого моделирования.");
        _host.Gate.Wait();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!IsCurrent(volume)) throw new InvalidOperationException("Кадр Physarum 3D уже заменён более новым.");
            var slot = _slots[volume.Slot]!;
            return ReadLocked(slot.Field.Buffer, slot.Agents.Buffer, volume.Step);
        }
        finally { _host.Gate.Release(); }
    }

    /// <summary>Текущее поле, включая ещё не опубликованные шаги.</summary>
    public Physarum3DField ReadCurrent()
    {
        _host.Gate.Wait();
        try { ObjectDisposedException.ThrowIf(_disposed, this); return ReadLocked(_current!.Buffer, _agents!.Buffer, _step); }
        finally { _host.Gate.Release(); }
    }

    private Physarum3DField ReadLocked(ID3D11Buffer trail, ID3D11Buffer agents, long step) =>
        new(Settings.Size, step, ReadBuffer(trail), ReadBuffer(agents));

    private float[] ReadBuffer(ID3D11Buffer source)
    {
        uint bytes = source.Description.ByteWidth;
        if (_staging is null || _staging.Description.ByteWidth != bytes)
        {
            _staging?.Dispose();
            _staging = _host.Device.CreateBuffer(new BufferDescription
                { ByteWidth = bytes, Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read });
        }
        _host.Context.CopyResource(_staging, source);
        var mapped = _host.Context.Map(_staging, MapMode.Read);
        float[] values = new float[bytes / 4];
        try { Marshal.Copy(mapped.DataPointer, values, 0, values.Length); }
        finally { _host.Context.Unmap(_staging, 0); }
        return values;
    }

    /// <summary>
    /// Порция подачи: на мелкой сетке шагов больше, чтобы ожидание между порциями не
    /// становилось дольше самой работы; на 64³ и 128³ — 16 шагов.
    /// </summary>
    private int StepsPerSubmit => 8;

    private void SubmitStepsLocked(int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (int i = 0; i < count; i++)
        {
            _parameters[2] = _step & 0xffffff; _constantsDirty = true;
            Dispatch("MoveAgents", _current!.View, null, null);
            Dispatch("Diffuse", _current!.View, _next!.WriteView, null);
            (_current, _next) = (_next, _current);
            _step++;
        }
        _fenceIndex ^= 1;
        _host.Context.End(_fences[_fenceIndex]);
        _fencePending[_fenceIndex] = true;
        _host.Context.Flush();
    }

    private void WaitForFence(int index, CancellationToken token, bool holdingGate)
    {
        if (!_fencePending[index]) return;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var spinner = new SpinWait();
        while (!token.IsCancellationRequested)
        {
            if (!holdingGate && !TryEnter(token)) return;
            try
            {
                if (_disposed) return;
                var result = _host.Context.GetData(_fences[index], IntPtr.Zero, 0, AsyncGetDataFlags.DoNotFlush);
                result.CheckError();
                if (result.Code == 0) { _fencePending[index] = false; return; }
            }
            finally { if (!holdingGate) _host.Gate.Release(); }
            if (watch.Elapsed.TotalSeconds > FenceTimeoutSeconds)
                throw new TimeoutException("ГП не завершил шаги Physarum 3D.");
            // Short waits spin: Sleep(1) may last a whole 15.6 ms scheduler tick.
            if (watch.ElapsedMilliseconds < 2) spinner.SpinOnce(sleep1Threshold: -1);
            else Thread.Sleep(1);
        }
    }

    private bool TryEnter(CancellationToken token)
    {
        try { _host.Gate.Wait(token); return true; }
        catch (OperationCanceledException) { return false; }
    }

    private void Dispatch(string entry, ID3D11ShaderResourceView? input,
        ID3D11UnorderedAccessView? output, ID3D11UnorderedAccessView? display)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ID3D11DeviceContext context = _host.Context;
        if (_constantsDirty)
        {
            var mapped = context.Map(_constants, MapMode.WriteDiscard);
            try { Marshal.Copy(_parameters, 0, mapped.DataPointer, _parameters.Length); }
            finally { context.Unmap(_constants, 0); }
            _constantsDirty = false;
        }
        context.CSSetShader(_shaders[entry]); context.CSSetConstantBuffer(0, _constants);
        context.CSSetShaderResource(0, input!);
        context.CSSetUnorderedAccessView(0, output!);
        context.CSSetUnorderedAccessView(1, display!);
        context.CSSetUnorderedAccessView(2, entry == "MoveAgents" ? _agents!.WriteView : null!);
        context.CSSetUnorderedAccessView(3, entry != "Publish" ? _deposits!.WriteView : null!);
        uint n = (uint)Settings.Size;
        if (entry == "MoveAgents") context.Dispatch(((uint)Settings.AgentCount + 127) / 128, 1, 1);
        else context.Dispatch((n + Physarum3DComputeShader.GroupX - 1) / Physarum3DComputeShader.GroupX,
            (n + Physarum3DComputeShader.GroupY - 1) / Physarum3DComputeShader.GroupY,
            (n + Physarum3DComputeShader.GroupZ - 1) / Physarum3DComputeShader.GroupZ);
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

    /// <summary>
    /// Освобождение из окна. Ссылка на устройство отпускается после очереди: если окно уже
    /// освободило рендер, эта ссылка последняя и уничтожает устройство вместе с его очередью.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _host.Gate.Wait();
        bool entered = true;
        try
        {
            if (_disposed) return;
            _disposed = true;
            DisposeResources();
        }
        finally { if (entered) _host.Gate.Release(); }
        _host.Release();
    }

    /// <summary>
    /// Освобождение под уже взятой очередью устройства (из рендера). Отпустить ссылку здесь можно:
    /// рендер держит свою до конца своего освобождения, поэтому устройство и очередь остаются живы.
    /// </summary>
    internal void DisposeWhileLocked()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeResources();
        _host.Release();
    }

    private void DisposeResources()
    {
        DisposeFields();
        _staging?.Dispose(); _staging = null;
        foreach (var shader in _shaders.Values) shader.Dispose();
        _shaders.Clear();
        foreach (var fence in _fences) fence?.Dispose();
        _constants?.Dispose();
    }

    private void DisposeFields()
    {
        _current?.Dispose(); _next?.Dispose(); _current = _next = null;
        _agents?.Dispose(); _deposits?.Dispose(); _agents = _deposits = null;
        for (int i = 0; i < _slots.Length; i++) { _slots[i]?.Dispose(); _slots[i] = null; }
    }

    private sealed class FieldBuffer : IDisposable
    {
        public ID3D11Buffer Buffer { get; }
        public ID3D11ShaderResourceView View { get; } = null!;
        public ID3D11UnorderedAccessView WriteView { get; } = null!;
        public FieldBuffer(ID3D11Device device, int count, int stride = 4)
        {
            Buffer = device.CreateBuffer(new BufferDescription { ByteWidth = (uint)(count * stride),
                Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess,
                MiscFlags = ResourceOptionFlags.BufferStructured, StructureByteStride = (uint)stride });
            try { View = device.CreateShaderResourceView(Buffer); WriteView = device.CreateUnorderedAccessView(Buffer); }
            catch { View?.Dispose(); Buffer.Dispose(); throw; }
        }
        public void Dispose() { WriteView.Dispose(); View.Dispose(); Buffer.Dispose(); }
    }

    /// <summary>Опубликованный кадр: точные агенты, след и его текстура R32_Float для рендера.</summary>
    private sealed class Slot : IDisposable
    {
        public FieldBuffer Field { get; }
        public FieldBuffer Agents { get; }
        public ID3D11Texture3D Display { get; }
        public ID3D11ShaderResourceView DisplayView { get; }
        public ID3D11UnorderedAccessView DisplayWrite { get; }
        public Slot(ID3D11Device device, int side, int agentCount)
        {
            Field = new(device, side * side * side); Agents = new(device, agentCount, 32);
            Display = device.CreateTexture3D(new Texture3DDescription
            {
                Width = (uint)side, Height = (uint)side, Depth = (uint)side, MipLevels = 1, Format = Format.R32_Float,
                Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess
            });
            DisplayView = device.CreateShaderResourceView(Display);
            DisplayWrite = device.CreateUnorderedAccessView(Display);
        }
        public void Dispose() { DisplayWrite.Dispose(); DisplayView.Dispose(); Display.Dispose(); Field.Dispose(); Agents.Dispose(); }
    }
}
