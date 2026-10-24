namespace MouseWithOutBorders;

internal sealed class KeyRemappingDialog : Form
{
    private readonly DarkCheckBox _enabled = new() { Name = "EnableKeyRemapping", Text = "Enable key remapping", Left = 24, Top = 64, Width = 540, Height = 28 };
    private readonly DarkComboBox _source = new() { Name = "SourceKey", Left = 24, Top = 134, Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DarkComboBox _target = new() { Name = "TargetKey", Left = 310, Top = 134, Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ListView _list = new()
    {
        Name = "KeyMappings", Left = 24, Top = 236, Width = 536, Height = 170,
        View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
        HeaderStyle = ColumnHeaderStyle.Nonclickable, BorderStyle = BorderStyle.FixedSingle,
        BackColor = DarkTheme.Surface, ForeColor = DarkTheme.Text, OwnerDraw = true
    };
    private readonly Label _message = new() { Name = "MappingMessage", Left = 24, Top = 416, Width = 536, Height = 46, ForeColor = DarkTheme.Accent };
    private readonly List<KeyMapping> _mappings;
    private readonly KeyChoice[] _choices = KeyCatalog.Choices.Select(NativeKeyboard.Describe).ToArray();
    internal IReadOnlyList<KeyMapping> Mappings => _mappings;
    internal bool RemappingEnabled => _enabled.Checked;

    internal KeyRemappingDialog(bool enabled, IReadOnlyList<KeyMapping> mappings)
    {
        _mappings = mappings.ToList();
        Text = "Remap keys — MWB";
        Icon = AppBranding.LoadIcon(SystemInformation.IconSize);
        ClientSize = new Size(584, 522);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = DarkTheme.Background;
        ForeColor = DarkTheme.Text;
        Font = new Font("Segoe UI", 10);
        AutoScroll = true;
        _enabled.Checked = enabled;
        _source.Items.AddRange(_choices.Cast<object>().ToArray());
        _target.Items.AddRange(_choices.Cast<object>().ToArray());
        _source.SelectedIndex = 0;
        _target.SelectedIndex = 0;
        _list.Columns.Add("Input key", 252);
        _list.Columns.Add("Output key", 252);
        _list.DrawColumnHeader += (_, e) =>
        {
            using SolidBrush fill = new(DarkTheme.Surface);
            e.Graphics.FillRectangle(fill, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header!.Text, Font, Rectangle.Inflate(e.Bounds, -8, 0),
                DarkTheme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        };
        _list.DrawItem += (_, _) => { };
        _list.DrawSubItem += (_, e) =>
        {
            bool selected = e.Item!.Selected;
            using SolidBrush fill = new(selected ? DarkTheme.Selected : DarkTheme.Surface);
            e.Graphics.FillRectangle(fill, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.SubItem!.Text, Font, Rectangle.Inflate(e.Bounds, -8, 0),
                DarkTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        };
        _list.SelectedIndexChanged += (_, _) =>
        {
            if (_list.SelectedIndices.Count == 0) return;
            KeyMapping mapping = _mappings[_list.SelectedIndices[0]];
            _source.SelectedItem = _choices.Single(key => key.Code == mapping.Source);
            _target.SelectedItem = _choices.Single(key => key.Code == mapping.Target);
        };
        Button add = Button("AddKeyMapping", "Add", 24, 184, 165, () => Upsert(false));
        Button replace = Button("ReplaceKeyMapping", "Change selected", 198, 184, 188, () => Upsert(true));
        Button remove = Button("RemoveKeyMapping", "Remove", 395, 184, 165, () =>
        {
            if (_list.SelectedIndices.Count == 0) { _message.Text = "Select a mapping in the list."; return; }
            _mappings.RemoveAt(_list.SelectedIndices[0]);
            RefreshList();
            _message.Text = "";
        });
        Button cancel = Button("CancelKeyMappings", "Cancel", 24, 470, 250, () => { DialogResult = DialogResult.Cancel; Close(); });
        Button save = Button("SaveKeyMappings", "Save", 310, 470, 250, () =>
        {
            try
            {
                KeyCatalog.Validate(_mappings);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (ArgumentException ex) { _message.Text = ex.Message; }
        });
        save.BackColor = DarkTheme.Accent;
        save.ForeColor = DarkTheme.Background;
        Controls.AddRange([
            Label("Remap keys", 24, 18, 536, 34, true),
            _enabled, Label("Input key", 24, 106, 250, 24),
            Label("Output key", 310, 106, 250, 24),
            _source, _target, add, replace, remove, _list, _message,
            cancel, save
        ]);
        RefreshList();
    }

    private void Upsert(bool replace)
    {
        if (replace && _list.SelectedIndices.Count == 0) { _message.Text = "Select a mapping to change it."; return; }
        KeyMapping mapping = new(((KeyChoice)_source.SelectedItem!).Code, ((KeyChoice)_target.SelectedItem!).Code);
        List<KeyMapping> candidate = _mappings.ToList();
        if (replace) candidate[_list.SelectedIndices[0]] = mapping;
        else candidate.Add(mapping);
        try { KeyCatalog.Validate(candidate); }
        catch (ArgumentException ex) { _message.Text = ex.Message; return; }
        _mappings.Clear();
        _mappings.AddRange(candidate);
        _enabled.Checked = true;
        RefreshList();
        _message.Text = "";
    }

    private void RefreshList()
    {
        _list.Items.Clear();
        foreach (KeyMapping map in _mappings)
            _list.Items.Add(new ListViewItem([_choices.Single(key => key.Code == map.Source).Name, _choices.Single(key => key.Code == map.Target).Name]));
        _list.Columns[1].Width = Math.Max(200, _list.ClientSize.Width - _list.Columns[0].Width);
    }
    private static Button Button(string name, string text, int x, int y, int width, Action action)
    {
        Button button = new DarkButton { Name = name, Text = text, Left = x, Top = y, Width = width, Height = 36,
            FlatStyle = FlatStyle.Flat, BackColor = DarkTheme.Surface, ForeColor = DarkTheme.Text };
        button.Click += (_, _) => action();
        return button;
    }
    private static Label Label(string text, int x, int y, int width, int height, bool heading = false) => new()
    {
        Text = text, Left = x, Top = y, Width = width, Height = height,
        ForeColor = heading ? DarkTheme.Text : DarkTheme.Muted,
        Font = new Font("Segoe UI", heading ? 17 : 9, heading ? FontStyle.Bold : FontStyle.Regular)
    };
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Height = Math.Min(Height, Screen.FromControl(this).WorkingArea.Height - 32);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) Icon?.Dispose();
        base.Dispose(disposing);
    }
}
