using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using WpfColor = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Результат живого кадра: буфер и признак того, что кадр досчитан, а не уступил место движению
/// камеры.
/// </summary>
public readonly record struct Fractal3DPixels(byte[] Buffer, bool Completed);

/// <summary>
/// Трассировка лучей по дистанционной оценке на GPU (Direct3D 11 через Vortice). Кадр считается
/// горизонтальными полосами: так работает прогресс и отмена, а длинный экспорт не упирается в
/// сторожевой таймер драйвера (TDR), который снимает один слишком долгий вызов отрисовки.
/// Результат забирается обратно в оперативную память, поэтому окну не нужен D3D-интероп.
/// </summary>
public sealed partial class Fractal3DRenderer : IDisposable
{
    /// <summary>Сколько пикселей считается за одну отрисовку при качестве по умолчанию.</summary>
    private const int BaseStripBudget = 1_000_000;

    /// <summary>Как часто ожидание очереди к устройству оглядывается на отмену.</summary>
    private const int GateWaitMs = 20;

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Опорные цвета палитры кадра: тот же массив переиспользуется каждой полосой.</summary>
    private readonly float[] _palette = new float[Fractal3DPalette.MaxColors * 4];

    private readonly Dictionary<Fractal3DKind, ID3D11PixelShader> _pixelShaders = [];
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11Buffer? _constantBuffer;
    private ID3D11Buffer? _apollonianTreeBuffer;
    private int _apollonianGeneration = -1;
    private ID3D11Texture2D? _renderTarget;
    private ID3D11Texture2D? _stagingTexture;
    private ID3D11RenderTargetView? _renderTargetView;
    private ID3D11Texture2D? _probeTarget;
    private ID3D11Texture2D? _probeStaging;
    private ID3D11RenderTargetView? _probeView;
    private int _surfaceWidth;
    private int _surfaceHeight;
    private bool _disposed;
    private readonly bool _softwareRendering;

    /// <param name="softwareRendering">Use WARP for a GPU-independent diagnostic render.</param>
    public Fractal3DRenderer(bool softwareRendering = false) => _softwareRendering = softwareRendering;

    /// <summary>Разовый рендер во временном устройстве — для превью каталога и проверок.</summary>
    public static async Task<BitmapSource> RenderOnceAsync(
        Fractal3DState state, int width, int height, CancellationToken token)
    {
        using var renderer = new Fractal3DRenderer();
        return await renderer.RenderAsync(state, width, height, null, token);
    }

