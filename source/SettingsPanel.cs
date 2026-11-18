using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DeskPause
{
    // A fresh helper and a private pair of inherited pipes exist only while Settings is open.
    internal sealed class SettingsPanel
    {
        internal static string Executable { get { return Path.Combine(Application.StartupPath, "DeskPaw.Settings.exe"); } }
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 16384 };
        private Process child;
        private StreamWriter writer;
        [DllImport("user32")] private static extern bool SetForegroundWindow(IntPtr handle);
        [DllImport("user32")] private static extern bool IsIconic(IntPtr handle);
        [DllImport("user32")] private static extern bool ShowWindow(IntPtr handle, int cmd);

        public sealed class Changes
        {
            public int? mods { get; set; }
            public int? key { get; set; }
            public string pin { get; set; }
            public string confirm { get; set; }
            public bool? fade { get; set; }
            public bool? lockAfterWrongPins { get; set; }
            public bool? startup { get; set; }
        }

        internal static string Apply(Changes edit, Preferences prior, bool? startupAvailable,
            Func<Preferences, bool?, string> commit)
        {
            if (edit == null || !edit.mods.HasValue || edit.mods < 1 || edit.mods > 15 ||
                !edit.key.HasValue || !Preferences.ValidKey((Keys)edit.key.Value))
                return "Choose at least one modifier and an available key.";
            if (!edit.fade.HasValue || (startupAvailable.HasValue && !edit.startup.HasValue))
                return "The settings are incomplete. Reopen the panel and try again.";
            var next = new Preferences { Mods = (uint)edit.mods.Value, Key = (Keys)edit.key.Value, FadeIndicator = edit.fade.Value };
            // Older portable helpers omit this field when saving unrelated settings.
            next.LockAfterWrongPins = edit.lockAfterWrongPins ?? (prior != null && prior.LockAfterWrongPins);
            if (prior != null && edit.pin == "" && edit.confirm == "")
            {
                next.PinLength = prior.PinLength;
                next.Salt = prior.Salt;
                next.Hash = prior.Hash;
            }
            else
            {
                if (!Preferences.ValidPin(edit.pin)) return "Enter a PIN of 4–15 digits (0–9).";
                if (edit.pin != edit.confirm) return "The PINs differ. Enter the same PIN in both fields.";
                next.SetPin(edit.pin);
            }
            return commit(next, startupAvailable.HasValue ? edit.startup : null);
        }

        internal async Task<bool> Run(Preferences prior, bool? startup, string notice, Func<Preferences, bool?, string> commit)
        {
            var value = prior ?? new Preferences();
            var keys = new List<object>();
            for (int vk = 0; vk < 256; vk++)
                if (Preferences.ValidKey((Keys)vk)) keys.Add(new { value = vk, label = Preferences.KeyLabel((Keys)vk) });
            var initial = new { mods = value.Mods, key = (int)value.Key, fade = value.FadeIndicator, startup,
                lockAfterWrongPins = value.LockAfterWrongPins, hasPin = prior != null, notice, keys, version = Application.ProductVersion };
            var start = new ProcessStartInfo(Executable) {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
                StandardOutputEncoding = new UTF8Encoding(false), CreateNoWindow = true
            };
            bool saved = false;
            using (child = Process.Start(start))
            {
                try
                {
                    using (writer = new StreamWriter(child.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true })
                    {
                        writer.WriteLine(json.Serialize(initial));
                        string line;
                        while ((line = await child.StandardOutput.ReadLineAsync()) != null)
                        {
                            string error;
                            try { error = Apply(json.Deserialize<Changes>(line), prior, startup, commit); }
                            catch (Exception ex) { error = ex.Message; }
                            if (error == null) saved = true;
                            writer.WriteLine(json.Serialize(error));
                        }
                    }
                    await Task.Run((Action)child.WaitForExit);
                    if (!saved && child.ExitCode != 0) throw new InvalidOperationException("The Tauri panel could not start.");
                }
                finally { writer = null; child = null; }
            }
            return saved;
        }

        internal void Activate()
        {
            if (child == null || child.HasExited) return;
            child.Refresh();
            IntPtr handle = child.MainWindowHandle;
            if (handle == IntPtr.Zero) return;
            if (IsIconic(handle)) ShowWindow(handle, 9);
            SetForegroundWindow(handle);
        }
        internal void Close() { if (writer != null) writer.Close(); }
    }
}
