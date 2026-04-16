namespace FractalExplorer.Utilities.SaveIO.SaveStateImplementations
{
    /// <summary>
    /// Состояние сохранения для отображения Икэды.
    /// </summary>
    public class IkedaSaveState : FractalSaveStateBase
    {
        public decimal CenterX { get; set; }
        public decimal CenterY { get; set; }
        public decimal Zoom { get; set; }

        public decimal U { get; set; }
        public decimal X0 { get; set; }
        public decimal Y0 { get; set; }
        public int Iterations { get; set; }
        public int DiscardIterations { get; set; }

        public decimal RangeXMin { get; set; }
        public decimal RangeXMax { get; set; }
        public decimal RangeYMin { get; set; }
        public decimal RangeYMax { get; set; }

        public IkedaSaveState()
        {
        }

        public IkedaSaveState(string fractalType)
        {
            FractalType = fractalType;
        }
    }
}
