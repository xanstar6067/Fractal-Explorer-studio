namespace FractalExplorerWPF.Core.Rendering3D;

internal static class AbcFlowShader
{
    // Reuse the exact capsule intersections and threaded BVH, including probes and reflections.
    public static string Source
    {
        get
        {
            string source = LSystem3DShader.Source;
            int first = source.IndexOf("bool LCapsule", StringComparison.Ordinal);
            int last = source.IndexOf("float LBoxDistance", StringComparison.Ordinal);
            string result = "StructuredBuffer<float4> AbcSamples : register(t4);\n" + source[..first] + Capsule + source[last..];
            return ShaderSource.Patch(result, "ABC-потока",
                ("float4 lo = LNodes[node * 2], hi = LNodes[node * 2 + 1];", """
                    float4 lo = LNodes[node * 2], hi = LNodes[node * 2 + 1];
                    if((hi.w == -2 && ShapeA.y < .5) || (hi.w == -3 && ShapeA.z < .5)) {
                        node=(int)lo.w; continue;
                    }
                    """));
        }
    }
    private static string Capsule => ShaderSource.Load("AbcFlow.Capsule.hlsl");
}
