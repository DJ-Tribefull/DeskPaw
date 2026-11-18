using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using DeskPause;

internal static class Checks
{
    static int assertions;
    static void Assert(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        assertions++;
    }
    static void Tap(PinEntry pin, int key, int t)
    {
        pin.Key(key, false, t);
        pin.Key(key, true, t+1);
    }
    static void Enter(PinEntry pin, string s, int start)
    {
        foreach(char c in s) { Tap(pin, c, start); start += 2; }
    }
    [STAThread]
    static int Main(string[] args)
    {
        var prefs = new Preferences();
        Assert(!Preferences.ValidPin(null) && !Preferences.ValidPin(""), "empty");
        Assert(!Preferences.ValidPin("123"), "short");
        Assert(!Preferences.ValidPin("0123456789012345"), "long");
        Assert(!Preferences.ValidPin("１２３４５６７"), "non ascii");
        Assert(!Preferences.ValidPin("012 456"), "non digit");
        Assert(Preferences.ValidPin("0022446"), "leading zero");
        for (int length = 4; length <= 15; length++)
        {
            string sample = new string('0', length - 1) + "9";
            Assert(Preferences.ValidPin(sample), "accepted PIN length " + length);
            byte[] received = null;
            var input = new PinEntry(length, delegate(byte[] digits) { received = digits; });
            Enter(input, sample.Substring(0, length - 1), 10);
            Assert(received == null, "no premature submission at length " + length);
            Tap(input, '9', 100);
            Assert(Encoding.ASCII.GetString(received) == sample, "full PIN delivered at length " + length);
        }
        prefs.SetPin("0022446");
        var edit = new SettingsPanel.Changes { mods = 3, key = (int)Keys.Oem2, fade = true, startup = true, pin = "", confirm = "" };
        Preferences proposed = null;
        bool? runAtLogin = null;
        Func<Preferences, bool?, string> capture = delegate(Preferences next, bool? startup)
        { proposed = next; runAtLogin = startup; return null; };
        Assert(SettingsPanel.Apply(edit, prefs, false, capture) == null && proposed.Hash == prefs.Hash &&
            proposed.PinLength == 7 && proposed.FadeIndicator && runAtLogin == true, "Tauri settings preserve the PIN and apply options");
        Assert(SettingsPanel.Apply(edit, null, false, capture) != null, "Tauri first-use requires a PIN");
        edit.pin = "0015"; edit.confirm = "0025";
        Assert(SettingsPanel.Apply(edit, prefs, false, capture) != null, "Tauri rejects mismatched digits");
        edit.confirm = edit.pin;
        Assert(SettingsPanel.Apply(edit, null, null, capture) == null && proposed.PinLength == 4 &&
            proposed.Matches(Encoding.ASCII.GetBytes("0015")) && runAtLogin == null, "Tauri new PIN and unavailable startup");
        edit.key = (int)Keys.Escape;
        Assert(SettingsPanel.Apply(edit, prefs, false, capture) != null, "Tauri rejects unsupported keys");
        edit.key = (int)Keys.F9; edit.mods = 0;
        Assert(SettingsPanel.Apply(edit, prefs, false, capture) != null, "Tauri requires a modifier");
        Assert(prefs.Matches(Encoding.ASCII.GetBytes("0022446")), "PIN match");
        Assert(!prefs.Matches(Encoding.ASCII.GetBytes("0022447")), "incorrect PIN rejected");
        var other = new Preferences();
        other.SetPin("0022446");
        Assert(Convert.ToBase64String(other.Hash) != Convert.ToBase64String(prefs.Hash), "random salt");
        string path = Path.Combine(Path.GetFullPath(args[0]), Guid.NewGuid().ToString("N") + ".dat");
        try
        {
            Assert(Preferences.Load(path) == null, "clean first launch");
            prefs.Save(path);
            Preferences loaded = Preferences.Load(path);
            Assert(loaded.Shortcut == "Ctrl+Alt+F9" && loaded.Matches(Encoding.ASCII.GetBytes("0022446")), "settings round trip");
            other.Mods = 6; other.Key = Keys.D0; other.Save(path);
            loaded = Preferences.Load(path);
            Assert(loaded.Shortcut == "Ctrl+Shift+0", "atomic hotkey replacement");
            Assert(Convert.ToBase64String(loaded.Salt) == Convert.ToBase64String(other.Salt), "atomic verifier replacement");
            byte[] legacy = new byte[51];
            legacy[0] = 1; legacy[1] = (byte)prefs.Mods; legacy[2] = (byte)prefs.Key;
            Buffer.BlockCopy(prefs.Salt, 0, legacy, 3, 16);
            Buffer.BlockCopy(prefs.Hash, 0, legacy, 19, 32);
            File.WriteAllBytes(path, ProtectedData.Protect(legacy, null, DataProtectionScope.CurrentUser));
            loaded = Preferences.Load(path);
            Assert(loaded.PinLength == 7 && !loaded.FadeIndicator && loaded.Matches(Encoding.ASCII.GetBytes("0022446")), "1.1 settings load");
            loaded.FadeIndicator = true;
            loaded.Save(path);
            loaded = Preferences.Load(path);
            Assert(loaded.PinLength == 7 && loaded.FadeIndicator && loaded.Matches(Encoding.ASCII.GetBytes("0022446")), "preserve old PIN when saving new option");
            foreach (string sample in new[] { "0007", "012345678901234" })
            {
                loaded.SetPin(sample);
                loaded.Save(path);
                loaded = Preferences.Load(path);
                Assert(loaded.PinLength == sample.Length && loaded.Matches(Encoding.ASCII.GetBytes(sample)), "persist PIN length " + sample.Length);
            }
            var bytes = File.ReadAllBytes(path);
            bytes[bytes.Length-1] ^= 1;
            File.WriteAllBytes(path, bytes);
            bool failed = false;
            try { Preferences.Load(path); } catch (CryptographicException) { failed = true; }
            Assert(failed, "tampering is detected");
        }
        finally { if(File.Exists(path)) File.Delete(path); }

        byte[] captured = null;
        int submissions = 0;
        var entry = new PinEntry(prefs.PinLength, delegate(byte[] b){ captured = b; submissions++; });
        Tap(entry, '8', 1); Tap(entry, (int)Keys.Escape, 3);
        Enter(entry, "01", 5); Tap(entry, (int)Keys.Back, 14);
        Tap(entry, 0x60, 16);
        entry.Key(0x62, false, 18); entry.Key(0x62, false, 19); entry.Key(0x62, true, 20);
        Enter(entry, "244", 22);
        entry.Key('6', false, 30);
        Assert(submissions == 0, "held last digit stays swallowed");
        entry.Key('6', false, 11035);
        entry.Key('6', true, 11036);
        Assert(submissions == 1 && Encoding.ASCII.GetString(captured) == "0022446", "submit on final release with leading zero, numpad and repeat");
        entry.Reset();
        entry.Key('0', false, 88);
        Tap(entry, (int)Keys.Escape, 89);
        Tap(entry, '0', 91);
        Enter(entry, "58", 93);
        entry.Key((int)Keys.LControlKey, false, 100);
        entry.Key((int)Keys.LMenu, false, 101);
        entry.Key((int)Keys.Delete, false, 102);
        // Windows can deliver the three releases to its secure desktop.
        Enter(entry, "0022446", 200);
        Assert(submissions == 2 && prefs.Matches(captured), "correct PIN submitted after unobserved Ctrl+Alt+Delete releases");
        entry.Reset();
        Enter(entry, "777", 40); Enter(entry, "0022446", 11000);
        Assert(submissions == 3 && Encoding.ASCII.GetString(captured) == "0022446", "stale digits expire");
        Enter(entry, "1234567", 12000);
        Assert(submissions == 3, "busy verifier ignores further digits");
        entry.Reset();
        byte[] recovered = null;
        var interrupted = new PinEntry(4, delegate(byte[] b) { recovered = b; });
        Enter(interrupted, "123", 100);
        interrupted.Key('4', false, 110);
        Tap(interrupted, (int)Keys.Escape, 120);
        Enter(interrupted, "4321", 130);
        Assert(Encoding.ASCII.GetString(recovered) == "4321", "Escape recovers a missing final digit release");

        IntPtr packet = Marshal.AllocHGlobal(32);
        try
        {
            for(int i=0;i<32;i++) Marshal.WriteByte(packet,i,0);
            using(var guard = new InputGuard(entry, IntPtr.Zero))
            {
                var keyProc = (Native.Hook)typeof(InputGuard).GetField("keyboardProc",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(guard);
                var mouseProc = (Native.Hook)typeof(InputGuard).GetField("mouseProc",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(guard);
                foreach(int key in new int[]{0x12,0x09,0x5b,0x45,0x11,0x1b})
                {
                    Marshal.WriteInt32(packet,0,key);
                    Marshal.WriteInt32(packet,8,0);
                    Assert(keyProc(0,new IntPtr(0x100),packet) == new IntPtr(1),"physical shortcut blocked down");
                    Marshal.WriteInt32(packet,8,0x80);
                    Assert(keyProc(0,new IntPtr(0x101),packet) == new IntPtr(1),"physical shortcut blocked up");
                }
                foreach (int key in Encoding.ASCII.GetBytes("0022446"))
                {
                    Marshal.WriteInt32(packet,0,key);
                    Marshal.WriteInt32(packet,8,0x10); keyProc(0,new IntPtr(0x100),packet);
                    Marshal.WriteInt32(packet,8,0x90); keyProc(0,new IntPtr(0x101),packet);
                }
                Assert(submissions == 3, "injected seven-digit sequence cannot submit PIN");
                foreach (int key in Encoding.ASCII.GetBytes("0022446"))
                {
                    Marshal.WriteInt32(packet,0,key);
                    Marshal.WriteInt32(packet,8,0); Assert(keyProc(0,new IntPtr(0x100),packet) == new IntPtr(1),"physical PIN down suppressed");
                    Marshal.WriteInt32(packet,8,0x80); Assert(keyProc(0,new IntPtr(0x101),packet) == new IntPtr(1),"physical PIN up suppressed");
                }
                Assert(submissions == 4 && prefs.Matches(captured), "physical PIN reaches real verifier");
                foreach(int msg in new int[]{0x200,0x201,0x202,0x204,0x205,0x207,0x208,0x20a,0x20b,0x20c,0x20e})
                {
                    Marshal.WriteInt32(packet,12,0);
                    Assert(mouseProc(0,new IntPtr(msg),packet) == new IntPtr(1),"physical mouse event suppressed");
                }
            }
        }
        finally { Marshal.FreeHGlobal(packet); }
        Console.WriteLine("{0} checks passed.",assertions);
        return 0;
    }
}
