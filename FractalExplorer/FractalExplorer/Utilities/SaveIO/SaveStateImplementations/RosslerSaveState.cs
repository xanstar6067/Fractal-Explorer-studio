namespace FractalExplorer.Utilities.SaveIO.SaveStateImplementations
{
    /// <summary>
    /// Состояние сохранения для формы аттрактора Рёсслера.
    /// </summary>
    public class RosslerSaveState : FractalSaveStateBase
    {
        public decimal CenterX { get; set; } = 0m;
        public decimal CenterY { get; set; } = 0m;
        public decimal Zoom { get; set; } = 1.0m;

        public decimal A { get; set; } = 0.2m;
        public decimal B { get; set; } = 0.2m;
        public decimal C { get; set; } = 5.7m;
        public decimal Dt { get; set; } = 0.01m;
        public int Steps { get; set; } = 150000;

        public decimal StartX { get; set; } = 0.1m;
        public decimal StartY { get; set; } = 0m;
        public decimal StartZ { get; set; } = 0m;

        public string ProjectionMode { get; set; } = "XY";

        public RosslerSaveState()
        {
        }

        public RosslerSaveState(string fractalType)
        {
            FractalType = fractalType;
        }
    }
}
