using System.Runtime.InteropServices;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Vortice.D3DCompiler;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Опубликованный кадр моделирования: объём V для рендера и точная копия U/V для сохранения.
/// Билет неизменяем; данные слота живут на ГП, пока в него не опубликован следующий кадр.
/// </summary>
public sealed record GrayScott3DVolume(GrayScott3DGpuSimulation Source, long Version, long Step, int Size, int Slot);

/// <summary>
/// Gray–Scott в трёхмерной сетке целиком на ГП: явный Эйлер, dt = 1, шеститочечный лапласиан,
/// периодические границы. Поле не покидает видеокарту: <see cref="Publish"/> копирует V в
/// текстуру, которую рендер того же <see cref="Direct3DDeviceHost"/> читает напрямую, а в
/// оперативную память U/V попадает только по запросу — для сохранения и проверок.
/// Слотов публикации два: новый кадр пишется мимо того, что показан, поэтому кадр, который
/// ещё рисуется или сохраняется, не меняется под ним.
/// Методы без суффикса <c>Locked</c> сами встают в очередь устройства; с суффиксом — вызываются
/// под уже взятым <see cref="Direct3DDeviceHost.Gate"/> (подготовка превью внутри рендера).
/// </summary>
public sealed class GrayScott3DGpuSimulation : IDisposable
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
    private int _fenceIndex, _lastSlot = 1;
    private bool _constantsDirty = true;
    private long _step, _version;
    private bool _disposed;

    public GrayScott3DGpuSimulation(Direct3DDeviceHost host, GrayScott3DSettings settings) : this(host, settings, false) { }

    private GrayScott3DGpuSimulation(Direct3DDeviceHost host, GrayScott3DSettings settings, bool gateHeld)
    {
        settings.Validate();
        _host = host.AddRef();
        bool entered = false;
        try
        {
            if (!gateHeld) { _host.Gate.Wait(); entered = true; }
            ID3D11Device device = _host.Device;
            foreach (var entry in GrayScott3DComputeShader.CacheEntries)
                _shaders[entry.EntryPoint] = device.CreateComputeShader(ShaderBytecodeCache.GetOrCompile(entry,
                    e => Compiler.Compile(e.Source, e.EntryPoint, "GrayScott3D", e.Profile)).Span);
            _constants = device.CreateBuffer(new BufferDescription { ByteWidth = 48, Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer, CPUAccessFlags = CpuAccessFlags.Write });
            for (int i = 0; i < _fences.Length; i++)
                _fences[i] = device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));
            ResetLocked(settings);
        }
        catch { DisposeResources(); _host.Release(); throw; }
        finally { if (entered) _host.Gate.Release(); }
    }

    /// <summary>Для рендера, который уже держит очередь устройства.</summary>
    internal static GrayScott3DGpuSimulation CreateLocked(Direct3DDeviceHost host, GrayScott3DSettings settings) =>
        new(host, settings, true);

    internal bool Uses(Direct3DDeviceHost host) => ReferenceEquals(_host, host);

    public GrayScott3DSettings Settings { get; private set; } = new();
    public string DeviceName => "ГП · " + _host.AdapterName;
    public int Size => Settings.Size;
    public long Step => Volatile.Read(ref _step);

    /// <summary>Новое уравнение и поле (сохранённое или стартовая затравка); ресурсы переживают смену при той же сетке.</summary>
    public void Reset(GrayScott3DSettings settings)
    {
        settings.Validate();
        _host.Gate.Wait();
        try { ResetLocked(settings); }
        finally { _host.Gate.Release(); }
    }

    internal void ResetLocked(GrayScott3DSettings settings)
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
        _parameters[2] = (float)settings.Feed; _parameters[3] = (float)settings.Kill; _parameters[4] = size;
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
    public GrayScott3DVolume Publish(GrayScott3DVolume? keep = null)
    {
        _host.Gate.Wait();
        try { return PublishLocked(keep); }
        finally { _host.Gate.Release(); }
    }

    internal GrayScott3DVolume PublishLocked(GrayScott3DVolume? keep = null)
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

    private bool IsCurrent(GrayScott3DVolume? volume) =>
        volume is not null && ReferenceEquals(volume.Source, this) && volume.Size == Settings.Size &&
        _slotVersions[volume.Slot] == volume.Version;

    /// <summary>Текстура V опубликованного кадра; вызывается рендером под очередью устройства.</summary>
    internal ID3D11ShaderResourceView ViewLocked(GrayScott3DVolume volume)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(volume.Source, this)) throw new ArgumentException("Кадр другого моделирования.");
        return _slots[volume.Slot]!.DisplayView;
    }

    /// <summary>Точные U/V опубликованного кадра — контрольная точка для сохранения.</summary>
    public GrayScott3DField ReadCheckpoint(GrayScott3DVolume volume)
    {
        if (!ReferenceEquals(volume.Source, this)) throw new ArgumentException("Кадр другого моделирования.");
        _host.Gate.Wait();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!IsCurrent(volume)) throw new InvalidOperationException("Кадр Gray–Scott 3D уже заменён более новым.");
            return ReadLocked(_slots[volume.Slot]!.Field.Buffer, volume.Step);
        }
        finally { _host.Gate.Release(); }
    }

    /// <summary>Текущее поле, включая ещё не опубликованные шаги.</summary>
    public GrayScott3DField ReadCurrent()
    {
        _host.Gate.Wait();
        try { ObjectDisposedException.ThrowIf(_disposed, this); return ReadLocked(_current!.Buffer, _step); }
        finally { _host.Gate.Release(); }
    }

    private GrayScott3DField ReadLocked(ID3D11Buffer source, long step)
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
                throw new TimeoutException("ГП не завершил шаги Gray–Scott 3D.");
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
        context.Dispatch((n + GrayScott3DComputeShader.GroupX - 1) / GrayScott3DComputeShader.GroupX,
            (n + GrayScott3DComputeShader.GroupY - 1) / GrayScott3DComputeShader.GroupY,
            (n + GrayScott3DComputeShader.GroupZ - 1) / GrayScott3DComputeShader.GroupZ);
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

    public void Dispose()
    {
        if (_disposed) return;
        bool entered = _host.Gate.Wait(TimeSpan.FromSeconds(10));
        try { DisposeWhileLocked(); }
        finally { if (entered) _host.Gate.Release(); }
    }

    /// <summary>Освобождение под уже взятой очередью устройства (из рендера).</summary>
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
                Width = (uint)side, Height = (uint)side, Depth = (uint)side, MipLevels = 1, Format = Format.R32_Float,
                Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess
            });
            DisplayView = device.CreateShaderResourceView(Display);
            DisplayWrite = device.CreateUnorderedAccessView(Display);
        }
        public void Dispose() { DisplayWrite.Dispose(); DisplayView.Dispose(); Display.Dispose(); Field.Dispose(); }
    }
}
