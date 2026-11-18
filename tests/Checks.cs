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
        Assert(!Preferences.SevenDigits("123456"), "short");
        Assert(!Preferences.SevenDigits("12345678"), "long");
        Assert(!Preferences.SevenDigits("１２３４５６７"), "non ascii");
        Assert(!Preferences.SevenDigits("012 456"), "non digit");
        Assert(Preferences.SevenDigits("0022446"), "leading zero");
        prefs.SetPin("0022446");
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
        var entry = new PinEntry(delegate(byte[] b){ captured = b; submissions++; });
        Tap(entry, '8', 1); Tap(entry, (int)Keys.Escape, 3);
        Enter(entry, "01", 5); Tap(entry, (int)Keys.Back, 14);
        Tap(entry, 0x60, 16);
        entry.Key(0x62, false, 18); entry.Key(0x62, false, 19); entry.Key(0x62, true, 20);
        Enter(entry, "244", 22);
        entry.Key('6', false, 30);
        Assert(submissions == 1 && Encoding.ASCII.GetString(captured) == "0022446", "edit, leading zero, numpad, repeat");
        entry.Confirmed = true;
        Assert(!entry.CanRelease, "held last digit stays swallowed");
        entry.Key('6', true, 35);
        Assert(entry.CanRelease, "release after last key-up");
        entry.Reset();
        Enter(entry, "777", 40); Enter(entry, "0022446", 11000);
        Assert(submissions == 2 && Encoding.ASCII.GetString(captured) == "0022446", "stale digits expire");
        Enter(entry, "1234567", 12000);
        Assert(submissions == 2, "busy verifier ignores further digits");
        entry.Reset();

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
                Assert(submissions == 2, "injected seven-digit sequence cannot submit PIN");
                foreach (int key in Encoding.ASCII.GetBytes("0022446"))
                {
                    Marshal.WriteInt32(packet,0,key);
                    Marshal.WriteInt32(packet,8,0); Assert(keyProc(0,new IntPtr(0x100),packet) == new IntPtr(1),"physical PIN down suppressed");
                    Marshal.WriteInt32(packet,8,0x80); Assert(keyProc(0,new IntPtr(0x101),packet) == new IntPtr(1),"physical PIN up suppressed");
                }
                Assert(submissions == 3 && prefs.Matches(captured), "physical PIN reaches real verifier");
                foreach(int msg in new int[]{0x200,0x201,0x202,0x204,0x205,0x207,0x208,0x20a,0x20b,0x20c,0x20e})
                {
                    Marshal.WriteInt32(packet,12,0);
                    Assert(mouseProc(0,new IntPtr(msg),packet) == new IntPtr(1),"physical mouse event suppressed");
                }
                mouseProc(0,new IntPtr(0x201),packet);
                Assert(guard.MouseButtonHeld,"held mouse button tracked");
                Marshal.WriteInt32(packet,12,1);
                mouseProc(0,new IntPtr(0x202),packet);
                Assert(guard.MouseButtonHeld,"injected mouse release independent of physical button");
                Marshal.WriteInt32(packet,12,0);
                mouseProc(0,new IntPtr(0x202),packet);
                Assert(!guard.MouseButtonHeld,"physical mouse button released");
            }
        }
        finally { Marshal.FreeHGlobal(packet); }
        Console.WriteLine("{0} checks passed.",assertions);
        return 0;
    }
}
