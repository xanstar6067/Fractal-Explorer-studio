using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Models;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Опубликованный кадр моделирования: объём поля и карты масштабов для рендера и точная копия
/// поля для сохранения.
/// </summary>
public sealed record Turing3DVolume(Turing3DGpuSimulation Source, long Version, long Step, int Size, int Slot)
    : IGpuVolumeTicket<Turing3DGpuSimulation>;

/// <summary>
/// Многомасштабные узоры Тьюринга и классические двухкомпонентные реакции в кубе целиком на ГП;
/// общий цикл подачи шагов, слотов публикации и чтения поля —
/// <see cref="GpuVolumeSimulation{TSettings, TField, TVolume}"/>.
/// </summary>
public sealed class Turing3DGpuSimulation : GpuVolumeSimulation<Turing3DSettings, Turing3DField, Turing3DVolume>
{
    /// <summary>Наибольшая группа — икосаэдр с отражениями (120); запас до предела замыкания.</summary>
    private const int MaxGroupSize = 240;

    private readonly Slot?[] _slots = new Slot?[SlotCount];
    private GpuStructuredBuffer? _value, _scale, _nextValue, _nextScale, _activator, _inhibitor, _work, _temporary;
    private GpuStructuredBuffer? _u, _v, _nextU, _nextV;
    private GpuStructuredBuffer? _best, _range0, _range1, _group;
    private IReadOnlyList<(int Layer, int Radius, int Inhibitor, double Amount)> _layers = [];
    private int _groupSize = 1;

    public Turing3DGpuSimulation(Direct3DDeviceHost host, Turing3DSettings settings) : this(host, settings, false) { }

    private Turing3DGpuSimulation(Direct3DDeviceHost host, Turing3DSettings settings, bool gateHeld)
        : base(host, Turing3DComputeShader.CacheEntries, "Turing3D", "узора Тьюринга 3D", 48) =>
        Initialize(settings, gateHeld);

    /// <summary>Для рендера, который уже держит очередь устройства.</summary>
    internal static Turing3DGpuSimulation CreateLocked(Direct3DDeviceHost host, Turing3DSettings settings) =>
        new(host, settings, true);

    /// <summary>Число элементов группы симметрии, по которым повторяются шаг и кисть.</summary>
    public int SymmetryOrder => _groupSize;

    private protected override double FenceTimeoutSeconds => 30;
    private protected override string TimeoutMessage => "ГП не завершил шаги узора Тьюринга 3D. Попробуйте уменьшить сетку.";
    private protected override bool ViewRequiresCurrentFrame => false;

    /// <summary>Порция подачи: шаг дорог, поэтому на крупной сетке — по одному, на мелкой — до 16.</summary>
    private protected override int StepsPerSubmit
    {
        get { long n = Size; return (int)Math.Clamp(6_000_000 / (n * n * n * Math.Max(_layers.Count, 1)), 1, 16); }
    }

    private protected override void CreateResourcesLocked() => _group = new(Host.Device, MaxGroupSize * 4, 16);

    /// <summary>Новое поле — сохранённое или случайное по числу генератора — и правило к нему.</summary>
    private protected override void ResetCoreLocked(Turing3DSettings settings)
    {
        int size = settings.Size;
        if (_value is null || Settings.Size != size) AllocateFields(size);
        ConfigureLocked(settings);
        ID3D11DeviceContext context = Host.Context;
        if (settings.Field is { } field)
        {
            context.UpdateSubresource(field.Values, _value!.Buffer);
            context.UpdateSubresource(field.Scales.Select(scale => (float)scale).ToArray(), _scale!.Buffer);
            if (settings.Reaction.IsClassical) {
                context.UpdateSubresource(field.UValues, _u!.Buffer); context.UpdateSubresource(field.VValues, _v!.Buffer);
            }
            CurrentStep = field.Step;
        }
        else if (settings.Reaction.IsClassical)
        {
            int count = size * size * size; var u = new float[count]; var v = new float[count];
            var random = new Random(settings.Seed); var equilibrium = settings.Reaction.Equilibrium;
            for (int i = 0; i < count; i++) {
                u[i] = (float)(equilibrium.U * (1 + .05 * (random.NextDouble() * 2 - 1)));
                v[i] = (float)(equilibrium.V * (1 + .05 * (random.NextDouble() * 2 - 1)));
            }
            context.UpdateSubresource(u, _u!.Buffer); context.UpdateSubresource(v, _v!.Buffer);
            SymmetrizeReactionLocked(); ReactionDisplayLocked(); CurrentStep = 0;
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
            CurrentStep = 0;
        }
    }

