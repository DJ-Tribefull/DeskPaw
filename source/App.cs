using System;
using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("DeskPause")]
[assembly: AssemblyDescription("Desktop input guard for unattended work")]
[assembly: AssemblyProduct("DeskPause")]
[assembly: AssemblyVersion("1.3.0.0")]

namespace DeskPause
{
    internal sealed class MessageWindow : Form
    {
        internal event Action<int> HotKey;
        internal event Action AuthenticatedUnlock;
        internal event Action<int> SessionChanged;
        internal event Action DisplaysChanged;
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && HotKey != null) HotKey(m.WParam.ToInt32());
            if (m.Msg == 0x007e && DisplaysChanged != null) BeginInvoke(DisplaysChanged);
            if (m.Msg == 0x02B1)
            {
                int change = m.WParam.ToInt32();
                if (SessionChanged != null) SessionChanged(change);
                if (change == 8 && AuthenticatedUnlock != null) AuthenticatedUnlock();
            }
            base.WndProc(ref m);
        }
    }

    internal sealed class TrayApp : ApplicationContext
    {
        private readonly MessageWindow window = new MessageWindow();
        private readonly ActiveIndicator indicator = new ActiveIndicator();
        private readonly StartupRegistration startup = new StartupRegistration(StartupRegistration.RunKey, Application.ExecutablePath);
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem arm, settings, check, exit;
        private readonly Icon idleIcon = MakeIcon(false), armedIcon = MakeIcon(true);
        private readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer { Interval = 35 };
        private Preferences prefs;
        private SettingsForm dialog;
        private SettingsPanel panel;
        private InputCheck checkWindow;
        private InputGuard guard;
        private PinEntry entry;
        private byte[] pendingDigits;
        private int hotKeyId, pendingSince, wrong;
        private bool waiting, disposed;

        internal TrayApp(bool openSettings = false)
        {
            if (!Native.WTSRegisterSessionNotification(window.Handle, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows session recovery could not be initialized. Reopen DeskPause.");
            window.HotKey += delegate(int id) { if (id == hotKeyId) Arm(); };
            window.AuthenticatedUnlock += Release;
            window.DisplaysChanged += indicator.RefreshDisplays;
            // A partial attempt should never span a lock or remote session change.
            window.SessionChanged += delegate(int change)
            {
                if (change == 7 || change == 4 || change == 2)
                {
                    if (waiting) Release();
                    if (entry != null) { entry.Reset(); CancelCandidate(); epoch++; }
                }
            };
            arm = new ToolStripMenuItem("Arm DeskPause", null, delegate { Arm(); });
            settings = new ToolStripMenuItem("Settings…", null, delegate { ShowSettings(null); });
            check = new ToolStripMenuItem("Test in a window…", null, delegate { ShowInputCheck(); });
            exit = new ToolStripMenuItem("Exit", null, delegate { if (guard == null && !waiting) ExitThread(); });
            menu.Items.AddRange(new ToolStripItem[] { arm, settings, check, new ToolStripSeparator(), exit });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowSettings(null); };
            tick.Tick += OnTick;
            string notice = null;
            try { prefs = Preferences.Load(Preferences.FileName); }
            catch (Exception) { notice = "The previous settings could not be read. Choose a new PIN and shortcut."; }
            if (prefs != null && Register(prefs, 1)) hotKeyId = 1;
            else if (prefs != null) notice = "Your saved shortcut is in use. Choose another combination.";
            RefreshTray();
            tray.Visible = true;
            if (openSettings || prefs == null || hotKeyId == 0)
                window.BeginInvoke((Action)delegate { ShowSettings(notice); });
            else Notify("Ready. Arm with " + prefs.Shortcut + ".");
        }

        private static Icon MakeIcon(bool armed)
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            using (var back = new SolidBrush(armed ? Color.FromArgb(157, 69, 14) : Color.FromArgb(25, 93, 99)))
            using (var pen = new Pen(Color.White, 2.5f))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.FillEllipse(back, 1, 1, 30, 30);
                if (armed) { g.DrawArc(pen, 11, 6, 10, 17, 180, 180); g.FillRectangle(Brushes.White, 9, 15, 14, 10); }
                else { g.FillRectangle(Brushes.White, 10, 10, 4, 12); g.FillRectangle(Brushes.White, 18, 10, 4, 12); }
                IntPtr h = bmp.GetHicon();
                try { using (var icon = Icon.FromHandle(h)) return (Icon)icon.Clone(); }
                finally { Native.DestroyIcon(h); }
            }
        }

        private bool Register(Preferences value, int id)
        {
            return Native.RegisterHotKey(window.Handle, id, value.Mods | 0x4000, (uint)value.Key);
        }

        private string Commit(Preferences next, bool? startAtSignIn)
        {
            bool changed = hotKeyId == 0 || prefs == null || next.Mods != prefs.Mods || next.Key != prefs.Key;
            int newId = hotKeyId == 1 ? 2 : 1;
            if (changed && !Register(next, newId)) return "Windows or another app already uses that shortcut. Choose a different one.";
            try
            {
                Action save = delegate { next.Save(Preferences.FileName); };
                if (startAtSignIn.HasValue) startup.Save(startAtSignIn.Value, save);
                else save();
            }
            catch { if (changed) Native.UnregisterHotKey(window.Handle, newId); throw; }
            if (changed)
            {
                if (hotKeyId != 0) Native.UnregisterHotKey(window.Handle, hotKeyId);
                hotKeyId = newId;
            }
            prefs = next;
            RefreshTray();
            return null;
        }

        private async void ShowSettings(string notice)
        {
            if (guard != null || waiting) return;
            if (checkWindow != null) { checkWindow.Activate(); return; }
            if (dialog != null) { dialog.Activate(); return; }
            if (panel != null) { panel.Activate(); return; }
            bool? startAtSignIn = null;
            try { startAtSignIn = startup.Enabled; }
            catch (Exception) { notice = ((notice ?? "") + " Windows startup settings are unavailable.").Trim(); }
            if (System.IO.File.Exists(SettingsPanel.Executable))
            {
                panel = new SettingsPanel();
                try
                {
                    bool saved = await panel.Run(prefs, startAtSignIn, notice, Commit);
                    if (saved && !disposed) Notify("Ready. Arm with " + prefs.Shortcut + ".");
                    return;
                }
                catch (Exception) { notice = "The modern panel was unavailable. You can edit your settings here."; }
                finally { panel = null; }
            }
            if (disposed) return;
            using (dialog = new SettingsForm(prefs, startAtSignIn, Commit, notice))
            {
                dialog.Icon = idleIcon;
                if (dialog.ShowDialog() == DialogResult.OK) Notify("Ready. Arm with " + prefs.Shortcut + ".");
            }
            dialog = null;
        }

        private void ShowInputCheck()
        {
            if (guard != null || waiting || dialog != null || panel != null) return;
            if (checkWindow != null) { checkWindow.Activate(); return; }
            using (checkWindow = new InputCheck()) checkWindow.ShowDialog();
            checkWindow = null;
        }

        private void RefreshTray()
        {
            bool busy = guard != null || waiting;
            tray.Icon = guard != null ? armedIcon : idleIcon;
            tray.Text = guard != null ? "DeskPause • armed" : waiting ? "DeskPause • release keys to arm" :
                prefs != null ? "DeskPause • ready" : "DeskPause • setup needed";
            arm.Text = guard != null ? "Armed" : waiting ? "Release keys to arm" : "Arm DeskPause";
            arm.Enabled = !busy && prefs != null;
            settings.Enabled = check.Enabled = exit.Enabled = !busy;
        }

        private void Notify(string message)
        {
            tray.ShowBalloonTip(2500, "DeskPause", message, ToolTipIcon.Info);
        }

        private void Arm()
        {
            if (guard != null || waiting || prefs == null || dialog != null || panel != null || checkWindow != null) return;
            waiting = true;
            pendingSince = Environment.TickCount;
            RefreshTray();
            tick.Start();
        }

        private static bool KeysReleased()
        {
            // Let the foreground app receive releases for keys and buttons pressed before arming.
            for (int key = 1; key <= 254; key++)
                if ((Native.GetAsyncKeyState(key) & 0x8000) != 0) return false;
            return true;
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (waiting)
            {
                if (!KeysReleased())
                {
                    if (unchecked((uint)(Environment.TickCount - pendingSince)) >= 5000)
                    { Release(); Notify("Arming canceled. Release held keys and try again."); }
                    return;
                }
                waiting = false;
                entry = new PinEntry(prefs.PinLength, delegate(byte[] candidate) { pendingDigits = candidate; });
                guard = new InputGuard(entry, IntPtr.Zero);
                try
                {
                    guard.Start();
                    indicator.Show(prefs.FadeIndicator);
                }
                catch (Win32Exception ex) { Release(); Notify("Could not arm. " + ex.Message); return; }
                RefreshTray();
            }
            indicator.Tick();
            if (pendingDigits != null)
            {
                byte[] digits = pendingDigits;
                pendingDigits = null;
                Verify(digits, epoch);
            }
            if (entry != null && entry.CanRelease && !guard.MouseButtonHeld)
            {
                Release();
                Notify("Disarmed. Physical input is available.");
            }
        }

        private int epoch;
        private async void Verify(byte[] digits, int generation)
        {
            Preferences config = prefs;
            bool match;
            try { match = await Task.Run(delegate { return config.Matches(digits); }); }
            catch
            {
                if (generation == epoch && entry != null) { entry.Verifying = false; Notify("PIN check failed. Use Windows sign-in to recover."); }
                return;
            }
            finally { Array.Clear(digits, 0, digits.Length); }
            if (disposed || generation != epoch || entry == null) return;
            entry.Verifying = false;
            if (match) { entry.Confirmed = true; return; }
            if (++wrong >= 5)
            {
                wrong = 0;
                if (!Native.LockWorkStation()) Notify("Windows lock was unavailable. DeskPause is still armed.");
            }
            else Notify("Incorrect PIN. Try again.");
        }

        private void CancelCandidate()
        {
            if (pendingDigits == null) return;
            Array.Clear(pendingDigits, 0, pendingDigits.Length);
            pendingDigits = null;
        }

        private void Release()
        {
            tick.Stop();
            indicator.Hide();
            epoch++;
            CancelCandidate();
            if (guard != null) guard.Dispose();
            guard = null;
            entry = null;
            waiting = false;
            wrong = 0;
            RefreshTray();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                if (panel != null) panel.Close();
                Release();
                if (hotKeyId != 0) Native.UnregisterHotKey(window.Handle, hotKeyId);
                Native.WTSUnRegisterSessionNotification(window.Handle);
                tray.Visible = false;
                tick.Dispose(); tray.Dispose(); menu.Dispose(); window.Dispose(); idleIcon.Dispose(); armedIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 1 && args[0] == "--input-check")
            {
                using (var check = new InputCheck()) Application.Run(check);
                return;
            }
            bool first;
            using (var single = new Mutex(true, @"Local\DeskPause.Tray.1", out first))
            {
                if (!first) { MessageBox.Show("DeskPause is already running. Open Settings from its tray icon.", "DeskPause"); return; }
                try { using (var app = new TrayApp(Array.IndexOf(args, "--settings") >= 0)) Application.Run(app); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "DeskPause could not start", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
    }
}
