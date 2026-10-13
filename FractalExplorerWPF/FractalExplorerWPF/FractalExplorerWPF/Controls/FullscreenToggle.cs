using System.Windows;
using System.Windows.Input;

namespace FractalExplorerWPF.Controls;

/// <summary>
/// Полноэкранный режим окна фрактала: окно без рамки на весь экран, при выходе — прежние рамка
/// и состояние окна. F11 переключает режим, Esc из него выходит.
/// </summary>
public sealed class FullscreenToggle
{
    private WindowStyle _previousStyle;
    private WindowState _previousState;

    public bool IsActive { get; private set; }

    public void Toggle(Window window)
    {
        if (!IsActive)
        {
            _previousStyle = window.WindowStyle;
            _previousState = window.WindowState;
            window.WindowStyle = WindowStyle.None;
            window.WindowState = WindowState.Maximized;
        }
        else
        {
            window.WindowStyle = _previousStyle;
            window.WindowState = _previousState;
        }
        IsActive = !IsActive;
    }

    /// <summary>F11 переключает режим, Esc выходит из него; true — клавиша обработана.</summary>
    public bool HandleKey(Window window, Key key)
    {
        if (key != Key.F11 && !(key == Key.Escape && IsActive)) return false;
        Toggle(window);
        return true;
    }
}