    /// <summary>Новое правило для текущего поля: время и поле сохраняются, действует со следующего шага.</summary>
    public void Configure(Turing3DSettings settings)
    {
        settings.Validate();
        Host.Gate.Wait();
        try
        {
            ThrowIfDisposed();
            if (settings.Reaction.Model != Settings.Reaction.Model) throw new ArgumentException("Смена модели требует нового поля.");
            if (settings.Size != Settings.Size) throw new ArgumentException("Смена сетки требует переноса поля.");
            ConfigureLocked(settings);
        }
        finally { Host.Gate.Release(); }
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
        Host.Context.UpdateSubresource(packed, _group!.Buffer);
        // Display value of each scale: its rank among the enabled ones, spread over the palette.
        int enabled = _layers.Count;
        for (int i = 0; i < 8; i++) Parameters[20 + i] = 0;
        for (int rank = 0; rank < enabled; rank++)
            Parameters[20 + _layers[rank].Layer] = enabled > 1 ? rank / (float)(enabled - 1) : .5f;
    }

    /// <summary>Сферический мазок в долях ребра, повторённый по группе симметрии; время не идёт.</summary>
    public void Paint(double x, double y, double z, double radius, double strength, TuringBrush brush)
    {
        ValidateBrush(x, y, z, radius, strength);
        Host.Gate.Wait();
        try
        {
            ThrowIfDisposed();
            Set(0, Size, 0, (int)Settings.Boundary, 0);
            Set(1, 0, 0, 0, _groupSize);
            Set(2, (float)x, (float)y, (float)z, (float)radius);
            Set(3, (float)strength, (int)brush, BitConverter.Int32BitsToSingle(Settings.Seed), BitConverter.Int32BitsToSingle(unchecked((int)CurrentStep)));
            SetReactionParameters();
            DispatchVolume("Paint", outA: Settings.Reaction.IsClassical ? _u : _value);
            if (Settings.Reaction.IsClassical) ReactionDisplayLocked();
        }
        finally { Host.Gate.Release(); }
    }

    private protected override void PublishToSlotLocked(int index)
    {
        Slot slot = _slots[index]!;
        ID3D11DeviceContext context = Host.Context;
        context.CopyResource(slot.Value.Buffer, _value!.Buffer);
        context.CopyResource(slot.Scale.Buffer, _scale!.Buffer);
        if (Settings.Reaction.IsClassical) {
            context.CopyResource(slot.U.Buffer, _u!.Buffer); context.CopyResource(slot.V.Buffer, _v!.Buffer);
        }
        slot.Model = Settings.Reaction.Model;
        SetReactionParameters();
        Set(0, Size, 0, 0, 0);
        DispatchVolume("Publish", a: slot.Value, b: slot.Scale, display: slot.Display.WriteView);
    }

    private protected override Turing3DVolume CreateVolume(long version, int slot) => new(this, version, CurrentStep, Size, slot);

    /// <summary>Текстура опубликованного кадра (поле и масштаб).</summary>
    private protected override ID3D11ShaderResourceView DisplayView(int slot) => _slots[slot]!.Display.View;

    private protected override Turing3DField ReadSlotLocked(Turing3DVolume volume)
    {
        Slot slot = _slots[volume.Slot]!;
        return ReadLocked(slot.Value, slot.Scale, volume.Step, slot.Model, slot.U, slot.V);
    }

    private protected override Turing3DField ReadCurrentLocked() =>
        ReadLocked(_value!, _scale!, CurrentStep, Settings.Reaction.Model, _u!, _v!);

