using System.Runtime.InteropServices;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Vortice.D3DCompiler;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Опубликованный кадр: возбуждение, круговые координаты фазы и вихревые нити для рендера,
/// точная копия знаковых U/V для сохранения.
/// Билет неизменяем; данные слота живут на ГП, пока в него не опубликован следующий кадр.
/// </summary>
public sealed record Excitable3DVolume(Excitable3DGpuSimulation Source, long Version, long Step, int Size, int Slot);

/// <summary>Two-field excitable medium on the shared D3D11 device. Whole steps, two immutable
/// publication slots, bounded submissions and lossless checkpoints follow the volume pipeline.
/// Barkley/FHN/AP use explicit Euler; Oregonator treats destruction semi-implicitly to preserve
/// positive concentrations at its stiff small-q rest state. No-flux or periodic six-neighbour stencil.</summary>
public sealed class Excitable3DGpuSimulation : IDisposable
{
    private const double FenceTimeoutSeconds = 15;

    private readonly Direct3DDeviceHost _host;
    private readonly Dictionary<string, ID3D11ComputeShader> _shaders = [];
    private readonly ID3D11Query[] _fences = new ID3D11Query[2];
    private readonly bool[] _fencePending = new bool[2];
    private readonly Slot?[] _slots = new Slot?[2];
    private readonly long[] _slotVersions = new long[2];
    private readonly float[] _parameters = new float[24];
    private ID3D11Buffer _constants = null!;
    private ID3D11Buffer? _staging;
    private FieldBuffer? _current, _next;
    private int _fenceIndex, _lastSlot = 1;
    private bool _constantsDirty = true;
    private long _step, _version;
    private bool _disposed;

    public Excitable3DGpuSimulation(Direct3DDeviceHost host, Excitable3DSettings settings) : this(host, settings, false) { }

