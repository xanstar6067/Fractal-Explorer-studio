using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>b1: header followed by four float4 constants per stage.</summary>
internal static class BulbBoxProgram
{
    public const int FloatCount = 4 + BulbBoxSequence.MaxStages * 16;
    public static void Fill(BulbBoxSequence? sequence, float[] data)
    {
        Array.Clear(data);
        if (sequence is null) return;
        sequence.Validate();
        data[0] = sequence.Stages.Count;
        data[1] = sequence.OperationCount;
        for (int i = 0; i < sequence.Stages.Count; i++)
        {
            var s = sequence.Stages[i]; int p = 4 + i * 16;
            data[p] = (int)s.Operation; data[p + 1] = s.Repeats;
            data[p + 2] = (float)s.Power; data[p + 3] = (float)s.Scale;
            data[p + 4] = (float)(s.MinRadius * s.MinRadius); data[p + 5] = (float)s.FoldLimit;
            data[p + 6] = (float)s.Mix; data[p + 7] = (int)s.Inversion;
            double unit = s.Operation == BulbBoxOperation.Rotate ? Math.PI / 180 : 1;
            data[p + 8] = (float)(s.X * unit); data[p + 9] = (float)(s.Y * unit); data[p + 10] = (float)(s.Z * unit);
            data[p + 11] = (float)s.Stretch; data[p + 12] = (float)s.InversionPower;
        }
    }
}
