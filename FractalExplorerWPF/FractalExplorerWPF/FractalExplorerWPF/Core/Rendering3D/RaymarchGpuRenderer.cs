using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Экспериментальный минимальный движок: raymarching Mandelbulb в пиксельном шейдере D3D11,
/// результат читается обратно на CPU (без D3DImage/D3D9-интеропа) — сделан только чтобы
/// оценить дополнительный вес зависимости Vortice.Windows, не для продакшена.
/// </summary>
public sealed class RaymarchGpuRenderer : IDisposable
{
    private const string ShaderSource = """
        struct PSInput
        {
            float4 position : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        cbuffer FrameConstants : register(b0)
        {
            float2 Resolution;
            float Time;
            float Padding;
        };

        PSInput VSMain(uint id : SV_VertexID)
        {
            float2 uv = float2((id << 1) & 2, id & 2);
            PSInput output;
            output.position = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
            output.uv = uv;
            return output;
        }

        float MandelbulbDE(float3 pos)
        {
            const float power = 8.0;
            float3 z = pos;
            float dr = 1.0;
            float r = 0.0;
            [loop]
            for (int i = 0; i < 8; i++)
            {
                r = length(z);
                if (r > 2.0) break;

                float theta = acos(z.z / r);
                float phi = atan2(z.y, z.x);
                dr = pow(r, power - 1.0) * power * dr + 1.0;

                float zr = pow(r, power);
                theta *= power;
                phi *= power;

                z = zr * float3(sin(theta) * cos(phi), sin(theta) * sin(phi), cos(theta));
                z += pos;
            }
            return 0.5 * log(r) * r / dr;
        }

        float3 EstimateNormal(float3 p)
        {
            const float h = 0.0005;
            float2 k = float2(1, -1);
            return normalize(
                k.xyy * MandelbulbDE(p + k.xyy * h) +
                k.yyx * MandelbulbDE(p + k.yyx * h) +
                k.yxy * MandelbulbDE(p + k.yxy * h) +
                k.xxx * MandelbulbDE(p + k.xxx * h));
        }

        float4 PSMain(PSInput input) : SV_TARGET
        {
            float2 uv = (input.uv * 2.0 - 1.0);
            uv.x *= Resolution.x / Resolution.y;

            float angle = Time * 0.25;
            float3 camPos = float3(sin(angle) * 3.0, 1.2, cos(angle) * 3.0);
            float3 forward = normalize(-camPos);
            float3 right = normalize(cross(float3(0, 1, 0), forward));
            float3 up = cross(forward, right);

            float3 rayDir = normalize(forward * 1.5 + right * uv.x + up * uv.y);
            float3 rayPos = camPos;

            float totalDist = 0.0;
            bool hit = false;
            [loop]
            for (int i = 0; i < 96; i++)
            {
                float dist = MandelbulbDE(rayPos);
                if (dist < 0.0015)
                {
                    hit = true;
                    break;
                }
                totalDist += dist;
                rayPos += rayDir * dist;
                if (totalDist > 8.0) break;
            }

            if (!hit)
            {
                float t = 0.5 * (rayDir.y + 1.0);
                float3 skyColor = lerp(float3(0.05, 0.05, 0.08), float3(0.12, 0.13, 0.2), t);
                return float4(skyColor, 1.0);
            }

            float3 normal = EstimateNormal(rayPos);
            float3 lightDir = normalize(float3(0.6, 0.8, 0.4));
            float diffuse = saturate(dot(normal, lightDir));
            float3 baseColor = 0.5 + 0.5 * cos(float3(0.0, 0.6, 1.0) + totalDist * 0.6);
            float3 color = baseColor * (0.15 + 0.85 * diffuse);
            return float4(color, 1.0);
        }
        """;

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _pixelShader;
    private readonly ID3D11Buffer _constantBuffer;

    private ID3D11Texture2D? _renderTarget;
    private ID3D11Texture2D? _stagingTexture;
    private ID3D11RenderTargetView? _renderTargetView;
    private int _width;
    private int _height;

    public RaymarchGpuRenderer()
    {
        var creationFlags = DeviceCreationFlags.BgraSupport;
#if DEBUG
        creationFlags |= DeviceCreationFlags.Debug;
#endif
        D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            creationFlags,
            [FeatureLevel.Level_11_0],
            out _device!,
            out _context!).CheckError();

        var vertexBlob = Compiler.Compile(ShaderSource, "VSMain", "Raymarch", "vs_5_0");
        var pixelBlob = Compiler.Compile(ShaderSource, "PSMain", "Raymarch", "ps_5_0");

        _vertexShader = _device.CreateVertexShader(vertexBlob.Span);
        _pixelShader = _device.CreatePixelShader(pixelBlob.Span);

        _constantBuffer = _device.CreateBuffer(new BufferDescription
        {
            ByteWidth = 16,
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ConstantBuffer,
            CPUAccessFlags = CpuAccessFlags.Write
        });
    }

    /// <summary>
    /// Рендерит один кадр в BGRA32 буфер размера <paramref name="width"/>x<paramref name="height"/>.
    /// </summary>
    public byte[] RenderFrame(int width, int height, float time)
    {
        EnsureRenderTarget(width, height);

        var constants = new FrameConstants(width, height, time);
        var mapped = _context.Map(_constantBuffer, MapMode.WriteDiscard);
        Marshal.StructureToPtr(constants, mapped.DataPointer, false);
        _context.Unmap(_constantBuffer, 0);

        _context.OMSetRenderTargets(_renderTargetView);
        _context.RSSetViewport(new Vortice.Mathematics.Viewport(0, 0, width, height));
        _context.ClearRenderTargetView(_renderTargetView!, new Vortice.Mathematics.Color4(0, 0, 0, 1));

        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertexShader);
        _context.PSSetShader(_pixelShader);
        _context.PSSetConstantBuffer(0, _constantBuffer);
        _context.Draw(3, 0);

        _context.CopyResource(_stagingTexture!, _renderTarget!);

        var mappedStaging = _context.Map(_stagingTexture!, 0, MapMode.Read);
        try
        {
            var rowBytes = width * 4;
            var result = new byte[rowBytes * height];
            for (var row = 0; row < height; row++)
            {
                var srcPtr = IntPtr.Add(mappedStaging.DataPointer, (int)(row * mappedStaging.RowPitch));
                Marshal.Copy(srcPtr, result, row * rowBytes, rowBytes);
            }
            return result;
        }
        finally
        {
            _context.Unmap(_stagingTexture!, 0);
        }
    }

    private void EnsureRenderTarget(int width, int height)
    {
        if (_renderTarget != null && _width == width && _height == height)
        {
            return;
        }

        _renderTargetView?.Dispose();
        _renderTarget?.Dispose();
        _stagingTexture?.Dispose();

        _width = width;
        _height = height;

        _renderTarget = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget
        });

        _stagingTexture = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read
        });

        _renderTargetView = _device.CreateRenderTargetView(_renderTarget);
    }

    public void Dispose()
    {
        _renderTargetView?.Dispose();
        _renderTarget?.Dispose();
        _stagingTexture?.Dispose();
        _constantBuffer.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();
        _context.Dispose();
        _device.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct FrameConstants(int width, int height, float time)
    {
        public readonly float ResolutionX = width;
        public readonly float ResolutionY = height;
        public readonly float Time = time;
        public readonly float Padding = 0;
    }
}
