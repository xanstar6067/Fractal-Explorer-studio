using System.Runtime.InteropServices;
using FractalExplorerWPF.Models;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    private ID3D11PixelShader? _ifsPixelShader;
    private ID3D11SamplerState? _ifsSampler;
    private ID3D11Texture3D? _ifsVolumeTexture;
    private ID3D11ShaderResourceView? _ifsVolumeView;
    private Fractal3DState? _ifsVolumeState;

    private ID3D11PixelShader GetIfsPixelShader()
    {
        if (_ifsPixelShader is not null) return _ifsPixelShader;
        _ifsPixelShader = _device!.CreatePixelShader(Compile(Ifs3DShader.Source, "PSMain", "ps_5_0").Span);
        return _ifsPixelShader;
    }

    private ID3D11SamplerState GetIfsSampler() =>
        _ifsSampler ??= _device!.CreateSamplerState(
            new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp));

    private void EnsureIfsVolume(Fractal3DState state, CancellationToken token)
    {
        if (_ifsVolumeState is not null && SameIfsGeometry(_ifsVolumeState, state)) return;
        byte[] voxels = Ifs3DVolume.Build(state, token);
        token.ThrowIfCancellationRequested();

        if (_ifsVolumeTexture is null)
        {
            _ifsVolumeTexture = _device!.CreateTexture3D(new Texture3DDescription
            {
                Width = Ifs3DVolume.Side,
                Height = Ifs3DVolume.Side,
                Depth = Ifs3DVolume.Side,
                MipLevels = 1,
                Format = Format.R8_UNorm,
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ShaderResource,
                CPUAccessFlags = CpuAccessFlags.Write
            });
            _ifsVolumeView = _device.CreateShaderResourceView(_ifsVolumeTexture);
        }

        ID3D11DeviceContext context = _context!;
        MappedSubresource mapped = context.Map(_ifsVolumeTexture, 0, MapMode.WriteDiscard);
        try
        {
            int side = Ifs3DVolume.Side;
            if (mapped.RowPitch == side && mapped.DepthPitch == side * side)
            {
                Marshal.Copy(voxels, 0, mapped.DataPointer, voxels.Length);
            }
            else
            {
                for (int z = 0; z < side; z++)
                for (int y = 0; y < side; y++)
                {
                    nint destination = IntPtr.Add(mapped.DataPointer, checked((int)(z * mapped.DepthPitch + y * mapped.RowPitch)));
                    Marshal.Copy(voxels, (z * side + y) * side, destination, side);
                }
            }
        }
        finally { context.Unmap(_ifsVolumeTexture, 0); }
        _ifsVolumeState = state.Clone();
    }

    private static bool SameIfsGeometry(Fractal3DState first, Fractal3DState second)
    {
        if (first.Iterations != second.Iterations || first.IfsTransforms.Count != second.IfsTransforms.Count)
            return false;
        for (int i = 0; i < first.IfsTransforms.Count; i++)
        {
            Ifs3DTransform a = first.IfsTransforms[i], b = second.IfsTransforms[i];
            if (a.M11 != b.M11 || a.M12 != b.M12 || a.M13 != b.M13 ||
                a.M21 != b.M21 || a.M22 != b.M22 || a.M23 != b.M23 ||
                a.M31 != b.M31 || a.M32 != b.M32 || a.M33 != b.M33 ||
                a.Tx != b.Tx || a.Ty != b.Ty || a.Tz != b.Tz || a.Probability != b.Probability)
                return false;
        }
        return true;
    }

    private void DisposeIfsResources()
    {
        _ifsVolumeView?.Dispose();
        _ifsVolumeTexture?.Dispose();
        _ifsPixelShader?.Dispose();
        _ifsSampler?.Dispose();
        _ifsVolumeState = null;
    }
}
