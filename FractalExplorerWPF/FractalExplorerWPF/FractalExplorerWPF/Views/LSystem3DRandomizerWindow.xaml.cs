using System.Windows;
using FractalExplorerWPF.Controls;

namespace FractalExplorerWPF.Views;

public partial class LSystem3DRandomizerWindow : Window
{
    public LSystem3DRandomizerWindow(LSystemRandomizerPanel panel)
    {
        InitializeComponent();
        Content = panel;
    }
}
