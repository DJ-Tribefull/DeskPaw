# DeskPaw

DeskPaw is a small Windows tray app for stepping away while Codex Computer Use keeps working on your visible desktop. Press a shortcut to block ordinary physical mouse and keyboard input. Type your PIN to take control again.

**[Download DeskPaw 1.4.0 for Windows x64 (portable ZIP)](https://github.com/DJ-Tribefull/DeskPaw/raw/refs/heads/main/downloads/DeskPaw-1.4.0-win-x64.zip)** · [SHA-256 checksum](https://github.com/DJ-Tribefull/DeskPaw/blob/main/downloads/DeskPaw-1.4.0-win-x64.zip.sha256)

![DeskPaw settings with an arming shortcut and optional PIN change](https://raw.githubusercontent.com/DJ-Tribefull/DeskPaw/v1.4.0/docs/settings.png)

## Get started

1. Extract the ZIP into a folder of your choice, keep the two executables together, and open **DeskPaw.exe**. The tray app runs as a standard user on **x64 Windows with .NET Framework 4.8 or later**. The Tauri settings panel uses the system Microsoft Edge WebView2 Runtime. The executables are unsigned.
2. Choose a shortcut and enter the same **4–15 digit PIN** in both fields. The suggested shortcut is **Ctrl+Alt+F9**. Leading zeroes work. Choose a PIN specifically for DeskPaw. Existing seven-digit PINs carry over from 1.1.
3. Select **Save settings**, then press and release your shortcut. **DeskPaw Active** appears in the center of each active monitor. The teal tray icon turns into an amber padlock. If the icon is hidden, open the taskbar’s tray overflow.
4. To resume, type the PIN on the physical keyboard and release the keys. Use the number row or the numpad with **Num Lock** on. The digits stay out of the focused app. You can leave the mouse pointer wherever it is.

The shortcut picker includes punctuation and separate numpad keys. For **Ctrl+/** on a US keyboard, select **Ctrl** and **/**. For **Ctrl+Shift+/**, also select **Shift**. If Windows has already assigned your combination, the settings window lets you choose another.

In the modern Settings panel, each PIN field has a **Show** button. Leaving the field and its button masks the digits again. You can return to the field and select **Show** again.

## Indicator and startup

By default the centered indicator stays visible until you disarm. It lets clicks pass through and keeps keyboard focus on your current app. To fade the overlay after two seconds, select **Fade the active indicator** under **Settings → Behavior**. The amber tray icon stays visible while the guard is armed.

Open **Settings → Behavior** and select **Start at Windows sign-in** to launch DeskPaw disarmed in your tray at future sign-ins. This is off by default and applies only to your Windows account. Select **Save settings** to apply changes across sections. If you move the app while startup is enabled, open the new copy and save Settings again to update the launch path.

## Try it before stepping away

Open **Test in a window** from the tray menu. For two minutes the filter applies inside that window. Ask Codex to click **Test click** and type a phrase into the text field. The counter and the text let you see whether automation works on your setup. You can also launch this check with `DeskPaw.exe --input-check`.

For a desktop-wide check, open a blank text editor, arm DeskPaw, and try a physical click and **Alt+Tab**. Type the PIN and confirm that control returns and the editor contains no PIN digits.

## PIN entry and recovery

**Backspace** removes the last digit. **Escape** clears the entry. A gap of more than ten seconds between keystrokes clears a partial entry. If someone has been tapping the keypad, press **Escape** and enter your full PIN.

By default, DeskPaw stays armed after any number of incorrect PINs and desktop automation can continue. This default also applies when upgrading from earlier versions. To have DeskPaw request a Windows lock on the fifth incorrect PIN, enable **Lock Windows after 5 incorrect PIN attempts** under **Settings → Behavior** and save. Only complete incorrect attempts count during an armed session. A Windows lock can pause desktop automation.

If you open Task Manager through **Ctrl+Alt+Delete**, close it or return to the ordinary desktop before entering your PIN. The guard checks the PIN when you release its final digit. If a Windows prompt interrupted you midway, press **Escape** on the ordinary desktop and re-enter the full PIN.

**Forgotten PIN:** Press **Ctrl+Alt+Delete**, choose **Lock**, and sign back into Windows. DeskPaw disarms when Windows reports that your session was unlocked. Open **Settings** in the tray menu to choose a new PIN. When changing only your shortcut, leave both PIN fields blank.

## Moving, updating, or removing DeskPaw

**DeskPaw.exe** runs the tray and input guard. It launches **DeskPaw.Settings.exe** when you open Settings and the helper exits when you close the panel. The helper embeds its HTML, CSS, and JavaScript. No installer, local web server, Node.js installation, or bundled browser is needed. You can carry just the tray EXE if you prefer the classic settings dialog.

Your shortcut and salted PIN verifier live in `%LOCALAPPDATA%\DeskPause\settings.dat`, protected by Windows for your user account. The app records no keystroke log. The Tauri helper uses a separate WebView2 profile under `%LOCALAPPDATA%\app.deskpause.settings`. Moving the executables to another folder on the same PC preserves your settings. On another PC or Windows account, choose your shortcut and PIN again.

To update, disarm the running copy and choose **Exit** from its tray menu. Extract the new ZIP and launch **DeskPaw.exe**. When upgrading from DeskPause with sign-in startup enabled, open Settings and save once to point the existing startup entry at DeskPaw. To remove it, turn off the sign-in option and save, then exit and delete the executables. To remove your saved configuration and WebView2 profile as well, delete the two app folders above. The optional startup entry keeps the name **DeskPause** for compatibility in the current user's Windows Run registry key.

## What the guard covers

DeskPaw helps prevent casual snooping and tampering on an unlocked PC. It keeps curious children and other passers-by from using the mouse or keyboard to switch windows, browse folders, or interrupt your work. **A determined attacker with physical access can bypass the guard.**

Anyone nearby can still read the monitors. Windows-marked injected keyboard and mouse events pass through, including events generated by other automation software. The filter applies to ordinary keyboard and mouse input on the interactive desktop. Touch, pen, game controllers, raw device input, elevated apps, and Windows security screens fall outside its guarantee. Use the Windows lock for authentication or screen privacy.

DeskPaw leaves Windows lock and sleep policies in effect. If Windows locks or the PC sleeps, DeskPaw cannot keep Windows Computer Use active. [OpenAI’s Computer Use guide](https://learn.chatgpt.com/docs/computer-use)

## Build and test

From PowerShell in the repository root, run the following on x64 Windows with .NET Framework 4.8 or later. The tray build uses the Windows .NET Framework C# compiler. The settings helper requires stable Rust for the MSVC target, the Visual Studio C++ build tools, and the Windows SDK. [Tauri Windows prerequisites](https://v2.tauri.app/start/prerequisites/)

```powershell
.\source\build.ps1
.\settings\build.ps1
.\tests\test.ps1
```

The scripts write both executables to the repository root. Exit that copy before rebuilding it. The automated checks exercise PIN validation and editing, protected settings, the panel’s save requests, and how the input filter handles physical and injected events. They use synthetic input and temporary test settings. To exercise Codex with the actual Windows hooks, use **Test in a window**.

DeskPaw is released under the [MIT License](LICENSE).
