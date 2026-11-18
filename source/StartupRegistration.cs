using System;
using Microsoft.Win32;

namespace DeskPause
{
    internal sealed class StartupRegistration
    {
        internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private readonly string subkey, command;

        internal StartupRegistration(string subkey, string executable)
        {
            this.subkey = subkey;
            command = "\"" + executable + "\"";
        }

        private string Read()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(subkey))
                return key == null ? null : (string)key.GetValue("DeskPause", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }
        internal bool Enabled { get { return Read() != null; } }

        private void Write(string value)
        {
            if (value == null)
            {
                using (var key = Registry.CurrentUser.OpenSubKey(subkey, true))
                    if (key != null) key.DeleteValue("DeskPause", false);
            }
            else
            {
                using (var key = Registry.CurrentUser.CreateSubKey(subkey))
                    key.SetValue("DeskPause", value, RegistryValueKind.String);
            }
        }

        internal void Save(bool enabled, Action savePreferences)
        {
            string old = Read();
            string desired = enabled ? command : null;
            if (old == desired) { savePreferences(); return; }
            if (enabled && command.Length > 260)
                throw new InvalidOperationException("Move DeskPause to a shorter path to start it at sign-in.");
            Write(desired);
            try { savePreferences(); }
            catch
            {
                if (Read() == desired) Write(old);
                throw;
            }
        }
    }
}
