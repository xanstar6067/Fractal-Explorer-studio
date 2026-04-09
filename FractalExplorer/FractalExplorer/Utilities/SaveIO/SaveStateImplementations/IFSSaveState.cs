using FractalExplorer.Engines;

namespace FractalExplorer.Utilities.SaveIO.SaveStateImplementations
{
    public sealed class IFSSaveState : FractalSaveStateBase
    {
        // legacy
        public int Iterations { get; set; }

        // new explorer params
        public string? PointOfInterestId { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double Scale { get; set; }
        public List<IfsAffineTransform> Transforms { get; set; } = [];

        public Color FractalColor { get; set; }
        public Color BackgroundColor { get; set; }

        public IFSSaveState()
        {
        }

        public IFSSaveState(string fractalType)
        {
            FractalType = fractalType;
        }
    }
}
