using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace FractalExplorer.Utilities.UI
{
    /// <summary>
    /// Управляет безопасным переключением WinForms-формы в полноэкранный режим и обратно.
    /// </summary>
    public sealed class FullscreenToggleController
    {
        private sealed class WindowStateSnapshot
        {
            public FormBorderStyle FormBorderStyle { get; init; }
            public FormWindowState WindowState { get; init; }
            public Rectangle Bounds { get; init; }
            public bool TopMost { get; init; }
            public FormStartPosition StartPosition { get; init; }
        }

        private readonly Dictionary<Form, WindowStateSnapshot> _snapshots = new();

        public bool IsFullscreen(Form form) => form != null && _snapshots.ContainsKey(form);

        public void Toggle(Form form)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));

            if (IsFullscreen(form))
            {
                ExitFullscreen(form);
            }
            else
            {
                EnterFullscreen(form);
            }
        }

        public void EnterFullscreen(Form form)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));
            if (IsFullscreen(form)) return;

            _snapshots[form] = new WindowStateSnapshot
            {
                FormBorderStyle = form.FormBorderStyle,
                WindowState = form.WindowState,
                Bounds = form.Bounds,
                TopMost = form.TopMost,
                StartPosition = form.StartPosition
            };

            form.FormBorderStyle = FormBorderStyle.None;
            form.StartPosition = FormStartPosition.Manual;
            form.WindowState = FormWindowState.Normal;
            form.Bounds = Screen.FromControl(form).Bounds;
        }

        public void ExitFullscreen(Form form)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));
            if (!_snapshots.TryGetValue(form, out var snapshot)) return;

            form.FormBorderStyle = snapshot.FormBorderStyle;
            form.StartPosition = snapshot.StartPosition;
            form.TopMost = snapshot.TopMost;
            form.WindowState = FormWindowState.Normal;
            form.Bounds = snapshot.Bounds;
            form.WindowState = snapshot.WindowState;

            _snapshots.Remove(form);
        }
    }
}
