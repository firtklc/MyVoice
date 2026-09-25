using MyVoice.Windows.Core;
using MyVoice.Windows.Platform;

namespace MyVoice.Windows;

/// <summary>What the Settings window asks of the tray app.</summary>
/// <param name="BeginShortcutCapture">Frees the live shortcut so pressing it is recorded; false during a dictation.</param>
/// <param name="ApplyShortcut">Registers and saves a new shortcut; returns null, or why it didn't work.</param>
/// <param name="EndShortcutCapture">Takes the (possibly new) shortcut back.</param>
/// <param name="SetLanguage">Saves a language code ("auto", "en", "tr").</param>
sealed record SettingsActions(Func<bool> BeginShortcutCapture, Func<Hotkey, string?> ApplyShortcut, Action EndShortcutCapture, Action<string> SetLanguage);

/// <summary>
/// Settings (the Mac's SettingsView): the dictation shortcut recorder and the language. Changes apply at once.
/// Unlike the overlay, this is an ordinary window that takes focus — dictating while it has focus leaves the text on
/// the clipboard (MyVoice never pastes into itself).
/// </summary>
sealed class SettingsForm : Form
{
    const int WM_SYSCOMMAND = 0x112, SC_KEYMENU = 0xF100;
    const string ShortcutHint = "Click the box, then press the new shortcut.";