    public async Task<BitmapSource> RenderAsync(
        Fractal3DState state, int width, int height, IProgress<int>? progress, CancellationToken token)
    {
        int safeWidth = Math.Max(1, width);
        int safeHeight = Math.Max(1, height);
        Fractal3DPixels frame = await RenderPixelsAsync(state, safeWidth, safeHeight, null, progress, token);
        // Здесь отмена — это отказ от результата: превью сохранения и экспорт ждут кадр целиком.
        if (!frame.Completed) token.ThrowIfCancellationRequested();

        BitmapSource bitmap = BitmapSource.Create(safeWidth, safeHeight, 96, 96,
            PixelFormats.Bgra32, null, frame.Buffer, checked(safeWidth * 4));
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Кадр в готовый буфер BGRA: живое превью переписывает один и тот же массив и один и тот же
    /// <c>WriteableBitmap</c>, поэтому на каждом кадре движения не появляется нового мусора.
    /// Отменённый кадр не бросает исключение, а возвращается с <c>Completed = false</c>: движение
    /// камеры отменяет начатое уточнение постоянно, и это обычный ход, а не ошибка.
    /// </summary>
    public async Task<Fractal3DPixels> RenderPixelsAsync(
        Fractal3DState state, int width, int height, byte[]? destination,
        IProgress<int>? progress, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(state);
        int safeWidth = Math.Max(1, width);
        int safeHeight = Math.Max(1, height);
        int required = checked(safeWidth * safeHeight * 4);
        byte[] buffer = destination is not null && destination.Length >= required
            ? destination
            : new byte[required];
        Fractal3DState snapshot = state.Clone();

        // Задача запускается без токена: иначе отмена вернула бы отменённую задачу, и ожидание
        // снова стало бы исключением.
        bool completed = await Task.Run(
            () => TryRenderPixels(snapshot, safeWidth, safeHeight, buffer, progress, token),
            CancellationToken.None);
        return new Fractal3DPixels(buffer, completed);
    }

    /// <summary>
    /// Расстояние от камеры до поверхности вдоль луча через указанный пиксель кадра заданного
    /// размера; <see cref="double.NaN"/>, если луч ушёл в фон. Считается кадром 1×1: смещение
    /// полосы наводит единственный пиксель ровно на курсор.
    /// </summary>
    public async Task<double> ProbeDistanceAsync(
        Fractal3DState state, double pixelX, double pixelY, int width, int height, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(state);
        Fractal3DState snapshot = state.Clone();
        int safeWidth = Math.Max(1, width);
        int safeHeight = Math.Max(1, height);
        return await Task.Run(
            () => ProbeDistance(snapshot, pixelX, pixelY, safeWidth, safeHeight, token), token);
    }

    private double ProbeDistance(
        Fractal3DState state, double pixelX, double pixelY, int width, int height, CancellationToken token)
    {
        _gate.Wait(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureDevice();
            EnsureProbeSurface();
            ID3D11DeviceContext context = _context!;

            FrameConstants constants = BuildConstants(state, width, height, 0);
            constants.Resolution = new Vector4(width, height, (float)(pixelX - 0.5), (float)(pixelY - 0.5));
            constants.Probe = new Vector4(1, 0, 0, 0);
            WriteConstants(constants, state);
            EnsureApollonianTree(state);
            if (state.Kind == Fractal3DKind.Terrain) EnsureTerrain(state.Terrain, token);
            if (state.Kind == Fractal3DKind.Ifs3D) EnsureIfsVolume(state, token);

            context.OMSetRenderTargets(_probeView!);
            context.RSSetViewport(new Viewport(0, 0, 1, 1));
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            context.VSSetShader(_vertexShader!);
            context.PSSetShader(state.Kind == Fractal3DKind.Ifs3D ? GetIfsPixelShader() : GetPixelShader(state.Kind));
            if (state.Kind == Fractal3DKind.Ifs3D)
            {
                context.PSSetShaderResource(0, _ifsVolumeView!);
                context.PSSetSampler(0, GetIfsSampler());
            }
            context.PSSetConstantBuffer(0, _constantBuffer!);
            context.Draw(3, 0);
            context.CopyResource(_probeStaging!, _probeTarget!);

            MappedSubresource mapped = context.Map(_probeStaging!, 0, MapMode.Read);
            try
            {
                Span<byte> bytes = stackalloc byte[4];
                for (int index = 0; index < bytes.Length; index++)
                {
                    bytes[index] = Marshal.ReadByte(mapped.DataPointer, index);
                }
                float distance = BitConverter.ToSingle(bytes);
                return distance < 0 || !float.IsFinite(distance) ? double.NaN : distance;
            }
            finally
            {
                context.Unmap(_probeStaging!, 0);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <returns>
    /// Досчитан ли кадр. Отмена проверяется опросом, а не исключением: живое превью отменяет
    /// начатое уточнение при каждом движении камеры, и бросок на этом пути только мешал бы —
    /// в отладчике он останавливает выполнение на каждом повороте мыши.
    /// </returns>
    private bool TryRenderPixels(
        Fractal3DState state, int width, int height, byte[] result,
        IProgress<int>? progress, CancellationToken token)
    {
        int stripRows = ComputeStripRows(state, width, height);
        int strips = (height + stripRows - 1) / stripRows;

        for (int index = 0; index < strips; index++)
        {
            if (token.IsCancellationRequested) return false;
            while (!_gate.Wait(GateWaitMs))
            {
                if (token.IsCancellationRequested) return false;
            }
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                EnsureDevice();
                EnsureSurface(width, stripRows);
                int offsetY = index * stripRows;
                RenderStrip(state, width, height, offsetY, Math.Min(stripRows, height - offsetY), result, token);
            }
            catch (OperationCanceledException) when (state.Kind == Fractal3DKind.Terrain && token.IsCancellationRequested)
            {
                return false;
            }
            finally
            {
                _gate.Release();
            }
            progress?.Report((index + 1) * 100 / strips);
        }
        return true;
    }

    /// <summary>
    /// Высота полосы: бюджет пикселей уменьшается вместе с ростом стоимости пикселя, чтобы одна
    /// отрисовка оставалась заметно короче сторожевого таймера драйвера.
    /// </summary>
    private static int ComputeStripRows(Fractal3DState state, int width, int height)
    {
        if (state.Kind == Fractal3DKind.Terrain)
            return Math.Clamp(30_000 / Math.Max(width, 1), 1, height);
        if (state.Kind == Fractal3DKind.Ifs3D)
            return Math.Clamp(120_000 / Math.Max(width, 1), 8, height);
        double cost = Math.Max(0.25, state.MaxSteps / 160.0 * Math.Max(state.Iterations, 1) / 8.0);
        // Поиск ближайшей сферы обходит дерево, а не одну формулу: полосы короче,
        // чтобы экспорт не занимал GPU дольше сторожевого таймера драйвера.
        if (state.Kind == Fractal3DKind.ApollonianPacking) cost *= 5;
        int budget = (int)Math.Clamp(BaseStripBudget / cost, 40_000, 2_000_000);
        return Math.Clamp(budget / Math.Max(width, 1), 8, height);
    }

    private void RenderStrip(
        Fractal3DState state, int width, int height, int offsetY, int rows, byte[] destination,
        CancellationToken token)
    {
        ID3D11DeviceContext context = _context!;
        WriteConstants(BuildConstants(state, width, height, offsetY), state);
        EnsureApollonianTree(state);
        if (state.Kind == Fractal3DKind.Terrain) EnsureTerrain(state.Terrain, token);
        if (state.Kind == Fractal3DKind.Ifs3D) EnsureIfsVolume(state, token);

        context.OMSetRenderTargets(_renderTargetView!);
        context.RSSetViewport(new Viewport(0, 0, width, _surfaceHeight));
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_vertexShader!);
        context.PSSetShader(state.Kind == Fractal3DKind.Ifs3D ? GetIfsPixelShader() : GetPixelShader(state.Kind));
        if (state.Kind == Fractal3DKind.Ifs3D)
        {
            context.PSSetShaderResource(0, _ifsVolumeView!);
            context.PSSetSampler(0, GetIfsSampler());
        }
        context.PSSetConstantBuffer(0, _constantBuffer!);
        context.Draw(3, 0);

        context.CopyResource(_stagingTexture!, _renderTarget!);

        MappedSubresource mapped = context.Map(_stagingTexture!, 0, MapMode.Read);
        try
        {
            int rowBytes = checked(width * 4);
            for (int row = 0; row < rows; row++)
            {
                nint source = IntPtr.Add(mapped.DataPointer, (int)(row * mapped.RowPitch));
                Marshal.Copy(source, destination, checked((offsetY + row) * rowBytes), rowBytes);
            }
        }
        finally
        {
            context.Unmap(_stagingTexture!, 0);
        }
    }

    /// <summary>
    /// Пишет константы кадра и следом — палитру. Палитра идёт отдельной копией, а не полем
    /// структуры: массив фиксированного размера внутри структуры пришлось бы маршалить с
    /// выделением памяти на каждой полосе кадра.
    /// </summary>
    private void WriteConstants(FrameConstants constants, Fractal3DState state)
    {
        FillPalette(state.ResolvePalette(), _palette);
        ID3D11DeviceContext context = _context!;
        MappedSubresource mapped = context.Map(_constantBuffer!, MapMode.WriteDiscard);
        Marshal.StructureToPtr(constants, mapped.DataPointer, false);
        Marshal.Copy(_palette, 0, IntPtr.Add(mapped.DataPointer, FrameConstants.SizeInBytes), _palette.Length);
        context.Unmap(_constantBuffer!, 0);
    }

    /// <summary>Геометрия меняется только при смене числа поколений; камера и свет её не пересоздают.</summary>
    private void EnsureApollonianTree(Fractal3DState state)
    {
        if (state.Kind != Fractal3DKind.ApollonianPacking) return;
        int generation = Math.Clamp(state.Iterations, 1, ApollonianSpherePacking.MaxGeneration);
        if (_apollonianTreeBuffer is null)
        {
            _apollonianTreeBuffer = _device!.CreateBuffer(new BufferDescription
            {
                ByteWidth = ApollonianSpherePacking.MaxTreeNodes * 16,
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer,
                CPUAccessFlags = CpuAccessFlags.Write
            });
        }
        if (_apollonianGeneration != generation)
        {
            float[] tree = ApollonianSpherePacking.BuildTree(generation);
            MappedSubresource mapped = _context!.Map(_apollonianTreeBuffer, MapMode.WriteDiscard);
            Marshal.Copy(tree, 0, mapped.DataPointer, tree.Length);
            _context.Unmap(_apollonianTreeBuffer, 0);
            _apollonianGeneration = generation;
        }
        _context!.PSSetConstantBuffer(1, _apollonianTreeBuffer);
    }

    // Лениво, чтобы не зависеть от порядка инициализации статических полей каталога.
    private static readonly Lazy<Dictionary<Fractal3DKind, Vector3>> HomeCameraPositions = new(() =>
        Enum.GetValues<Fractal3DKind>().ToDictionary(
            kind => kind, kind => Fractal3DCamera.Position(Fractal3DCatalog.CreateDefaultState(kind))));

    /// <summary>
    /// Положение камеры в стартовом виде. Шейдер сравнивает расстояние от него до поверхности
    /// с расстоянием от текущей камеры и во столько же раз сжимает шкалу тумана, окраски по
    /// глубине и затенения складок: фрактал самоподобен, и при подъезде вид должен выглядеть
    /// как стартовый в уменьшенном масштабе. Точка наблюдения для этого не годится — колесо
    /// переносит её на поверхность под курсором, и туман скакал вслед за курсором.
    /// </summary>
    internal static Vector3 HomeCameraPosition(Fractal3DKind kind) =>
        HomeCameraPositions.Value.TryGetValue(kind, out Vector3 position) ? position : new Vector3(0, 0, 3);

    private static FrameConstants BuildConstants(Fractal3DState state, int width, int height, int offsetY)
    {
        Fractal3DCameraBasis camera = Fractal3DCamera.Build(state);
        Vector3 light = Fractal3DCamera.LightDirection(state);
        Fractal3DPalette palette = state.ResolvePalette();

        (float shapeX, float shapeY, float shapeZ) = state.Kind switch
        {
            Fractal3DKind.Terrain => ((float)state.Terrain.Size, (float)state.Terrain.Height, state.Terrain.Resolution),
            Fractal3DKind.Mandelbox => (
                (float)state.BoxScale,
                (float)(state.BoxMinRadius * state.BoxMinRadius),
                (float)state.BoxFoldingLimit),
            Fractal3DKind.BulbBoxHybrid => (
                (float)state.Power,
                (float)(state.BoxMinRadius * state.BoxMinRadius),
                (float)state.BoxFoldingLimit),
            Fractal3DKind.SierpinskiTetrahedron => ((float)state.SierpinskiScale, 0f, 0f),
            Fractal3DKind.Vicsek or Fractal3DKind.CantorDust => ((float)state.CubeThickness, 0f, 0f),
            Fractal3DKind.BurningShip or Fractal3DKind.BurningShipJulia =>
                ((float)state.Power, (float)state.BurningShipFormula, 0f),
            _ => ((float)state.Power, 0f, 0f)
        };

        return new FrameConstants
        {
            Resolution = new Vector4(width, height, 0, offsetY),
            CameraPosition = new Vector4(camera.Position, camera.FieldOfViewScale),
            CameraRight = new Vector4(camera.Right, 0),
            CameraUp = new Vector4(camera.Up, 0),
            CameraForward = new Vector4(camera.Forward, 0),
            March = new Vector4(
                Math.Clamp(state.MaxSteps, 16, 1024),
                (float)Math.Clamp(state.Detail, 0.05, 8),
                (float)Math.Clamp(state.MaxDistance, 1, 1000),
                Math.Clamp(state.Iterations, 1, 64)),
            ShapeA = new Vector4(shapeX, shapeY, shapeZ, (float)Math.Max(state.Bailout, 1.0001)),
            ShapeB = state.Kind == Fractal3DKind.BulbBoxHybrid
                ? new Vector4((float)state.BoxScale, (float)Math.Clamp(state.HybridMix, 0, 1),
                    Math.Clamp(state.HybridBulbSteps, 1, 6), Math.Clamp(state.HybridBoxSteps, 1, 6))
                : new Vector4((float)state.JuliaCX, (float)state.JuliaCY, (float)state.JuliaCZ, (float)state.JuliaCW),
            ShapeC = new Vector4(
                (float)state.QuaternionSlice,
                (float)Math.Clamp(state.ColorScale, 0.01, 100),
                (float)state.ColorOffset,
                state.Kind == Fractal3DKind.BulbBoxHybrid ? (float)state.HybridOrder : 0),
            BoxInversion = new Vector4(
                (float)(Enum.IsDefined(state.BoxInversionShape) ? state.BoxInversionShape : BoxInversionShape.Sphere),
                (float)Math.Clamp(state.BoxInversionStretch, 0.5, 2),
                (float)Math.Clamp(state.BoxInversionPower, 2, 16), 0),
            Light = new Vector4(light, (float)Math.Clamp(state.Specular, 0, 4)),
            Surface = ToLinear(state.SurfaceColor, (float)Math.Clamp(state.AoStrength, 0, 1)),
            BackgroundTop = ToLinear(state.BackgroundTop),
            BackgroundBottom = ToLinear(state.BackgroundBottom),
            Flags = new Vector4(
                (int)state.ColoringMode,
                state.SoftShadows ? (float)Math.Clamp(state.ShadowSharpness, 1, 128) : 0,
                state.AmbientOcclusion ? 1 : 0,
                (float)Math.Clamp(state.Ambient, 0, 2)),
            Probe = Vector4.Zero,
            PickerMarker = state.PickerMarker,
            Style = new Vector4(
                (int)state.ShadingStyle,
                (float)Math.Clamp(state.EffectStrength, 0, 8),
                (float)Math.Clamp(state.SkyLightMix, 0, 1),
                0),
            LightColor = ToLinear(state.LightColor),
            PaletteInfo = new Vector4(
                Math.Clamp(palette.Colors.Count, 1, Fractal3DPalette.MaxColors),
                (int)state.ColorRepeat,
                palette.IsGradient ? 0 : 1,
                (float)Math.Clamp(palette.Gamma, 0.05, 8)),
            HomeCamera = new Vector4(HomeCameraPosition(state.Kind), 0)
        };
    }

    /// <summary>
    /// Опорные цвета палитры в линейном пространстве — хвост буфера констант. Пустая палитра
    /// заменяется белым: чёрный кадр хуже объясняет, что цвета кончились.
    /// </summary>
    private static void FillPalette(Fractal3DPalette palette, float[] destination)
    {
        Array.Clear(destination);
        int count = Math.Clamp(palette.Colors.Count, 0, Fractal3DPalette.MaxColors);
        if (count == 0)
        {
            destination[0] = destination[1] = destination[2] = destination[3] = 1;
            return;
        }
        for (int index = 0; index < count; index++)
        {
            Vector4 color = ToLinear(palette.Colors[index]);
            destination[index * 4] = color.X;
            destination[index * 4 + 1] = color.Y;
            destination[index * 4 + 2] = color.Z;
            destination[index * 4 + 3] = color.W;
        }
    }

    /// <summary>Цвет интерфейса — sRGB; освещение считается в линейном пространстве.</summary>
    private static Vector4 ToLinear(WpfColor color, float alpha = 1) =>
        new(SrgbToLinear(color.R), SrgbToLinear(color.G), SrgbToLinear(color.B), alpha);

    private static float SrgbToLinear(byte value)
    {
        float channel = value / 255f;
        return channel <= 0.04045f ? channel / 12.92f : MathF.Pow((channel + 0.055f) / 1.055f, 2.4f);
    }

    private ID3D11PixelShader GetPixelShader(Fractal3DKind kind)
    {
        if (_pixelShaders.TryGetValue(kind, out ID3D11PixelShader? shader)) return shader;
        ReadOnlyMemory<byte> bytecode = Compile(PixelShaderEntry(kind));
        shader = _device!.CreatePixelShader(bytecode.Span);
        _pixelShaders[kind] = shader;
        return shader;
    }

    private static ShaderCacheEntry PixelShaderEntry(Fractal3DKind kind) =>
        new($"fractal3d-{kind}-pixel", Fractal3DShader.Build(kind), "PSMain", "ps_5_0");

    private static ShaderCacheEntry VertexShaderEntry() =>
        new("fractal3d-vertex", Fractal3DShader.Build(Fractal3DKind.Mandelbulb), "VSMain", "vs_5_0");

    private static ShaderCacheEntry IfsPixelShaderEntry() =>
        new("ifs3d-pixel", Ifs3DShader.Source, "PSMain", "ps_5_0");

    private static ReadOnlyMemory<byte> Compile(ShaderCacheEntry entry) =>
        ShaderBytecodeCache.GetOrCompile(entry, CompileUncached);

    /// <summary>Перекомпилирует и заменяет все шейдеры 3D-режимов; вызывать в фоновом потоке.</summary>
    public static void RebuildShaderCache(Action<int, int, string>? progress = null)
    {
        var entries = new List<ShaderCacheEntry> { VertexShaderEntry() };
        foreach (Fractal3DKind kind in Enum.GetValues<Fractal3DKind>())
            if (kind != Fractal3DKind.Ifs3D) entries.Add(PixelShaderEntry(kind));
        entries.Add(IfsPixelShaderEntry());
        ShaderBytecodeCache.Rebuild(entries, CompileUncached, progress);
    }

    private static ReadOnlyMemory<byte> CompileUncached(ShaderCacheEntry entry)
    {
        try
        {
            return Compiler.Compile(entry.Source, entry.EntryPoint, "Fractal3D", entry.Profile);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Не удалось скомпилировать шейдер трёхмерного фрактала: {exception.Message}", exception);
        }
    }

    private void EnsureDevice()
    {
        if (_device is not null) return;

        if (!_softwareRendering)
        {
            // A null hardware adapter selects adapter 0, often the integrated GPU on laptops.
            // Rendering reads pixels back into RAM, so the dedicated adapter needs no WPF interop.
            using IDXGIFactory1? factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            var adapters = new List<IDXGIAdapter1>();
            try
            {
                for (uint index = 0; factory is not null && factory.EnumAdapters1(index, out IDXGIAdapter1? adapter).Success; index++)
                    if (adapter is not null) adapters.Add(adapter);
                foreach (IDXGIAdapter1 adapter in adapters.OrderByDescending(candidate => candidate.Description1.DedicatedVideoMemory))
                {
                    if (!D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                            [FeatureLevel.Level_11_0], out ID3D11Device? preferredDevice,
                            out ID3D11DeviceContext? preferredContext).Success) continue;
                    _device = preferredDevice;
                    _context = preferredContext;
                    break;
                }
            }
            finally { foreach (IDXGIAdapter1 adapter in adapters) adapter.Dispose(); }
        }

        ReadOnlySpan<DriverType> drivers = _softwareRendering
            ? [DriverType.Warp]
            : [DriverType.Hardware, DriverType.Warp];
        foreach (DriverType driver in _device is null ? drivers : [])
        {
            if (D3D11.D3D11CreateDevice(null, driver, DeviceCreationFlags.BgraSupport,
                    [FeatureLevel.Level_11_0], out ID3D11Device? device, out ID3D11DeviceContext? context).Success)
            {
                _device = device;
                _context = context;
                break;
            }
        }
        if (_device is null)
        {
            throw new InvalidOperationException(
                "Не удалось создать устройство Direct3D 11. Трёхмерные фракталы считаются на видеокарте, " +
                "поэтому нужен драйвер с поддержкой Direct3D 11 (Feature Level 11_0).");
        }

        _vertexShader = _device.CreateVertexShader(
            Compile(VertexShaderEntry()).Span);
        _constantBuffer = _device.CreateBuffer(new BufferDescription
        {
            ByteWidth = FrameConstants.BufferSizeInBytes,
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ConstantBuffer,
            CPUAccessFlags = CpuAccessFlags.Write
        });
    }

    private void EnsureSurface(int width, int height)
    {
        if (_renderTarget is not null && _surfaceWidth == width && _surfaceHeight == height) return;

        _renderTargetView?.Dispose();
        _renderTarget?.Dispose();
        _stagingTexture?.Dispose();
        _surfaceWidth = width;
        _surfaceHeight = height;

        var description = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget
        };
        _renderTarget = _device!.CreateTexture2D(description);

        description.Usage = ResourceUsage.Staging;
        description.BindFlags = BindFlags.None;
        description.CPUAccessFlags = CpuAccessFlags.Read;
        _stagingTexture = _device.CreateTexture2D(description);

        _renderTargetView = _device.CreateRenderTargetView(_renderTarget);
    }