    private Excitable3DGpuSimulation(Direct3DDeviceHost host, Excitable3DSettings settings, bool gateHeld)
    {
        settings.Validate();
        _host = host.AddRef();
        bool entered = false, created = false;
        try
        {
            if (!gateHeld) { _host.Gate.Wait(); entered = true; }
            ID3D11Device device = _host.Device;
            foreach (var entry in Excitable3DComputeShader.CacheEntries)
                _shaders[entry.EntryPoint] = device.CreateComputeShader(ShaderBytecodeCache.GetOrCompile(entry,
                    e => Compiler.Compile(e.Source, e.EntryPoint, "Excitable3D", e.Profile)).Span);
            _constants = device.CreateBuffer(new BufferDescription { ByteWidth = 96, Usage = ResourceUsage.Dynamic,
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
    internal static Excitable3DGpuSimulation CreateLocked(Direct3DDeviceHost host, Excitable3DSettings settings) =>
        new(host, settings, true);

    internal bool Uses(Direct3DDeviceHost host) => ReferenceEquals(_host, host);

    public Excitable3DSettings Settings { get; private set; } = new();
    public string DeviceName => "ГП · " + _host.AdapterName;
    public int Size => Settings.Size;
    public long Step => Volatile.Read(ref _step);

    /// <summary>Новое уравнение и поле (сохранённое или стартовая затравка); ресурсы переживают смену при той же сетке.</summary>
    public void Reset(Excitable3DSettings settings)
    {
        settings.Validate();
        _host.Gate.Wait();
        try { ResetLocked(settings); }
        finally { _host.Gate.Release(); }
    }

    internal void ResetLocked(Excitable3DSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        settings.Validate();
        int size = settings.Size;
        if (_current is null || Settings.Size != size)
        {
            DisposeFields();
            ID3D11Device device = _host.Device;
            _current = new(device, size); _next = new(device, size);
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new(device, size);
        }
        float[] values = settings.Field?.Values ?? InitialField(settings);
        _host.Context.UpdateSubresource(values, _current.Buffer);
        Settings = settings with { Field = null };
        _step = settings.Field?.Step ?? 0;
        Array.Clear(_slotVersions);
        _parameters[0] = (float)settings.DiffusionU; _parameters[1] = (float)settings.DiffusionV;
        _parameters[2] = (float)settings.A; _parameters[3] = (float)settings.B;
        _parameters[4] = size; _parameters[5] = (float)settings.TimeStep;
        _parameters[6] = (float)(1 / (settings.Spacing * settings.Spacing)); _parameters[7] = (float)settings.Model;
        _parameters[12] = (float)settings.Epsilon; _parameters[13] = (float)settings.Mu1;
        _parameters[14] = (float)settings.Mu2; _parameters[15] = (float)settings.Boundary;
        var phase = PhaseCenter(settings);
        _parameters[16] = phase.U; _parameters[17] = phase.V;
        _parameters[18] = settings.Model == Excitable3DModel.FitzHughNagumo ? .25f : 1f;
        _parameters[19] = settings.Model == Excitable3DModel.FitzHughNagumo ? .5f : 0;
        _parameters[20] = settings.Model == Excitable3DModel.FitzHughNagumo ? 2 : 1;
        _constantsDirty = true;
    }

    /// <summary>
    /// Ставит шаги в очередь ГП порциями и не держит устройство между ними: полосы кадра идут
    /// вперемежку. Ждёт не конца работы, а предпоследней порции — видеокарта не простаивает,
    /// пока поток спит. Отмена прекращает подачу новых порций; уже поданные шаги — целые шаги,
    /// поэтому поле остаётся согласованным. Возвращает число поданных шагов.
    /// </summary>
    public int Advance(int steps, CancellationToken token)
    {
        if (steps < 0) throw new ArgumentOutOfRangeException(nameof(steps));
        int done = 0;
        while (done < steps && !token.IsCancellationRequested)
        {
            int count = Math.Min(StepsPerSubmit, steps - done);
            if (!TryEnter(token)) break;
            try { SubmitStepsLocked(count); }
            finally { _host.Gate.Release(); }
            done += count;
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

    public void Inject(double x, double y, double z, double radius)
    {
        ValidateBrush(x, y, z, radius);
        _host.Gate.Wait();
        try
        {
            _parameters[8] = (float)x; _parameters[9] = (float)y; _parameters[10] = (float)z; _parameters[11] = (float)radius;
            _constantsDirty = true;
            Dispatch("Inject", null, _current!.WriteView, null);
        }
        finally { _host.Gate.Release(); }
    }

    /// <summary>Публикует текущее поле для рендера и сохранения, не трогая слот кадра <paramref name="keep"/>.</summary>
    public Excitable3DVolume Publish(Excitable3DVolume? keep = null)
    {
        _host.Gate.Wait();
        try { return PublishLocked(keep); }
        finally { _host.Gate.Release(); }
    }

    internal Excitable3DVolume PublishLocked(Excitable3DVolume? keep = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int index = IsCurrent(keep) ? keep!.Slot ^ 1 : _lastSlot ^ 1;
        Slot slot = _slots[index]!;
        _host.Context.CopyResource(slot.Field.Buffer, _current!.Buffer);
        Dispatch("Publish", slot.Field.View, null, slot.DisplayWrite);
        _version++;
        _slotVersions[index] = _version;
        _lastSlot = index;
        return new(this, _version, _step, Settings.Size, index);
    }

    private bool IsCurrent(Excitable3DVolume? volume) =>
        volume is not null && ReferenceEquals(volume.Source, this) && volume.Size == Settings.Size &&
        _slotVersions[volume.Slot] == volume.Version;

    /// <summary>Текстура возбуждения, фазы и нитей; вызывается рендером под очередью устройства.</summary>
    internal ID3D11ShaderResourceView ViewLocked(Excitable3DVolume volume)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsCurrent(volume)) throw new InvalidOperationException("Опубликованный кадр уже заменён.");
        return _slots[volume.Slot]!.DisplayView;
    }

    /// <summary>Точные U/V опубликованного кадра — контрольная точка для сохранения.</summary>
    public Excitable3DField ReadCheckpoint(Excitable3DVolume volume)
    {
        if (!ReferenceEquals(volume.Source, this)) throw new ArgumentException("Кадр другого моделирования.");
        _host.Gate.Wait();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!IsCurrent(volume)) throw new InvalidOperationException("Кадр Возбудимая среда 3D уже заменён более новым.");
            return ReadLocked(_slots[volume.Slot]!.Field.Buffer, volume.Step);
        }
        finally { _host.Gate.Release(); }
    }

    /// <summary>Текущее поле, включая ещё не опубликованные шаги.</summary>
    public Excitable3DField ReadCurrent()
    {
        _host.Gate.Wait();
        try { ObjectDisposedException.ThrowIf(_disposed, this); return ReadLocked(_current!.Buffer, _step); }
        finally { _host.Gate.Release(); }
    }

    private Excitable3DField ReadLocked(ID3D11Buffer source, long step)
    {
        int size = Settings.Size;
        int bytes = size * size * size * 8;
        if (_staging is null || _staging.Description.ByteWidth != bytes)
        {
            _staging?.Dispose();
            _staging = _host.Device.CreateBuffer(new BufferDescription
                { ByteWidth = (uint)bytes, Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read });
        }
        ID3D11DeviceContext context = _host.Context;
        context.CopyResource(_staging, source);
        var mapped = context.Map(_staging, MapMode.Read);
        float[] values = new float[bytes / 4];
        try { Marshal.Copy(mapped.DataPointer, values, 0, values.Length); }
        finally { context.Unmap(_staging, 0); }
        return new(size, step, values, true);
    }

    /// <summary>
    /// Порция подачи: на мелкой сетке шагов больше, чтобы ожидание между порциями не
    /// становилось дольше самой работы; на 64³ и 128³ — 16 шагов.
    /// </summary>
    private int StepsPerSubmit
    {
        get { int n = Settings.Size; return Math.Clamp(4_000_000 / (n * n * n), 16, 128); }
    }

    private void SubmitStepsLocked(int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (int i = 0; i < count; i++)
        {
            Dispatch("Evolve", _current!.View, _next!.WriteView, null);
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
                throw new TimeoutException("ГП не завершил шаги Возбудимая среда 3D.");
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
        uint n = (uint)Settings.Size;
        context.Dispatch((n + Excitable3DComputeShader.GroupX - 1) / Excitable3DComputeShader.GroupX,
            (n + Excitable3DComputeShader.GroupY - 1) / Excitable3DComputeShader.GroupY,
            (n + Excitable3DComputeShader.GroupZ - 1) / Excitable3DComputeShader.GroupZ);
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

    /// <summary>
    /// Освобождение из окна. Ссылка на устройство отпускается после очереди: если окно уже
    /// освободило рендер, эта ссылка последняя и уничтожает устройство вместе с его очередью.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _host.Gate.Wait();
        try
        {
            if (_disposed) return;
            _disposed = true;
            DisposeResources();
        }
        finally { _host.Gate.Release(); }
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
        for (int i = 0; i < _slots.Length; i++) { _slots[i]?.Dispose(); _slots[i] = null; }
    }

    private sealed class FieldBuffer : IDisposable
    {
        public ID3D11Buffer Buffer { get; }
        public ID3D11ShaderResourceView View { get; } = null!;
        public ID3D11UnorderedAccessView WriteView { get; } = null!;
        public FieldBuffer(ID3D11Device device, int side)
        {
            Buffer = device.CreateBuffer(new BufferDescription { ByteWidth = (uint)(side * side * side * 8),
                Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess,
                MiscFlags = ResourceOptionFlags.BufferStructured, StructureByteStride = 8 });
            try { View = device.CreateShaderResourceView(Buffer); WriteView = device.CreateUnorderedAccessView(Buffer); }
            catch { View?.Dispose(); Buffer.Dispose(); throw; }
        }
        public void Dispose() { WriteView.Dispose(); View.Dispose(); Buffer.Dispose(); }
    }

    /// <summary>Опубликованный кадр: точные U/V и объём V в формате R32_Float для рендера.</summary>
    private sealed class Slot : IDisposable
    {
        public FieldBuffer Field { get; }
        public ID3D11Texture3D Display { get; }
        public ID3D11ShaderResourceView DisplayView { get; }
        public ID3D11UnorderedAccessView DisplayWrite { get; }
        public Slot(ID3D11Device device, int side)
        {
            Field = new(device, side);
            Display = device.CreateTexture3D(new Texture3DDescription
            {
                Width = (uint)side, Height = (uint)side, Depth = (uint)side, MipLevels = 1, Format = Format.R32G32B32A32_Float,
                Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess
            });
            DisplayView = device.CreateShaderResourceView(Display);
            DisplayWrite = device.CreateUnorderedAccessView(Display);
        }
        public void Dispose() { DisplayWrite.Dispose(); DisplayView.Dispose(); Display.Dispose(); Field.Dispose(); }
    }
}
