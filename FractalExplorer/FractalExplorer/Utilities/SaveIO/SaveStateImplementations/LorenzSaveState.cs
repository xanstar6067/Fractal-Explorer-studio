namespace FractalExplorer.Utilities.SaveIO.SaveStateImplementations
{
    /// <summary>
    /// Состояние сохранения для формы аттрактора Лоренца.
    /// </summary>
    public class LorenzSaveState : FractalSaveStateBase
    {
        public decimal CenterX { get; set; } = 0m;
        public decimal CenterY { get; set; } = 25m;
        public decimal Zoom { get; set; } = 1.0m;

        public decimal Sigma { get; set; } = 10m;
        public decimal Rho { get; set; } = 28m;
        public decimal Beta { get; set; } = 2.666666m;
        public decimal Dt { get; set; } = 0.01m;
        public int Steps { get; set; } = 120000;

        public decimal StartX { get; set; } = 0.01m;
        public decimal StartY { get; set; } = 0m;
        public decimal StartZ { get; set; } = 0m;

        public string ProjectionMode { get; set; } = "XY";

        public LorenzSaveState()
        {
        }

        public LorenzSaveState(string fractalType)
        {
            FractalType = fractalType;
        }
    }
}
