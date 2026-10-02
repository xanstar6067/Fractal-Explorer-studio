using System.Windows;
using FractalExplorerWPF.Controls;

namespace FractalExplorerWPF.Views;

public partial class LSystemRandomizerWindow : Window
{
    public LSystemRandomizerWindow(LSystemRandomizerPanel panel)
    {
        InitializeComponent();
        Content = panel;
    }
}