    /// <summary>Отдельная цель 1×1 для зонда: она не вытесняет текстуры текущего кадра.</summary>
    private void EnsureProbeSurface()
    {
        if (_probeTarget is not null) return;

        var description = new Texture2DDescription
        {
            Width = 1,
            Height = 1,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget
        };
        _probeTarget = _device!.CreateTexture2D(description);

        description.Usage = ResourceUsage.Staging;
        description.BindFlags = BindFlags.None;
        description.CPUAccessFlags = CpuAccessFlags.Read;
        _probeStaging = _device.CreateTexture2D(description);

        _probeView = _device.CreateRenderTargetView(_probeTarget);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Рендер уже отменён вызывающим кодом; ждём выхода из полосы, чтобы не освободить
        // ресурсы под работающей отрисовкой.
        _gate.Wait(TimeSpan.FromSeconds(10));
        _renderTargetView?.Dispose();
        _renderTarget?.Dispose();
        _stagingTexture?.Dispose();
        _probeView?.Dispose();
        _probeTarget?.Dispose();
        _probeStaging?.Dispose();
        _constantBuffer?.Dispose();
        DisposeIfsResources();
        DisposeTerrainResources();
        _apollonianTreeBuffer?.Dispose();
        foreach (ID3D11PixelShader shader in _pixelShaders.Values) shader.Dispose();
        _pixelShaders.Clear();
        _vertexShader?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
        _gate.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameConstants
    {
        /// <summary>Размер самой структуры; следом за ней в буфер дописывается палитра.</summary>
        public const int SizeInBytes = 21 * 16;

        /// <summary>Полный размер буфера констант: структура плюс опорные цвета палитры.</summary>
        public const int BufferSizeInBytes = SizeInBytes + Fractal3DPalette.MaxColors * 16;

        public Vector4 Resolution;
        public Vector4 CameraPosition;
        public Vector4 CameraRight;
        public Vector4 CameraUp;
        public Vector4 CameraForward;
        public Vector4 March;
        public Vector4 ShapeA;
        public Vector4 ShapeB;
        public Vector4 ShapeC;
        public Vector4 Light;
        public Vector4 Surface;
        public Vector4 BackgroundTop;
        public Vector4 BackgroundBottom;
        public Vector4 Flags;
        public Vector4 Probe;
        public Vector4 PickerMarker;
        public Vector4 Style;
        public Vector4 LightColor;
        public Vector4 PaletteInfo;
        public Vector4 HomeCamera;
        public Vector4 BoxInversion;
    }
}
