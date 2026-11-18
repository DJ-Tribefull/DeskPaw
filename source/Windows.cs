using System;
using System.Drawing;
using System.Windows.Forms;

namespace DeskPause
{
    internal sealed class SettingsForm : Form
    {
        private readonly CheckBox ctrl, alt, shift, win;
        private readonly ComboBox key = new ComboBox();
        private readonly TextBox pin, confirm;
        private readonly Label status;
        private readonly Preferences prior;
        private readonly Func<Preferences, string> save;

        internal SettingsForm(Preferences current, Func<Preferences, string> save, string notice)
        {
            prior = current;
            this.save = save;
            Preferences initial = current ?? new Preferences();
            Text = "DeskPause settings";
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(482, 455);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            var title = AddLabel("Set up DeskPause", 22, 18, 442, 37);
            title.Font = new Font(Font.FontFamily, 17, FontStyle.Bold);
            AddLabel("Arm with a shortcut. Type your PIN anywhere to resume.", 24, 63, 435, 28);
            var hot = AddLabel("Arming shortcut", 24, 108, 250, 24);
            hot.Font = new Font(Font, FontStyle.Bold);
            ctrl = AddModifier("Ctrl", 24, (initial.Mods & 2) != 0);
            alt = AddModifier("Alt", 94, (initial.Mods & 1) != 0);
            shift = AddModifier("Shift", 158, (initial.Mods & 4) != 0);
            win = AddModifier("Win", 233, (initial.Mods & 8) != 0);
            key.DropDownStyle = ComboBoxStyle.DropDownList;
            key.SetBounds(327, 137, 110, 30);
            for (int i = 0; i < 256; i++) if (Preferences.ValidKey((Keys)i)) key.Items.Add((Keys)i);
            key.Format += delegate(object sender, ListControlConvertEventArgs e)
            {
                e.Value = Preferences.KeyLabel((Keys)e.ListItem);
            };
            key.FormattingEnabled = true;
            key.SelectedItem = initial.Key;
            Controls.Add(key);
            AddLabel(current == null ? "Seven-digit PIN" : "New seven-digit PIN", 24, 201, 197, 25);
            AddLabel("Confirm PIN", 245, 201, 200, 25);
            pin = AddPin(24);
            confirm = AddPin(245);
            AddLabel(current == null ? "Choose a PIN just for DeskPause. Leading zeroes are allowed." :
                "Leave both PIN fields empty to keep your current PIN.", 24, 267, 436, 45);
            status = AddLabel(notice ?? "", 24, 317, 435, 60);
            status.AccessibleName = "Settings status";
            status.ForeColor = Color.DarkRed;
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
            cancel.SetBounds(240, 392, 93, 37);
            var ok = new Button { Text = "Save", UseVisualStyleBackColor = true };
            ok.SetBounds(344, 392, 93, 37);
            ok.Click += SaveSettings;
            Controls.Add(cancel);
            Controls.Add(ok);
            CancelButton = cancel;
            AcceptButton = ok;
        }

        private Label AddLabel(string text, int x, int y, int w, int h)
        {
            var label = new Label { Text = text };
            label.SetBounds(x, y, w, h);
            Controls.Add(label);
            return label;
        }
        private CheckBox AddModifier(string name, int x, bool initial)
        {
            var box = new CheckBox { Text = name, Checked = initial, AutoSize = true };
            box.Location = new Point(x, 141);
            Controls.Add(box);
            return box;
        }
        private TextBox AddPin(int x)
        {
            var input = new TextBox { UseSystemPasswordChar = true, AccessibleName = x == 24 ? "Seven-digit PIN" : "Confirm PIN" };
            input.SetBounds(x, 231, 192, 29);
            Controls.Add(input);
            return input;
        }
        private void SaveSettings(object sender, EventArgs e)
        {
            uint mods = (uint)((ctrl.Checked ? 2 : 0) | (alt.Checked ? 1 : 0) |
                (shift.Checked ? 4 : 0) | (win.Checked ? 8 : 0));
            if (mods == 0) { status.Text = "Select at least one shortcut modifier."; return; }
            var next = new Preferences { Mods = mods, Key = (Keys)key.SelectedItem };
            if (prior != null && pin.TextLength == 0 && confirm.TextLength == 0)
            {
                next.Salt = prior.Salt;
                next.Hash = prior.Hash;
            }
            else
            {
                if (!Preferences.SevenDigits(pin.Text)) { status.Text = "Enter exactly seven digits from 0 to 9."; pin.Focus(); return; }
                if (pin.Text != confirm.Text) { status.Text = "The PINs differ. Enter the same seven digits in both fields."; confirm.Focus(); return; }
            }
            try
            {
                if (next.Hash == null) next.SetPin(pin.Text);
                string error = save(next);
                if (error != null) { status.Text = error; return; }
                pin.Clear();
                confirm.Clear();
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex) { status.Text = ex.Message; }
        }
    }

    internal sealed class InputCheck : Form
    {
        private InputGuard guard;
        private readonly Timer timer = new Timer { Interval = 250 };
        private readonly Label result = new Label();
        private readonly Label activity = new Label();
        private int clicks;
        private DateTime until;
        internal InputCheck()
        {
            Text = "DeskPause input check";
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(490, 260);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            var heading = new Label { Text = "Codex input check", Font = new Font(Font, FontStyle.Bold) };
            heading.SetBounds(20, 17, 440, 23);
            var hint = new Label { Text = "Filtering applies inside this window for two minutes. Other apps remain usable." };
            hint.SetBounds(20, 46, 449, 50);
            var click = new Button { Text = "Test click", AccessibleName = "Test click" };
            click.SetBounds(20, 101, 115, 34);
            click.Click += delegate { result.Text = "Clicks received: " + (++clicks); };
            result.Text = "Clicks received: 0";
            result.SetBounds(149, 109, 280, 23);
            var field = new TextBox { AccessibleName = "Test typing" };
            field.SetBounds(20, 151, 441, 32);
            activity.SetBounds(20, 207, 455, 40);
            Controls.AddRange(new Control[] { heading, hint, click, result, field, activity });
            Shown += delegate
            {
                guard = new InputGuard(null, Handle);
                try { guard.Start(); }
                catch (Exception ex) { activity.Text = "Check unavailable. " + ex.Message; guard.Dispose(); guard = null; return; }
                until = DateTime.UtcNow.AddMinutes(2);
                activity.Text = "Filter armed in this window.";
                timer.Start();
            };
            timer.Tick += delegate
            {
                if (DateTime.UtcNow >= until)
                {
                    timer.Stop();
                    guard.Dispose();
                    activity.Text = "Filter released. You can close this window.";
                    return;
                }
                activity.Text = "Filter armed  |  Injected mouse events " + guard.InjectedMouse +
                    "  |  Injected key events " + guard.InjectedKeys;
            };
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); if (guard != null) guard.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
