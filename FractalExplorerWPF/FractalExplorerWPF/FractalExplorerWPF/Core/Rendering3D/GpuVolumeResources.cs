using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Структурированный буфер, который вычислительные шейдеры читают и пишут.</summary>
internal sealed class GpuStructuredBuffer : IDisposable
{
    public GpuStructuredBuffer(ID3D11Device device, int count, int stride)
    {
        Count = count;
        Buffer = device.CreateBuffer(new BufferDescription { ByteWidth = checked((uint)(count * stride)),
            Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess,
            MiscFlags = ResourceOptionFlags.BufferStructured, StructureByteStride = (uint)stride });
        try { View = device.CreateShaderResourceView(Buffer); WriteView = device.CreateUnorderedAccessView(Buffer); }
        catch { View?.Dispose(); Buffer.Dispose(); throw; }
    }

    public ID3D11Buffer Buffer { get; }
    public ID3D11ShaderResourceView View { get; } = null!;
    public ID3D11UnorderedAccessView WriteView { get; } = null!;
    public int Count { get; }

    public void Dispose() { WriteView.Dispose(); View.Dispose(); Buffer.Dispose(); }
}

/// <summary>Объём для рендера: шейдер публикации пишет его, трассировка читает без копий через ЦП.</summary>
internal sealed class GpuVolumeTexture : IDisposable
{
    public GpuVolumeTexture(ID3D11Device device, int side, Format format)
    {
        Texture = device.CreateTexture3D(new Texture3DDescription
        {
            Width = (uint)side, Height = (uint)side, Depth = (uint)side, MipLevels = 1, Format = format,
            Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess
        });
        try { View = device.CreateShaderResourceView(Texture); WriteView = device.CreateUnorderedAccessView(Texture); }
        catch { View?.Dispose(); Texture.Dispose(); throw; }
    }

    public ID3D11Texture3D Texture { get; }
    public ID3D11ShaderResourceView View { get; } = null!;
    public ID3D11UnorderedAccessView WriteView { get; } = null!;

    public void Dispose() { WriteView.Dispose(); View.Dispose(); Texture.Dispose(); }
}

/// <summary>Слот публикации: точная копия поля для сохранения и его объём для рендера.</summary>
internal sealed class GpuFieldSlot : IDisposable
{
    public GpuFieldSlot(ID3D11Device device, int side, int stride, Format displayFormat)
    {
        Field = new(device, side * side * side, stride);
        try { Display = new(device, side, displayFormat); }
        catch { Field.Dispose(); throw; }
    }

    public GpuStructuredBuffer Field { get; }
    public GpuVolumeTexture Display { get; }

    public void Dispose() { Display.Dispose(); Field.Dispose(); }
}
