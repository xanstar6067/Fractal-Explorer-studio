using System.Runtime.InteropServices;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Vortice.D3DCompiler;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Опубликованный кадр моделирования: объём поля и карты масштабов для рендера и точная копия
/// поля для сохранения. Билет неизменяем; данные слота живут на ГП до следующей публикации в него.
/// </summary>
public sealed record Turing3DVolume(Turing3DGpuSimulation Source, long Version, long Step, int Size, int Slot);

/// <summary>
/// Многомасштабные узоры Тьюринга в кубе целиком на ГП. Поле не покидает видеокарту:
/// <see cref="Publish"/> пишет текстуру, которую рендер того же <see cref="Direct3DDeviceHost"/>
/// читает напрямую, а в оперативную память поле попадает только по запросу — для сохранения.
/// Слотов публикации два: новый кадр пишется мимо показанного. Шаги подаются только целиком,
/// поэтому отмена между порциями оставляет согласованное поле.
/// Методы без суффикса <c>Locked</c> сами встают в очередь устройства; с суффиксом — вызываются
/// под уже взятым <see cref="Direct3DDeviceHost.Gate"/> (подготовка превью внутри рендера).
/// </summary>
public sealed class Turing3DGpuSimulation : IDisposable
{
    private const double FenceTimeoutSeconds = 30;
    /// <summary>Наибольшая группа — икосаэдр с отражениями (120); запас до предела замыкания.</summary>
    private const int MaxGroupSize = 240;

    private readonly Direct3DDeviceHost _host;
    private readonly Dictionary<string, ID3D11ComputeShader> _shaders = [];
    private readonly ID3D11Query[] _fences = new ID3D11Query[2];
    private readonly bool[] _fencePending = new bool[2];
    private readonly Slot?[] _slots = new Slot?[2];
    private readonly long[] _slotVersions = new long[2];
    private readonly float[] _parameters = new float[32];
    private ID3D11Buffer _constants = null!;
    private ID3D11Buffer? _staging;
    private GpuBuffer? _value, _scale, _nextValue, _nextScale, _activator, _inhibitor, _work, _temporary;
    private GpuBuffer? _best, _range0, _range1, _group;
    private IReadOnlyList<(int Layer, int Radius, int Inhibitor, double Amount)> _layers = [];
    private int _groupSize = 1, _fenceIndex, _lastSlot = 1;
    private long _step, _version;
    private bool _disposed;

    public Turing3DGpuSimulation(Direct3DDeviceHost host, Turing3DSettings settings) : this(host, settings, false) { }

