using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using FractalExplorerWPF.Models;
using Vortice.Direct3D11;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    private ID3D11ComputeShader? _abcCompute;
    private AbcFlowSettings? _abcIntegrationKey, _abcGeometryKey;
    private Vector4[]? _abcSamples;
    private AbcFlowGeometry? _abcGeometry;
    private ID3D11Buffer? _abcNodes, _abcCapsules;
    private ID3D11ShaderResourceView? _abcNodesView, _abcCapsulesView;
    private ID3D11Buffer? _abcSampleBuffer;
    private ID3D11ShaderResourceView? _abcSampleView;
    public IReadOnlyList<AbcSectionPoint> AbcFlowSections => _abcGeometry?.Sections ?? [];
    internal int AbcIntegrationBuilds { get; private set; }

    private void EnsureAbcFlow(Fractal3DState state, CancellationToken token)
    {
        if (state.Kind != Fractal3DKind.AbcFlow) return;
        var s = state.AbcFlow; s.Validate();
        if (_abcIntegrationKey != s.IntegrationKey())
        {
            Vector4[] samples = IntegrateAbc(s, token);
            token.ThrowIfCancellationRequested();
            var buffer = CreateLSystemBuffer(samples);
            ID3D11ShaderResourceView view;
            try { view = _device!.CreateShaderResourceView(buffer); } catch { buffer.Dispose(); throw; }
            _abcSampleView?.Dispose(); _abcSampleBuffer?.Dispose();
            _abcSampleBuffer = buffer; _abcSampleView = view;
            _abcSamples = samples; _abcIntegrationKey = s.IntegrationKey(); _abcGeometryKey = null;
            AbcIntegrationBuilds++;
        }
        if (_abcGeometryKey != s.GeometryKey())
        {
            var geometry = AbcFlowGeometry.Build(s, _abcSamples!, token);
            token.ThrowIfCancellationRequested();
            var nodes = CreateLSystemBuffer(geometry.Nodes);
            ID3D11Buffer? capsules = null;
            ID3D11ShaderResourceView? nodesView = null, capsulesView = null;
            try
            {
                capsules = CreateLSystemBuffer(geometry.Capsules);
                nodesView = _device!.CreateShaderResourceView(nodes);
                capsulesView = _device.CreateShaderResourceView(capsules);
            }
            catch { capsulesView?.Dispose(); nodesView?.Dispose(); capsules?.Dispose(); nodes.Dispose(); throw; }
            DisposeAbcGeometry();
            _abcNodes = nodes; _abcCapsules = capsules; _abcNodesView = nodesView; _abcCapsulesView = capsulesView;
            _abcGeometry = geometry; _abcGeometryKey = s.GeometryKey();
        }
        _context!.PSSetShaderResource(2, _abcNodesView!);
        _context.PSSetShaderResource(3, _abcCapsulesView!);
        _context.PSSetShaderResource(4, _abcSampleView!);
    }

    // Called under the shared device gate. Batches cap command length and observe cancellation.
    private Vector4[] IntegrateAbc(AbcFlowSettings s, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _abcCompute ??= _device!.CreateComputeShader(Compile(AbcFlowComputeShader.CacheEntry).Span);
        int count = s.SeedCount * (s.Samples + 1) * 2;
        using var states = Buffer(s.SeedCount * 2);
        using var output = Buffer(count);
        using var stateWrite = _device!.CreateUnorderedAccessView(states);
        using var outputWrite = _device.CreateUnorderedAccessView(output);
        using var constants = _device.CreateBuffer(new BufferDescription { ByteWidth = 32, Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ConstantBuffer, CPUAccessFlags = CpuAccessFlags.Write });
        using var fence = _device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));
        var initial = new Vector4[s.SeedCount * 2];
        var seeds = s.Seeds();
        for (int i = 0; i < seeds.Length; i++)
        { initial[i * 2] = new(seeds[i], 0); initial[i * 2 + 1] = new(Vector3.Normalize(new Vector3(1, .37f, .19f)), 0); }
        var context = _context!;
        context.UpdateSubresource(initial, states);
        try
        {
            for (int offset = 0; offset < s.Samples; offset += 128)
            {
                token.ThrowIfCancellationRequested();
                float[] parameters = [(float)s.A, (float)s.B, (float)s.C, (float)s.TimeStep,
                    s.SeedCount, s.Samples, offset, Math.Min(128, s.Samples - offset)];
                var mapped = context.Map(constants, MapMode.WriteDiscard);
                try { Marshal.Copy(parameters, 0, mapped.DataPointer, 8); } finally { context.Unmap(constants, 0); }
                context.CSSetShader(_abcCompute); context.CSSetConstantBuffer(0, constants);
                context.CSSetUnorderedAccessView(0, stateWrite); context.CSSetUnorderedAccessView(1, outputWrite);
                context.Dispatch(1, 1, 1);
                context.CSSetUnorderedAccessView(0, null!); context.CSSetUnorderedAccessView(1, null!);
                context.End(fence); context.Flush();
                var watch = Stopwatch.StartNew();
                while (true)
                {
                    var result = context.GetData(fence, IntPtr.Zero, 0, AsyncGetDataFlags.DoNotFlush);
                    result.CheckError(); if (result.Code == 0) break;
                    token.ThrowIfCancellationRequested();
                    if (watch.Elapsed.TotalSeconds > 15) throw new TimeoutException("ГП не завершил расчёт потока ABC.");
                    Thread.Sleep(1);
                }
            }
        }
        finally { context.CSSetUnorderedAccessView(0, null!); context.CSSetUnorderedAccessView(1, null!); context.CSSetShader(null!); }
        token.ThrowIfCancellationRequested();
        using var staging = _device.CreateBuffer(new BufferDescription { ByteWidth = (uint)(count * 16),
            Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read });
        context.CopyResource(staging, output);
        var read = context.Map(staging, MapMode.Read);
        float[] values = new float[count * 4];
        try { Marshal.Copy(read.DataPointer, values, 0, values.Length); } finally { context.Unmap(staging, 0); }
        return MemoryMarshal.Cast<float, Vector4>(values).ToArray();

        ID3D11Buffer Buffer(int elements) => _device!.CreateBuffer(new BufferDescription { ByteWidth = (uint)(elements * 16),
            Usage = ResourceUsage.Default, BindFlags = BindFlags.UnorderedAccess,
            MiscFlags = ResourceOptionFlags.BufferStructured, StructureByteStride = 16 });
    }

    internal async Task<Vector4[]> ReadAbcSamplesAsync(AbcFlowSettings settings, CancellationToken token)
    {
        settings.Validate();
        return await Task.Run(() =>
        {
            Gate.Wait(token);
            try { EnsureDevice(); return IntegrateAbc(settings, token); } finally { Gate.Release(); }
        }, token);
    }
    private void DisposeAbcGeometry()
    {
        _abcNodesView?.Dispose(); _abcCapsulesView?.Dispose(); _abcNodes?.Dispose(); _abcCapsules?.Dispose();
        _abcNodesView = _abcCapsulesView = null; _abcNodes = _abcCapsules = null;
        _abcGeometry = null; _abcGeometryKey = null;
    }
    private void DisposeAbcFlow()
    {
        DisposeAbcGeometry(); _abcCompute?.Dispose(); _abcSampleView?.Dispose(); _abcSampleBuffer?.Dispose();
        _abcCompute = null; _abcSampleView = null; _abcSampleBuffer = null; _abcSamples = null; _abcIntegrationKey = null;
    }
}
