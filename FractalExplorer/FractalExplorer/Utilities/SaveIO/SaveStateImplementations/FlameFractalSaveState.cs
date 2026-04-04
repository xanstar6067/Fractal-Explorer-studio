using FractalExplorer.Engines;

namespace FractalExplorer.Utilities.SaveIO.SaveStateImplementations
{
    public sealed class FlameFractalSaveState : FractalSaveStateBase
    {
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double Scale { get; set; }
        public int Samples { get; set; }
        public int IterationsPerSample { get; set; }
        public int WarmupIterations { get; set; }
        public double Exposure { get; set; }
        public double Gamma { get; set; }
        public List<FlameTransform> Transforms { get; set; } = new();

        public FlameFractalSaveState() { }

        public FlameFractalSaveState(string fractalType)
        {
            FractalType = fractalType;
        }
    }
}