    private Turing3DGpuSimulation(Direct3DDeviceHost host, Turing3DSettings settings, bool gateHeld)
    {
        settings.Validate();
        _host = host.AddRef();
        bool entered = false;
        try
        {
            if (!gateHeld) { _host.Gate.Wait(); entered = true; }
            ID3D11Device device = _host.Device;
            foreach (var entry in Turing3DComputeShader.CacheEntries)
                _shaders[entry.EntryPoint] = device.CreateComputeShader(ShaderBytecodeCache.GetOrCompile(entry,
                    e => Compiler.Compile(e.Source, e.EntryPoint, "Turing3D", e.Profile)).Span);
            _constants = device.CreateBuffer(new BufferDescription { ByteWidth = 128, Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer, CPUAccessFlags = CpuAccessFlags.Write });
            for (int i = 0; i < _fences.Length; i++)
                _fences[i] = device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));
            _group = new GpuBuffer(device, MaxGroupSize * 4, 16);
            ResetLocked(settings);
        }
        catch { DisposeResources(); _host.Release(); throw; }
        finally { if (entered) _host.Gate.Release(); }
    }

    /// <summary>Для рендера, который уже держит очередь устройства.</summary>
    internal static Turing3DGpuSimulation CreateLocked(Direct3DDeviceHost host, Turing3DSettings settings) =>
        new(host, settings, true);

    internal bool Uses(Direct3DDeviceHost host) => ReferenceEquals(_host, host);

    /// <summary>Действующее правило; <c>Field</c> всегда null — поле живёт на ГП.</summary>
    public Turing3DSettings Settings { get; private set; } = new();
    public string DeviceName => "ГП · " + _host.AdapterName;
    public int Size => Settings.Size;
    public long Step => Volatile.Read(ref _step);
    /// <summary>Число элементов группы симметрии, по которым повторяются шаг и кисть.</summary>
    public int SymmetryOrder => _groupSize;

    /// <summary>Новое поле — сохранённое или случайное по числу генератора — и правило к нему.</summary>
    public void Reset(Turing3DSettings settings)
    {
        settings.Validate();
        _host.Gate.Wait();
        try { ResetLocked(settings); }
        finally { _host.Gate.Release(); }
    }

    internal void ResetLocked(Turing3DSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        settings.Validate();
        int size = settings.Size;
        if (_value is null || Settings.Size != size) AllocateFields(size);
        ConfigureLocked(settings);
        Array.Clear(_slotVersions);
        ID3D11DeviceContext context = _host.Context;
        if (settings.Field is { } field)
        {
            context.UpdateSubresource(field.Values, _value!.Buffer);
            context.UpdateSubresource(field.Scales.Select(scale => (float)scale).ToArray(), _scale!.Buffer);
            _step = field.Step;
        }
        else
        {
            // The same start as in 2D: uniform noise, made symmetric and normalised.
            int count = size * size * size;
            var noise = new float[count]; var random = new Random(settings.Seed);
            for (int i = 0; i < count; i++) noise[i] = (float)(random.NextDouble() * 2 - 1);
            context.UpdateSubresource(noise, _nextValue!.Buffer);
            context.UpdateSubresource(new float[count], _nextScale!.Buffer);
            FinishStepLocked();
            _step = 0;
        }
    }

    /// <summary>Новое правило для текущего поля: время и поле сохраняются, действует со следующего шага.</summary>
    public void Configure(Turing3DSettings settings)
    {
        settings.Validate();
        _host.Gate.Wait();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (settings.Size != Settings.Size) throw new ArgumentException("Смена сетки требует переноса поля.");
            ConfigureLocked(settings);
        }
        finally { _host.Gate.Release(); }
    }

    private void ConfigureLocked(Turing3DSettings settings)
    {
        Settings = settings with { Field = null, Live = null };
        _layers = settings.EffectiveLayers(settings.Size);
        var group = Turing3DSymmetryGroup.Build(settings.Symmetry, settings.Arms, settings.Mirror);
        _groupSize = group.Count;
        // UpdateSubresource fills the whole buffer, so the source array must cover all of it.
        var packed = new float[MaxGroupSize * 16];
        Turing3DSymmetryGroup.Pack(group).CopyTo(packed, 0);
        _host.Context.UpdateSubresource(packed, _group!.Buffer);
        // Display value of each scale: its rank among the enabled ones, spread over the palette.
        int enabled = _layers.Count;
        for (int i = 0; i < 8; i++) _parameters[20 + i] = 0;
        for (int rank = 0; rank < enabled; rank++)
            _parameters[20 + _layers[rank].Layer] = enabled > 1 ? rank / (float)(enabled - 1) : .5f;
    }

    /// <summary>
    /// Ставит шаги в очередь ГП порциями и не держит устройство между ними: полосы кадра идут
    /// вперемежку. Ждёт не конца работы, а предпоследней порции. Отмена прекращает подачу новых
    /// порций; уже поданные шаги — целые шаги. Возвращает число поданных шагов.
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

    /// <summary>Сферический мазок в долях ребра, повторённый по группе симметрии; время не идёт.</summary>
    public void Paint(double x, double y, double z, double radius, double strength, TuringBrush brush)
    {
        ValidateBrush(x, y, z, radius, strength);
        _host.Gate.Wait();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Set(0, Size, 0, (int)Settings.Boundary, 0);
            Set(1, 0, 0, 0, _groupSize);
            Set(2, (float)x, (float)y, (float)z, (float)radius);
            Set(3, (float)strength, (int)brush, BitConverter.Int32BitsToSingle(Settings.Seed), BitConverter.Int32BitsToSingle(unchecked((int)_step)));
            DispatchVolume("Paint", outA: _value);
        }
        finally { _host.Gate.Release(); }
    }

    /// <summary>Публикует текущее поле для рендера и сохранения, не трогая слот кадра <paramref name="keep"/>.</summary>
    public Turing3DVolume Publish(Turing3DVolume? keep = null)
    {
        _host.Gate.Wait();
        try { return PublishLocked(keep); }
        finally { _host.Gate.Release(); }
    }

    internal Turing3DVolume PublishLocked(Turing3DVolume? keep = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int index = IsCurrent(keep) ? keep!.Slot ^ 1 : _lastSlot ^ 1;
        Slot slot = _slots[index]!;
        ID3D11DeviceContext context = _host.Context;
        context.CopyResource(slot.Value.Buffer, _value!.Buffer);
        context.CopyResource(slot.Scale.Buffer, _scale!.Buffer);
        Set(0, Size, 0, 0, 0);
        DispatchVolume("Publish", a: slot.Value, b: slot.Scale, display: slot.DisplayWrite);
        _version++;
        _slotVersions[index] = _version;
        _lastSlot = index;
        return new(this, _version, _step, Size, index);
    }

    private bool IsCurrent(Turing3DVolume? volume) =>
        volume is not null && ReferenceEquals(volume.Source, this) && volume.Size == Size &&
        _slotVersions[volume.Slot] == volume.Version;

    /// <summary>Текстура опубликованного кадра (поле и масштаб); вызывается рендером под очередью устройства.</summary>
    internal ID3D11ShaderResourceView ViewLocked(Turing3DVolume volume)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(volume.Source, this)) throw new ArgumentException("Кадр другого моделирования.");
        return _slots[volume.Slot]!.DisplayView;
    }

    /// <summary>Точное поле опубликованного кадра — контрольная точка для сохранения.</summary>
    public Turing3DField ReadCheckpoint(Turing3DVolume volume)
    {
        if (!ReferenceEquals(volume.Source, this)) throw new ArgumentException("Кадр другого моделирования.");
        _host.Gate.Wait();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!IsCurrent(volume)) throw new InvalidOperationException("Кадр узора Тьюринга 3D уже заменён более новым.");
            Slot slot = _slots[volume.Slot]!;
            return ReadLocked(slot.Value, slot.Scale, volume.Step);
        }
        finally { _host.Gate.Release(); }
    }

    /// <summary>Текущее поле, включая ещё не опубликованные шаги.</summary>
    public Turing3DField ReadCurrent()
    {
        _host.Gate.Wait();
        try { ObjectDisposedException.ThrowIf(_disposed, this); return ReadLocked(_value!, _scale!, _step); }
        finally { _host.Gate.Release(); }
    }

    private Turing3DField ReadLocked(GpuBuffer value, GpuBuffer scale, long step)
    {
        float[] values = ReadFloats(value);
        float[] scales = ReadFloats(scale);
        var map = new byte[scales.Length];
        for (int i = 0; i < map.Length; i++) map[i] = (byte)scales[i];
        return new(Size, step, values, map, true);
    }

    private float[] ReadFloats(GpuBuffer source)
    {
        int bytes = source.Count * 4;
        if (_staging is null || _staging.Description.ByteWidth != bytes)
        {
            _staging?.Dispose();
            _staging = _host.Device.CreateBuffer(new BufferDescription
                { ByteWidth = (uint)bytes, Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read });
        }
        ID3D11DeviceContext context = _host.Context;
        context.CopyResource(_staging, source.Buffer);
        var mapped = context.Map(_staging, MapMode.Read);
        var values = new float[source.Count];
        try { Marshal.Copy(mapped.DataPointer, values, 0, values.Length); }
        finally { context.Unmap(_staging, 0); }
        return values;
    }

    /// <summary>Порция подачи: шаг дорог, поэтому на крупной сетке — по одному, на мелкой — до 16.</summary>
    private int StepsPerSubmit
    {
        get { long n = Size; return (int)Math.Clamp(6_000_000 / (n * n * n * Math.Max(_layers.Count, 1)), 1, 16); }
    }

    private void SubmitStepsLocked(int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (int i = 0; i < count; i++)
        {
            EvolveLocked();
            _step++;
        }
        _fenceIndex ^= 1;
        _host.Context.End(_fences[_fenceIndex]);
        _fencePending[_fenceIndex] = true;
        _host.Context.Flush();
    }

    /// <summary>Один целый шаг: масштабы по очереди, затем сложение, симметрия и нормировка.</summary>
    private void EvolveLocked()
    {
        bool first = true;
        foreach (var layer in _layers)
        {
            Smooth(_value!, ref _activator, layer.Radius);
            Smooth(_value!, ref _inhibitor, layer.Inhibitor);
            Set(0, Size, 0, 0, 0);
            DispatchCells("Difference", a: _activator, b: _inhibitor, outA: _work);
            Smooth(_work!, ref _work, layer.Radius);
            Set(0, Size, 0, 0, 0);
            Set(1, first ? 1 : 0, layer.Layer, (float)layer.Amount, _groupSize);
            DispatchCells("Choose", a: _work, b: _activator, c: _inhibitor, outB: _nextScale, outD: _best);
            first = false;
        }
        Set(0, Size, 0, 0, 0);
        DispatchCells("Compose", a: _value, d: _best, outA: _nextValue);
        FinishStepLocked();
    }

    /// <summary>Из (_nextValue, _nextScale) — симметричное нормированное поле в (_value, _scale).</summary>
    private void FinishStepLocked()
    {
        if (_groupSize > 1 || Settings.Region != Turing3DRegion.Cube)
        {
            // Rotated and radially mirrored images reach outside the cube; they fold back as in 2D.
            Set(0, Size, 0, (int)TuringBoundary.Reflect, 0);
            Set(1, 0, 0, 0, _groupSize);
            Set(4, 0, 0, (int)Settings.Region, (float)Settings.ShellThickness);
            DispatchVolume("Symmetry", a: _nextValue, b: _nextScale, outA: _value, outB: _scale);
        }
        else
        {
            (_value, _nextValue) = (_nextValue, _value);
            (_scale, _nextScale) = (_nextScale, _scale);
        }
        int cells = Size * Size * Size, groups = (cells + 255) / 256;
        Set(0, Size, 0, 0, 0);
        Set(4, cells, 0, 0, 0);
        Dispatch("Reduce", (uint)groups, 1, 1, a: _value, outD: _range0);
        int remaining = groups; GpuBuffer range = _range0!, other = _range1!;
        while (remaining > 1)
        {
            Set(4, remaining, 1, 0, 0);
            remaining = (remaining + 255) / 256;
            Dispatch("Reduce", (uint)remaining, 1, 1, d: range, outD: other);
            (range, other) = (other, range);
        }
        DispatchCells("Normalize", d: range, outA: _value);
    }

    /// <summary>
    /// Три квадратных окна (приближение гауссова окружения, как в 2D) по трём осям: один проход
    /// на ось, все окна — внутри проход. Результат оказывается в <paramref name="target"/>:
    /// проходы чередуют его с общим временным буфером, и при нечётном их числе они меняются
    /// ролями без копирования. Источник не перезаписывается, пока не прочитан целиком.
    /// </summary>
    private void Smooth(GpuBuffer source, ref GpuBuffer? target, int radius)
    {
        int[] boxes = TuringSimulation.GaussianBoxWidths(radius * .75).Select(width => width / 2).ToArray();
        if (boxes.All(box => box == 0))
        {
            if (!ReferenceEquals(source, target)) _host.Context.CopyResource(target!.Buffer, source.Buffer);
            return;
        }
        GpuBuffer current = source;
        uint n = (uint)Size;
        for (int axis = 0; axis < 3; axis++)
        {
            GpuBuffer next = ReferenceEquals(current, _temporary) ? target! : _temporary!;
            Set(0, Size, 0, (int)Settings.Boundary, axis);
            Set(7, boxes[0], boxes[1], boxes[2], 0);
            Dispatch("Blur", n, n, 1, a: current, outA: next);
            current = next;
        }
        if (ReferenceEquals(current, _temporary)) (target, _temporary) = (_temporary, target);
    }

    private void Set(int index, float x, float y, float z, float w)
    {
        _parameters[index * 4] = x; _parameters[index * 4 + 1] = y;
        _parameters[index * 4 + 2] = z; _parameters[index * 4 + 3] = w;
    }

    private void DispatchCells(string entry, GpuBuffer? a = null, GpuBuffer? b = null, GpuBuffer? c = null, GpuBuffer? d = null,
        GpuBuffer? outA = null, GpuBuffer? outB = null, GpuBuffer? outD = null)
    {
        long cells = (long)Size * Size * Size;
        Dispatch(entry, (uint)((cells + Turing3DComputeShader.CellGroup - 1) / Turing3DComputeShader.CellGroup), 1, 1, a, b, c, d, outA, outB, outD);
    }

    private void DispatchVolume(string entry, GpuBuffer? a = null, GpuBuffer? b = null,
        GpuBuffer? outA = null, GpuBuffer? outB = null, ID3D11UnorderedAccessView? display = null)
    {
        uint n = (uint)Size;
        Dispatch(entry, (n + Turing3DComputeShader.VolumeX - 1) / Turing3DComputeShader.VolumeX,
            (n + Turing3DComputeShader.VolumeY - 1) / Turing3DComputeShader.VolumeY,
            (n + Turing3DComputeShader.VolumeZ - 1) / Turing3DComputeShader.VolumeZ, a, b, null, null, outA, outB, null, display);
    }

    private void Dispatch(string entry, uint x, uint y, uint z, GpuBuffer? a = null, GpuBuffer? b = null, GpuBuffer? c = null,
        GpuBuffer? d = null, GpuBuffer? outA = null, GpuBuffer? outB = null, GpuBuffer? outD = null, ID3D11UnorderedAccessView? display = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ID3D11DeviceContext context = _host.Context;
        var mapped = context.Map(_constants, MapMode.WriteDiscard);
        try { Marshal.Copy(_parameters, 0, mapped.DataPointer, _parameters.Length); }
        finally { context.Unmap(_constants, 0); }
        context.CSSetShader(_shaders[entry]); context.CSSetConstantBuffer(0, _constants);
        context.CSSetShaderResource(0, a?.View!); context.CSSetShaderResource(1, b?.View!);
        context.CSSetShaderResource(2, c?.View!); context.CSSetShaderResource(3, d?.View!);
        context.CSSetShaderResource(4, _group!.View);
        context.CSSetUnorderedAccessView(0, outA?.WriteView!); context.CSSetUnorderedAccessView(1, outB?.WriteView!);
        context.CSSetUnorderedAccessView(2, outD?.WriteView!); context.CSSetUnorderedAccessView(3, display!);
        context.Dispatch(x, y, z);
        // Explicit unbinding avoids SRV/UAV hazards on the next pass, which often swaps the roles.
        for (uint slot = 0; slot < 5; slot++) context.CSSetShaderResource(slot, null!);
        for (uint slot = 0; slot < 4; slot++) context.CSSetUnorderedAccessView(slot, null!);
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
                throw new TimeoutException("ГП не завершил шаги узора Тьюринга 3D. Попробуйте уменьшить сетку.");
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

    private void AllocateFields(int size)
    {
        DisposeFields();
        ID3D11Device device = _host.Device;
        int cells = size * size * size, groups = (cells + 255) / 256;
        _value = new(device, cells, 4); _scale = new(device, cells, 4);
        _nextValue = new(device, cells, 4); _nextScale = new(device, cells, 4);
        _activator = new(device, cells, 4); _inhibitor = new(device, cells, 4);
        _work = new(device, cells, 4); _temporary = new(device, cells, 4);
        _best = new(device, cells, 8);
        _range0 = new(device, groups, 8); _range1 = new(device, (groups + 255) / 256, 8);
        for (int i = 0; i < _slots.Length; i++) _slots[i] = new(device, size);
    }

    internal static void ValidateBrush(double x, double y, double z, double radius, double strength)
    {
        if (!double.IsFinite(x + y + z + radius + strength) || x is < 0 or > 1 || y is < 0 or > 1 || z is < 0 or > 1 ||
            radius is < .02 or > .3 || strength is < .05 or > 1)
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
        _group?.Dispose(); _group = null;
        _staging?.Dispose(); _staging = null;
        foreach (var shader in _shaders.Values) shader.Dispose();
        _shaders.Clear();
        foreach (var fence in _fences) fence?.Dispose();
        _constants?.Dispose();
    }

    private void DisposeFields()
    {
        foreach (var buffer in new[] { _value, _scale, _nextValue, _nextScale, _activator, _inhibitor, _work, _temporary, _best, _range0, _range1 })
            buffer?.Dispose();
        _value = _scale = _nextValue = _nextScale = _activator = _inhibitor = _work = _temporary = _best = _range0 = _range1 = null;
        for (int i = 0; i < _slots.Length; i++) { _slots[i]?.Dispose(); _slots[i] = null; }
    }

    private sealed class GpuBuffer : IDisposable
    {
        public ID3D11Buffer Buffer { get; }
        public ID3D11ShaderResourceView View { get; } = null!;
        public ID3D11UnorderedAccessView WriteView { get; } = null!;
        public int Count { get; }
        public GpuBuffer(ID3D11Device device, int count, int stride)
        {
            Count = count;
            Buffer = device.CreateBuffer(new BufferDescription { ByteWidth = checked((uint)(count * stride)),
                Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess,
                MiscFlags = ResourceOptionFlags.BufferStructured, StructureByteStride = (uint)stride });
            try { View = device.CreateShaderResourceView(Buffer); WriteView = device.CreateUnorderedAccessView(Buffer); }
            catch { View?.Dispose(); Buffer.Dispose(); throw; }
        }
        public void Dispose() { WriteView.Dispose(); View.Dispose(); Buffer.Dispose(); }
    }

    /// <summary>Опубликованный кадр: точные поле и карта масштабов и объём R32G32_Float для рендера.</summary>
    private sealed class Slot : IDisposable
    {
        public GpuBuffer Value { get; }
        public GpuBuffer Scale { get; }
        public ID3D11Texture3D Display { get; }
        public ID3D11ShaderResourceView DisplayView { get; }
        public ID3D11UnorderedAccessView DisplayWrite { get; }
        public Slot(ID3D11Device device, int side)
        {
            int cells = side * side * side;
            Value = new(device, cells, 4); Scale = new(device, cells, 4);
            Display = device.CreateTexture3D(new Texture3DDescription
            {
                Width = (uint)side, Height = (uint)side, Depth = (uint)side, MipLevels = 1, Format = Format.R32G32_Float,
                Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess
            });
            DisplayView = device.CreateShaderResourceView(Display);
            DisplayWrite = device.CreateUnorderedAccessView(Display);
        }
        public void Dispose() { DisplayWrite.Dispose(); DisplayView.Dispose(); Display.Dispose(); Scale.Dispose(); Value.Dispose(); }
    }
}
