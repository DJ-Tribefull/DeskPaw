using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace DeskPause
{
    internal sealed class Preferences
    {
        internal uint Mods = 3; // Ctrl + Alt
        internal Keys Key = Keys.F9;
        internal byte[] Salt, Hash;
        internal const int MinPinLength = 4, MaxPinLength = 15;
        internal int PinLength;
        internal bool FadeIndicator;
        internal bool LockAfterWrongPins;
        private const int Rounds = 200000;
        internal static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskPause"); }
        }
        internal static string FileName { get { return Path.Combine(Folder, "settings.dat"); } }

        // Windows registers these keyboard-layout-dependent keys by virtual key code.
        // Use the US key face only if the current layout supplies no printable character.
        private static readonly Dictionary<Keys, string> Punctuation = new Dictionary<Keys, string>
        {
            { Keys.Oem1, ";" }, { Keys.Oemplus, "=" }, { Keys.Oemcomma, "," },
            { Keys.OemMinus, "-" }, { Keys.OemPeriod, "." }, { Keys.Oem2, "/" },
            { Keys.Oem3, "\u0060" }, { Keys.Oem4, "[" }, { Keys.Oem5, "\\" },
            { Keys.Oem6, "]" }, { Keys.Oem7, "'" }, { Keys.Oem8, "OEM 8" },
            { Keys.Oem102, "extra key" }
        };

        private static readonly Dictionary<Keys, string> KeypadOperators = new Dictionary<Keys, string>
        {
            { Keys.Divide, "Numpad /" }, { Keys.Multiply, "Numpad *" }, { Keys.Subtract, "Numpad -" },
            { Keys.Add, "Numpad +" }, { Keys.Decimal, "Numpad decimal" }
        };

        internal static bool ValidKey(Keys key)
        {
            return (key >= Keys.A && key <= Keys.Z) || (key >= Keys.D0 && key <= Keys.D9) ||
                (key >= Keys.F1 && key <= Keys.F24 && key != Keys.F12) ||
                (key >= Keys.NumPad0 && key <= Keys.NumPad9) ||
                Punctuation.ContainsKey(key) || KeypadOperators.ContainsKey(key);
        }

        internal static string KeyLabel(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9) return ((int)key - (int)Keys.D0).ToString();
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return "Numpad " + ((int)key - (int)Keys.NumPad0);
            string label;
            if (KeypadOperators.TryGetValue(key, out label)) return label;
            if (!Punctuation.TryGetValue(key, out label)) return key.ToString();

            // MAPVK_VK_TO_CHAR returns the unshifted character in the low word, even for dead keys.
            char glyph = (char)(Native.MapVirtualKey((uint)key, 2) & 0xffff);
            if (!char.IsControl(glyph) && !char.IsWhiteSpace(glyph)) label = glyph.ToString();
            return key == Keys.Oem102 ? "ISO " + label : label;
        }

        internal static bool ValidPin(string value)
        {
            if (value == null || value.Length < MinPinLength || value.Length > MaxPinLength) return false;
            foreach (char c in value) if (c < '0' || c > '9') return false;
            return true;
        }

        internal string Shortcut
        {
            get
            {
                return ((Mods & 2) != 0 ? "Ctrl+" : "") + ((Mods & 1) != 0 ? "Alt+" : "") +
                    ((Mods & 4) != 0 ? "Shift+" : "") + ((Mods & 8) != 0 ? "Win+" : "") + KeyLabel(Key);
            }
        }

        private byte[] Derive(byte[] digits)
        {
            using (var kdf = new Rfc2898DeriveBytes(digits, Salt, Rounds, HashAlgorithmName.SHA256))
                return kdf.GetBytes(32);
        }

        internal void SetPin(string text)
        {
            if (!ValidPin(text)) throw new ArgumentException("Invalid PIN. Enter 4–15 digits (0–9).");
            Salt = new byte[16];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(Salt);
            var digits = Encoding.ASCII.GetBytes(text);
            try { Hash = Derive(digits); PinLength = digits.Length; }
            finally { Array.Clear(digits, 0, digits.Length); }
        }

        internal bool Matches(byte[] digits)
        {
            byte[] actual = Derive(digits);
            int difference = 0;
            for (int i = 0; i < Hash.Length; i++) difference |= actual[i] ^ Hash[i];
            Array.Clear(actual, 0, actual.Length);
            return difference == 0;
        }

        internal void Save(string path)
        {
            string folder = Path.GetDirectoryName(path);
            Directory.CreateDirectory(folder);
            var record = new byte[54];
            record[0] = 3;
            record[1] = (byte)Mods;
            record[2] = (byte)Key;
            Buffer.BlockCopy(Salt, 0, record, 3, 16);
            Buffer.BlockCopy(Hash, 0, record, 19, 32);
            record[51] = checked((byte)PinLength);
            record[52] = (byte)(FadeIndicator ? 1 : 0);
            record[53] = (byte)(LockAfterWrongPins ? 1 : 0);
            var blob = ProtectedData.Protect(record, null, DataProtectionScope.CurrentUser);
            string temp = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllBytes(temp, blob);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        internal static Preferences Load(string path)
        {
            if (!File.Exists(path)) return null;
            byte[] b = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            bool legacy = b.Length == 51 && b[0] == 1;
            bool v2 = b.Length == 53 && b[0] == 2;
            bool v3 = b.Length == 54 && b[0] == 3 && b[53] <= 1;
            if ((!legacy && !v2 && !v3) || (!legacy && (b[51] < MinPinLength || b[51] > MaxPinLength || b[52] > 1)) ||
                b[1] == 0 || b[1] > 15 || !ValidKey((Keys)b[2]))
                throw new InvalidDataException("The saved settings could not be read.");
            var result = new Preferences { Mods = b[1], Key = (Keys)b[2], Salt = new byte[16], Hash = new byte[32] };
            result.PinLength = legacy ? 7 : b[51];
            result.FadeIndicator = !legacy && b[52] == 1;
            result.LockAfterWrongPins = v3 && b[53] == 1;
            Buffer.BlockCopy(b, 3, result.Salt, 0, 16);
            Buffer.BlockCopy(b, 19, result.Hash, 0, 32);
            return result;
        }
    }
}
