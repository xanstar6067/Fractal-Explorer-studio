using System.Runtime.InteropServices;
using FractalExplorerWPF.Models;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    private TerrainSettings? _terrainSettings;
    private ID3D11Texture2D? _terrainTexture;
    private ID3D11ShaderResourceView? _terrainView;

    private void EnsureTerrain(TerrainSettings settings, CancellationToken token)
    {
        if (_terrainSettings != settings)
        {
            float[] heights = TerrainHeightField.Build(settings, token);
            token.ThrowIfCancellationRequested();
            if (_terrainTexture is null || _terrainSettings?.Resolution != settings.Resolution)
            {
                DisposeTerrainResources();
                _terrainTexture = _device!.CreateTexture2D(new Texture2DDescription
                {
                    Width = (uint)settings.Resolution, Height = (uint)settings.Resolution,
                    MipLevels = 1, ArraySize = 1, Format = Format.R32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Dynamic, BindFlags = BindFlags.ShaderResource,
                    CPUAccessFlags = CpuAccessFlags.Write
                });
                _terrainView = _device.CreateShaderResourceView(_terrainTexture);
            }
            var mapped = _context!.Map(_terrainTexture, 0, MapMode.WriteDiscard);
            try
            {
                for (int row = 0; row < settings.Resolution; row++)
                    Marshal.Copy(heights, row * settings.Resolution,
                        IntPtr.Add(mapped.DataPointer, checked((int)(row * mapped.RowPitch))), settings.Resolution);
            }
            finally { _context.Unmap(_terrainTexture, 0); }
            _terrainSettings = settings;
        }
        _context!.PSSetShaderResource(1, _terrainView!);
    }

    private void DisposeTerrainResources()
    {
        _terrainView?.Dispose();
        _terrainTexture?.Dispose();
        _terrainView = null;
        _terrainTexture = null;
        _terrainSettings = null;
    }
}
