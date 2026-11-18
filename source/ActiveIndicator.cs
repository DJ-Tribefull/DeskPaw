using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeskPause
{
    internal sealed class IndicatorWindow : Form
    {
        [DllImport("user32", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        internal const double FullOpacity = .94;
        private readonly Font captionFont;
        internal IndicatorWindow(Rectangle monitor)
        {
            Text = "DeskPaw Active";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(34, 36, 37);
            ForeColor = Color.White;
            Opacity = FullOpacity;
            DoubleBuffered = true;
            int pixels = Math.Max(24, Math.Min(58, monitor.Height / 30));
            captionFont = new Font("Segoe UI", pixels, FontStyle.Bold, GraphicsUnit.Pixel);
            Size text = TextRenderer.MeasureText(Text, captionFont);
            int width = Math.Min(monitor.Width, text.Width + pixels * 2);
            int height = Math.Min(monitor.Height, text.Height + pixels);
            Bounds = new Rectangle(monitor.Left + (monitor.Width - width) / 2,
                monitor.Top + (monitor.Height - height) / 2, width, height);
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        internal void ShowIndicator()
        {
            // Form.Show can activate a top-level window even when ShowWithoutActivation is overridden.
            // Set the bounds, topmost z-order and visibility in one SWP_NOACTIVATE operation.
            Rectangle b = Bounds;
            if (!SetWindowPos(Handle, new IntPtr(-1), b.X, b.Y, b.Width, b.Height, 0x10 | 0x40))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        protected override CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                // WS_EX_NOACTIVATE, TOOLWINDOW, LAYERED and TRANSPARENT keep underlying apps interactive.
                p.ExStyle |= 0x08000000 | 0x80 | 0x80000 | 0x20;
                return p;
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var accent = new Pen(Color.FromArgb(250, 183, 88), 3))
                e.Graphics.DrawRectangle(accent, 2, 2, ClientSize.Width - 5, ClientSize.Height - 5);
            TextRenderer.DrawText(e.Graphics, Text, captionFont, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && captionFont != null) captionFont.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class ActiveIndicator : IDisposable
    {
        private readonly List<IndicatorWindow> windows = new List<IndicatorWindow>();
        private readonly Stopwatch shown = new Stopwatch();
        private bool fade;

        internal void Show(bool fadeAfterTwoSeconds)
        {
            fade = fadeAfterTwoSeconds;
            shown.Restart();
            RefreshDisplays();
        }

        internal void RefreshDisplays()
        {
            if (!shown.IsRunning) return;
            CloseWindows();
            foreach (Screen screen in Screen.AllScreens)
            {
                var window = new IndicatorWindow(screen.Bounds);
                windows.Add(window);
                window.ShowIndicator();
            }
            Tick();
        }

        internal void Tick()
        {
            if (!fade || !shown.IsRunning || shown.ElapsedMilliseconds < 2000) return;
            double remaining = 1.0 - (shown.ElapsedMilliseconds - 2000) / 350.0;
            if (remaining <= 0) { Hide(); return; }
            foreach (var window in windows) window.Opacity = IndicatorWindow.FullOpacity * remaining;
        }

        private void CloseWindows()
        {
            foreach (var window in windows) window.Dispose();
            windows.Clear();
        }

        internal void Hide() { shown.Reset(); CloseWindows(); }
        public void Dispose() { Hide(); }
    }
}
