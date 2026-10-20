namespace FractalExplorerWPF.Models;

public readonly record struct PhoenixAtlasParameters(decimal C1Real, decimal C1Imaginary, decimal C2Real, decimal C2Imaginary)
{
    public decimal[] ToArray() => [C1Real, C1Imaginary, C2Real, C2Imaginary];
    public static PhoenixAtlasParameters From(PhoenixState state) => new(state.C1Real, state.C1Imaginary, state.C2Real, state.C2Imaginary);
    public static PhoenixAtlasParameters From(IReadOnlyList<decimal> point)
    {
        if (point.Count != 4) throw new ArgumentException("Phoenix требует четыре компоненты C1/C2.", nameof(point));
        return new(point[0], point[1], point[2], point[3]);
    }
    public void ApplyTo(PhoenixState state)
    { state.C1Real = C1Real; state.C1Imaginary = C1Imaginary; state.C2Real = C2Real; state.C2Imaginary = C2Imaginary; }
}
