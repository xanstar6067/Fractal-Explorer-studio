using System.Reflection;
using System.Windows;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    /// <summary>Редактор живого моделирования, который окно поставило для своего режима.</summary>
    private static TEditor SimulationEditor<TEditor>(Fractal3DWindow window) where TEditor : FrameworkElement =>
        (TEditor)((IFractal3DSimulationMode)typeof(Fractal3DWindow)
            .GetField("_simulation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!).Editor;

    /// <summary>
    /// Именованный элемент окна или вложенной панели: у редакторов режимов своя область имён,
    /// поэтому <see cref="FrameworkElement.FindName"/> окна их не видит. Как и FindName, null — если нет.
    /// </summary>
    private static object FindAny(FrameworkElement root, string name) => FindNamed(root, name)!;

    private static object? FindNamed(FrameworkElement root, string name)
    {
        if (root.FindName(name) is { } found) return found;
        foreach (object child in LogicalTreeHelper.GetChildren(root))
            if (child is FrameworkElement element && (element.Name == name ? element : FindNamed(element, name)) is { } match)
                return match;
        return null;
    }

    /// <summary>Закрытый метод редактора режима — кнопки поиска, кисть.</summary>
    private static object? InvokeEditor(object editor, string method, params object[] arguments) =>
        editor.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(editor, arguments);

    /// <summary>Закрытое поле редактора режима — история поиска, токен отмены.</summary>
    private static T EditorField<T>(object editor, string field) =>
        (T)editor.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;

    private static Fractal3DLiveSimulation<GrayScott3DGpuSimulation, GrayScott3DSettings, GrayScott3DField, GrayScott3DVolume> GrayLive(Fractal3DWindow window) =>
        SimulationEditor<GrayScott3DEditor>(window).Simulation;
    private static Fractal3DLiveSimulation<Turing3DGpuSimulation, Turing3DSettings, Turing3DField, Turing3DVolume> TuringLive(Fractal3DWindow window) =>
        SimulationEditor<Turing3DEditor>(window).Simulation;
    private static Fractal3DLiveSimulation<CahnHilliard3DGpuSimulation, CahnHilliard3DSettings, CahnHilliard3DField, CahnHilliard3DVolume> CahnLive(Fractal3DWindow window) =>
        SimulationEditor<CahnHilliard3DEditor>(window).Simulation;
    private static Fractal3DLiveSimulation<Lenia3DGpuSimulation, Lenia3DSettings, Lenia3DField, Lenia3DVolume> LeniaLive(Fractal3DWindow window) =>
        SimulationEditor<Lenia3DEditor>(window).Simulation;
    private static Fractal3DLiveSimulation<Physarum3DGpuSimulation, Physarum3DSettings, Physarum3DField, Physarum3DVolume> PhysarumLive(Fractal3DWindow window) =>
        SimulationEditor<Physarum3DEditor>(window).Simulation;
    private static Fractal3DLiveSimulation<Kobayashi3DGpuSimulation, Kobayashi3DSettings, Kobayashi3DField, Kobayashi3DVolume> KobLive(Fractal3DWindow window) =>
        SimulationEditor<Kobayashi3DEditor>(window).Simulation;
    private static Fractal3DLiveSimulation<Excitable3DGpuSimulation, Excitable3DSettings, Excitable3DField, Excitable3DVolume> ExLive(Fractal3DWindow window) =>
        SimulationEditor<Excitable3DEditor>(window).Simulation;
}
