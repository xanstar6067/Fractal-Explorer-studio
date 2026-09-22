using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Models;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using WpfColor = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Трассировка лучей по дистанционной оценке на GPU (Direct3D 11 через Vortice). Кадр считается
/// горизонтальными полосами: так работает прогресс и отмена, а длинный экспорт не упирается в
/// сторожевой таймер драйвера (TDR), который снимает один слишком долгий вызов отрисовки.
/// Результат забирается обратно в оперативную память, поэтому окну не нужен D3D-интероп.
/// </summary>
public sealed class Fractal3DRenderer : IDisposable
{
    /// <summary>Сколько пикселей считается за одну отрисовку при качестве по умолчанию.</summary>
    private const int BaseStripBudget = 1_000_000;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Fractal3DKind, ID3D11PixelShader> _pixelShaders = [];
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11Buffer? _constantBuffer;
    private ID3D11Texture2D? _renderTarget;
    private ID3D11Texture2D? _stagingTexture;
    private ID3D11RenderTargetView? _renderTargetView;
    private int _surfaceWidth;
    private int _surfaceHeight;
    private bool _disposed;

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
        ArgumentNullException.ThrowIfNull(state);
        int safeWidth = Math.Max(1, width);
        int safeHeight = Math.Max(1, height);
        Fractal3DState snapshot = state.Clone();

        byte[] pixels = await Task.Run(
            () => RenderPixels(snapshot, safeWidth, safeHeight, progress, token), token);

        BitmapSource bitmap = BitmapSource.Create(safeWidth, safeHeight, 96, 96,
            PixelFormats.Bgra32, null, pixels, checked(safeWidth * 4));
        bitmap.Freeze();
        return bitmap;
    }

    private byte[] RenderPixels(
        Fractal3DState state, int width, int height, IProgress<int>? progress, CancellationToken token)
    {
        byte[] result = new byte[checked(width * height * 4)];
        int stripRows = ComputeStripRows(state, width, height);
        int strips = (height + stripRows - 1) / stripRows;

        for (int index = 0; index < strips; index++)
        {
            token.ThrowIfCancellationRequested();
            _gate.Wait(token);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                EnsureDevice();
                EnsureSurface(width, stripRows);
                int offsetY = index * stripRows;
                RenderStrip(state, width, height, offsetY, Math.Min(stripRows, height - offsetY), result);
            }
            finally
            {
                _gate.Release();
            }
            progress?.Report((index + 1) * 100 / strips);
        }
        return result;
    }

    /// <summary>
    /// Высота полосы: бюджет пикселей уменьшается вместе с ростом стоимости пикселя, чтобы одна
    /// отрисовка оставалась заметно короче сторожевого таймера драйвера.
    /// </summary>
    private static int ComputeStripRows(Fractal3DState state, int width, int height)
    {
        double cost = Math.Max(0.25, state.MaxSteps / 160.0 * Math.Max(state.Iterations, 1) / 8.0);
        int budget = (int)Math.Clamp(BaseStripBudget / cost, 100_000, 2_000_000);
        return Math.Clamp(budget / Math.Max(width, 1), 8, height);
    }

    private void RenderStrip(
        Fractal3DState state, int width, int height, int offsetY, int rows, byte[] destination)
    {
        ID3D11DeviceContext context = _context!;
        FrameConstants constants = BuildConstants(state, width, height, offsetY);

        MappedSubresource mappedConstants = context.Map(_constantBuffer!, MapMode.WriteDiscard);
        Marshal.StructureToPtr(constants, mappedConstants.DataPointer, false);
        context.Unmap(_constantBuffer!, 0);

        context.OMSetRenderTargets(_renderTargetView!);
        context.RSSetViewport(new Viewport(0, 0, width, _surfaceHeight));
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_vertexShader!);
        context.PSSetShader(GetPixelShader(state.Kind));
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

    private static FrameConstants BuildConstants(Fractal3DState state, int width, int height, int offsetY)
    {
        Fractal3DCameraBasis camera = Fractal3DCamera.Build(state);
        Vector3 light = Fractal3DCamera.LightDirection(state);

        (float shapeX, float shapeY, float shapeZ) = state.Kind switch
        {
            Fractal3DKind.Mandelbox => (
                (float)state.BoxScale,
                (float)(state.BoxMinRadius * state.BoxMinRadius),
                (float)state.BoxFoldingLimit),
            Fractal3DKind.SierpinskiTetrahedron => ((float)state.SierpinskiScale, 0f, 0f),
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
            ShapeB = new Vector4((float)state.JuliaCX, (float)state.JuliaCY, (float)state.JuliaCZ, (float)state.JuliaCW),
            ShapeC = new Vector4(
                (float)state.QuaternionSlice,
                (float)Math.Clamp(state.ColorScale, 0.01, 100),
                (float)state.ColorOffset,
                0),
            Light = new Vector4(light, (float)Math.Clamp(state.Specular, 0, 4)),
            Surface = ToLinear(state.SurfaceColor, (float)Math.Clamp(state.AoStrength, 0, 1)),
            ColorA = ToLinear(state.ColorA),
            ColorB = ToLinear(state.ColorB),
            BackgroundTop = ToLinear(state.BackgroundTop),
            BackgroundBottom = ToLinear(state.BackgroundBottom),
            Flags = new Vector4(
                (int)state.ColoringMode,
                state.SoftShadows ? (float)Math.Clamp(state.ShadowSharpness, 1, 128) : 0,
                state.AmbientOcclusion ? 1 : 0,
                (float)Math.Clamp(state.Ambient, 0, 2))
        };
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
        ReadOnlyMemory<byte> bytecode = Compile(Fractal3DShader.Build(kind), "PSMain", "ps_5_0");
        shader = _device!.CreatePixelShader(bytecode.Span);
        _pixelShaders[kind] = shader;
        return shader;
    }

    private static ReadOnlyMemory<byte> Compile(string source, string entryPoint, string profile)
    {
        try
        {
            return Compiler.Compile(source, entryPoint, "Fractal3D", profile);
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

        foreach (DriverType driver in (ReadOnlySpan<DriverType>)[DriverType.Hardware, DriverType.Warp])
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
            Compile(Fractal3DShader.Build(Fractal3DKind.Mandelbulb), "VSMain", "vs_5_0").Span);
        _constantBuffer = _device.CreateBuffer(new BufferDescription
        {
            ByteWidth = FrameConstants.SizeInBytes,
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
        _constantBuffer?.Dispose();
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
        public const int SizeInBytes = 16 * 16;

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
        public Vector4 ColorA;
        public Vector4 ColorB;
        public Vector4 BackgroundTop;
        public Vector4 BackgroundBottom;
        public Vector4 Flags;
    }
}