    private Turing3DField ReadLocked(GpuStructuredBuffer value, GpuStructuredBuffer scale, long step, TuringReactionModel model,
        GpuStructuredBuffer u, GpuStructuredBuffer v)
    {
        float[] values = ReadFloatsLocked(value.Buffer);
        float[] scales = ReadFloatsLocked(scale.Buffer);
        var map = new byte[scales.Length];
        for (int i = 0; i < map.Length; i++) map[i] = (byte)scales[i];
        return new(Size, step, values, map, true, model, model == TuringReactionModel.McCabe ? [] : ReadFloatsLocked(u.Buffer),
            model == TuringReactionModel.McCabe ? [] : ReadFloatsLocked(v.Buffer));
    }

    /// <summary>Один целый шаг: масштабы по очереди, затем сложение, симметрия и нормировка.</summary>
    private protected override void StepLocked()
    {
        if (Settings.Reaction.IsClassical) { EvolveReactionLocked(); return; }
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
        int remaining = groups; GpuStructuredBuffer range = _range0!, other = _range1!;
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
    /// на ось, все окна — внутри прохода. Результат оказывается в <paramref name="target"/>:
    /// проходы чередуют его с общим временным буфером, и при нечётном их числе они меняются
    /// ролями без копирования. Источник не перезаписывается, пока не прочитан целиком.
    /// </summary>
    private void Smooth(GpuStructuredBuffer source, ref GpuStructuredBuffer? target, int radius)
    {
        int[] boxes = TuringSimulation.GaussianBoxWidths(radius * .75).Select(width => width / 2).ToArray();
        if (boxes.All(box => box == 0))
        {
            if (!ReferenceEquals(source, target)) Host.Context.CopyResource(target!.Buffer, source.Buffer);
            return;
        }
        GpuStructuredBuffer current = source;
        uint n = (uint)Size;
        for (int axis = 0; axis < 3; axis++)
        {
            GpuStructuredBuffer next = ReferenceEquals(current, _temporary) ? target! : _temporary!;
            Set(0, Size, 0, (int)Settings.Boundary, axis);
            Set(7, boxes[0], boxes[1], boxes[2], 0);
            Dispatch("Blur", n, n, 1, a: current, outA: next);
            current = next;
        }
        if (ReferenceEquals(current, _temporary)) (target, _temporary) = (_temporary, target);
    }

    private void SetReactionParameters()
    {
        var r = Settings.Reaction; var equilibrium = r.Equilibrium;
        Set(8, (int)r.Model, (float)r.A, (float)r.B, 0);
        Set(9, 0, 0, (float)equilibrium.U, (float)equilibrium.V);
    }

    private void SymmetrizeReactionLocked()
    {
        if (_groupSize == 1 && Settings.Region == Turing3DRegion.Cube) return;
        Set(0, Size, 0, (int)TuringBoundary.Reflect, 0); Set(1, 0, 0, 0, _groupSize);
        Set(4, 0, 0, (int)Settings.Region, (float)Settings.ShellThickness);
        // Both channels use the same representative and trilinear interpolation.
        DispatchVolume("Symmetry", a: _u, b: _v, outA: _nextU, outB: _work);
        DispatchVolume("Symmetry", a: _v, b: _u, outA: _nextV, outB: _work);
        (_u, _nextU) = (_nextU, _u); (_v, _nextV) = (_nextV, _v);
    }

    private void ReactionDisplayLocked()
    {
        SetReactionParameters(); Set(0, Size, 0, 0, 0);
        DispatchCells("ReactionDisplay", a: _u, outA: _value, outB: _scale);
    }

    private void EvolveReactionLocked()
    {
        var integration = Settings.Reaction.Integration(Size, Turing3DSettings.ReferenceSize, Settings.DetailSize, Settings.EvolutionRate, 3);
        for (int sub = 0; sub < integration.Count; sub++)
        {
            SetReactionParameters();
            Parameters[35] = (float)integration.Dt;
            Parameters[36] = (float)(Settings.Reaction.DiffusionU * integration.DiffusionScale);
            Parameters[37] = (float)(Settings.Reaction.DiffusionV * integration.DiffusionScale);
            Set(0, Size, 0, (int)Settings.Boundary, 0);
            DispatchVolume("ReactionStep", a: _u, b: _v, outA: _nextU, outB: _nextV);
            (_u, _nextU) = (_nextU, _u); (_v, _nextV) = (_nextV, _v);
            SymmetrizeReactionLocked();
        }
        ReactionDisplayLocked();
    }

    private void Set(int index, float x, float y, float z, float w)
    {
        Parameters[index * 4] = x; Parameters[index * 4 + 1] = y;
        Parameters[index * 4 + 2] = z; Parameters[index * 4 + 3] = w;
    }

    private void DispatchCells(string entry, GpuStructuredBuffer? a = null, GpuStructuredBuffer? b = null,
        GpuStructuredBuffer? c = null, GpuStructuredBuffer? d = null,
        GpuStructuredBuffer? outA = null, GpuStructuredBuffer? outB = null, GpuStructuredBuffer? outD = null)
    {
        long cells = (long)Size * Size * Size;
        Dispatch(entry, (uint)((cells + Turing3DComputeShader.CellGroup - 1) / Turing3DComputeShader.CellGroup), 1, 1, a, b, c, d, outA, outB, outD);
    }

    private void DispatchVolume(string entry, GpuStructuredBuffer? a = null, GpuStructuredBuffer? b = null,
        GpuStructuredBuffer? outA = null, GpuStructuredBuffer? outB = null, ID3D11UnorderedAccessView? display = null)
    {
        uint n = (uint)Size;
        Dispatch(entry, (n + Turing3DComputeShader.VolumeX - 1) / Turing3DComputeShader.VolumeX,
            (n + Turing3DComputeShader.VolumeY - 1) / Turing3DComputeShader.VolumeY,
            (n + Turing3DComputeShader.VolumeZ - 1) / Turing3DComputeShader.VolumeZ, a, b, null, null, outA, outB, null, display);
    }

    private void Dispatch(string entry, uint x, uint y, uint z, GpuStructuredBuffer? a = null, GpuStructuredBuffer? b = null,
        GpuStructuredBuffer? c = null, GpuStructuredBuffer? d = null, GpuStructuredBuffer? outA = null,
        GpuStructuredBuffer? outB = null, GpuStructuredBuffer? outD = null, ID3D11UnorderedAccessView? display = null)
    {
        // Parameters change between almost all passes, so they are uploaded every time.
        ConstantsDirty = true;
        ID3D11DeviceContext context = BindLocked(entry);
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

    private void AllocateFields(int size)
    {
        DisposeFields();
        ID3D11Device device = Host.Device;
        int cells = size * size * size, groups = (cells + 255) / 256;
        _u = new(device, cells, 4); _v = new(device, cells, 4); _nextU = new(device, cells, 4); _nextV = new(device, cells, 4);
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

    private protected override void DisposeResourcesCore()
    {
        DisposeFields();
        _group?.Dispose(); _group = null;
    }

    private void DisposeFields()
    {
        foreach (var buffer in new[] { _u, _v, _nextU, _nextV, _value, _scale, _nextValue, _nextScale, _activator, _inhibitor, _work, _temporary, _best, _range0, _range1 })
            buffer?.Dispose();
        _u = _v = _nextU = _nextV = null;
        _value = _scale = _nextValue = _nextScale = _activator = _inhibitor = _work = _temporary = _best = _range0 = _range1 = null;
        for (int i = 0; i < _slots.Length; i++) { _slots[i]?.Dispose(); _slots[i] = null; }
    }

    /// <summary>Опубликованный кадр: точные поле и карта масштабов и объём R32G32_Float для рендера.</summary>
    private sealed class Slot : IDisposable
    {
        public Slot(ID3D11Device device, int side)
        {
            int cells = side * side * side;
            U = new(device, cells, 4); V = new(device, cells, 4);
            Value = new(device, cells, 4); Scale = new(device, cells, 4);
            Display = new(device, side, Format.R32G32_Float);
        }

        public TuringReactionModel Model { get; set; }
        public GpuStructuredBuffer U { get; }
        public GpuStructuredBuffer V { get; }
        public GpuStructuredBuffer Value { get; }
        public GpuStructuredBuffer Scale { get; }
        public GpuVolumeTexture Display { get; }

        public void Dispose() { Display.Dispose(); Scale.Dispose(); Value.Dispose(); U.Dispose(); V.Dispose(); }
    }
}