    readonly SettingsActions _actions;
    readonly ShortcutBox _shortcutBox = new();
    readonly Label _shortcutNote = new() { AutoSize = true, MaximumSize = new Size(340, 0), Text = ShortcutHint };
    readonly Dictionary<string, RadioButton> _languages = [];
    readonly Button _closeButton = new() { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
    readonly Font _bold = new(SystemFonts.MessageBoxFont!, FontStyle.Bold);
    Hotkey _current = Hotkey.Default;
    bool _showingState;

    public SettingsForm(SettingsActions actions, Icon icon)
    {
        _actions = actions;
        SuspendLayout();
        Text = "MyVoice Settings";
        Icon = icon;
        Font = SystemFonts.MessageBoxFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        CancelButton = _closeButton;

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        layout.Controls.Add(Heading("Dictation shortcut"));
        _shortcutBox.Width = 240;
        _shortcutBox.Margin = new Padding(3, 6, 3, 3);
        layout.Controls.Add(_shortcutBox);
        _shortcutNote.ForeColor = SystemColors.GrayText;
        layout.Controls.Add(_shortcutNote);

        var language = Heading("Language");
        language.Margin = new Padding(3, 18, 3, 3);
        layout.Controls.Add(language);
        var choices = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 3, 0, 0) };
        foreach (var preference in LanguagePreference.All)
        {
            var radio = new RadioButton { Text = preference.DisplayName, AutoSize = true, Tag = preference.Code };
            radio.CheckedChanged += (_, _) => { if (radio.Checked && !_showingState) _actions.SetLanguage((string)radio.Tag!); };
            _languages[preference.Code] = radio;
            choices.Controls.Add(radio);
        }
        layout.Controls.Add(choices);
        layout.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(340, 0), ForeColor = SystemColors.GrayText,
            Text = "Auto-detect picks the language from your audio. Pick a specific language if auto-detect guesses wrong.",
        });

        _closeButton.Anchor = AnchorStyles.Right;
        _closeButton.Margin = new Padding(3, 18, 3, 3);
        _closeButton.Click += (_, _) => Close();
        layout.Controls.Add(_closeButton);
        Controls.Add(layout);
        ActiveControl = _closeButton; // don't start recording a shortcut just because the window opened

        _shortcutBox.Enter += (_, _) => BeginCapture();
        _shortcutBox.MouseDown += (_, _) => BeginCapture(); // already focused (after Esc or a switch away): Enter won't fire
        _shortcutBox.Leave += (_, _) => EndCapture(moveFocus: false);
        _shortcutBox.KeyPressed += OnShortcutKey;
        Deactivate += (_, _) => EndCapture();   // never leave the shortcut unregistered while the user works elsewhere
        FormClosing += (_, _) => EndCapture(moveFocus: false);

        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();
    }

    Label Heading(string text) => new() { Text = text, AutoSize = true, Font = _bold };

    /// <summary>Mirrors the app's state; called on every UI update.</summary>
    public void ShowState(Hotkey current, string languageCode, bool canChange)
    {
        _current = current;
        if (!_shortcutBox.Capturing) _shortcutBox.Text = current.Display(KeyLabels.ForCurrentLayout);
        _shortcutBox.Enabled = canChange || _shortcutBox.Capturing;
        _showingState = true;
        foreach (var (code, radio) in _languages)
        {
            radio.Checked = code == languageCode;
            radio.Enabled = canChange;
        }
        _showingState = false;
    }

    void BeginCapture()
    {
        if (_shortcutBox.Capturing) return;
        if (!_actions.BeginShortcutCapture())
        {
            Note("Finish the current dictation first.", error: true);
            BeginInvoke(() => ActiveControl = _closeButton); // WinForms: never move focus inside an Enter handler
            return;
        }
        _shortcutBox.Capturing = true;
        _shortcutBox.Text = "Press the new shortcut…";
        Note("Press the keys together. Esc keeps the current shortcut. If nothing appears, another app already uses those keys.");
    }

    void OnShortcutKey(Keys keyData, bool win)
    {
        switch (HotkeyCapture.KeyDown(keyData, win, KeyLabels.ForCurrentLayout))
        {
            case CaptureHeld(var text):
                _shortcutBox.Text = text;
                break;
            case CaptureRejected(var text, var reason):
                _shortcutBox.Text = text;
                Note(reason, error: true);
                break;
            case CaptureChosen(var hotkey):
                var display = hotkey.Display(KeyLabels.ForCurrentLayout);
                if (_actions.ApplyShortcut(hotkey) is { } problem)
                {
                    _shortcutBox.Text = display;
                    Note(problem, error: true);
                    break;
                }
                _current = hotkey;
                EndCapture();
                Note($"Saved — {display} now starts and stops dictation.");
                break;
            case CaptureCancelled:
                EndCapture();
                Note(ShortcutHint);
                break;
        }
    }

    /// <param name="moveFocus">Off the box, so coming back to the window doesn't look like recording.
    /// False from Leave and FormClosing, where focus is already moving.</param>
    void EndCapture(bool moveFocus = true)
    {
        if (!_shortcutBox.Capturing) return;
        _shortcutBox.Capturing = false;
        _actions.EndShortcutCapture();
        _shortcutBox.Text = _current.Display(KeyLabels.ForCurrentLayout);
        if (moveFocus) ActiveControl = _closeButton;
    }

    void Note(string text, bool error = false)
    {
        _shortcutNote.Text = text;
        _shortcutNote.ForeColor = error ? Color.Firebrick : SystemColors.GrayText;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _bold.Dispose();
        base.Dispose(disposing);
    }

    protected override void WndProc(ref Message m)
    {
        // A swallowed Alt+key still makes Windows open the window menu on Alt's release; not while recording.
        if (m.Msg == WM_SYSCOMMAND && ((int)m.WParam & 0xFFF0) == SC_KEYMENU && _shortcutBox.Capturing) return;
        base.WndProc(ref m);
    }

    /// <summary>A read-only box that, while <see cref="Capturing"/>, receives every key — Tab, Enter, Esc and Alt
    /// combinations included — instead of letting the dialog act on them.</summary>
    sealed class ShortcutBox : TextBox
    {
        const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;

        public ShortcutBox()
        {
            ReadOnly = true;
            ShortcutsEnabled = false;
            TextAlign = HorizontalAlignment.Center;
            Cursor = Cursors.Hand;
            BackColor = SystemColors.Window;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Capturing { get; set; }

        public event Action<Keys, bool>? KeyPressed;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!Capturing) return base.ProcessCmdKey(ref msg, keyData);
            var win = NativeMethods.GetKeyState(VK_LWIN) < 0 || NativeMethods.GetKeyState(VK_RWIN) < 0;
            KeyPressed?.Invoke(keyData, win);
            return true;
        }
    }
}
