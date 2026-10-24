namespace MouseWithOutBorders;

internal sealed record KeyMapping(int Source, int Target);
internal sealed record KeyChoice(int Code, string Name)
{
    public override string ToString() => Name;
}

internal static class KeyCatalog
{
    internal const int NumpadEnter = 0x1000D;
    internal static readonly KeyChoice[] Choices = Build();
    internal static bool Valid(int code) => Choices.Any(key => key.Code == code);
    internal static string Name(int code) => Choices.FirstOrDefault(key => key.Code == code)?.Name ?? $"Key {code}";
    internal static bool Extended(int code) => code == NumpadEnter || code is 33 or 34 or 35 or 36 or 37 or 38 or 39 or 40 or
        44 or 45 or 46 or 91 or 92 or 93 or 111 or 144 or 163 or 165;

    internal static void Validate(IReadOnlyList<KeyMapping> mappings)
    {
        if (mappings.Count > 128) throw new ArgumentException("You can save up to 128 key mappings.");
        HashSet<int> sources = [];
        foreach (KeyMapping mapping in mappings)
        {
            if (mapping is null || !Valid(mapping.Source) || !Valid(mapping.Target))
                throw new ArgumentException("Select a valid input key and output key.");
            if (mapping.Source == mapping.Target) throw new ArgumentException("Select two different keys.");
            if (!sources.Add(mapping.Source)) throw new ArgumentException($"{Name(mapping.Source)} already has a mapping. Select it in the list to change it.");
        }
    }

    private static KeyChoice[] Build()
    {
        List<KeyChoice> keys = [new(13, "Enter"), new(8, "Backspace"), new(9, "Tab"), new(27, "Escape"),
            new(32, "Space"), new(20, "Caps Lock"), new(46, "Delete"), new(45, "Insert"), new(36, "Home"),
            new(35, "End"), new(33, "Page Up"), new(34, "Page Down"), new(37, "Left arrow"), new(38, "Up arrow"),
            new(39, "Right arrow"), new(40, "Down arrow"), new(44, "Print Screen"), new(19, "Pause"),
            new(145, "Scroll Lock"), new(144, "Num Lock"), new(NumpadEnter, "Numpad Enter")];
        for (int i = 1; i <= 24; i++) keys.Add(new(111 + i, "F" + i));
        for (int i = 65; i <= 90; i++) keys.Add(new(i, ((char)i).ToString()));
        for (int i = 48; i <= 57; i++) keys.Add(new(i, ((char)i).ToString()));
        for (int i = 0; i <= 9; i++) keys.Add(new(96 + i, "Numpad " + i));
        keys.AddRange([new(106, "Numpad *"), new(107, "Numpad +"), new(109, "Numpad -"), new(110, "Numpad decimal"),
            new(111, "Numpad /"), new(12, "Clear"), new(160, "Left Shift"), new(161, "Right Shift"),
            new(162, "Left Ctrl"), new(163, "Right Ctrl"), new(164, "Left Alt"), new(165, "Right Alt / AltGr"),
            new(91, "Left Windows"), new(92, "Right Windows"), new(93, "Menu / Application")]);
        string[] media = ["Browser Back", "Browser Forward", "Browser Refresh", "Browser Stop", "Browser Search", "Browser Favorites", "Browser Home",
            "Mute", "Volume Down", "Volume Up", "Next Track", "Previous Track", "Stop Media", "Play / Pause", "Mail", "Media Select", "Launch App 1", "Launch App 2"];
        for (int i = 0; i < media.Length; i++) keys.Add(new(166 + i, media[i]));
        foreach (int code in new[] { 186, 187, 188, 189, 190, 191, 192, 219, 220, 221, 222, 223, 226 })
            keys.Add(new(code, $"OEM {code} (keyboard layout)"));
        return keys.ToArray();
    }
}

// Receives physical key transitions only. Generated input is filtered by the
// native hook, so swaps and chains never remap a generated key a second time.
internal sealed class KeyboardRemapper
{
    private sealed record Press(int Target, bool Mapped);
    private readonly Action<int, bool> _send;
    private readonly Dictionary<int, Press> _pressed = [];
    private readonly HashSet<int> _blockedUntilUp = [];
    private readonly Dictionary<int, long> _injected = [];
    private readonly Dictionary<int, long> _targetVersions = [];
    private Dictionary<int, int> _mappings = [];
    private long _configuration;
    private long _injection;

    internal KeyboardRemapper(Action<int, bool> send) => _send = send;

    internal void Configure(bool enabled, IReadOnlyList<KeyMapping> mappings)
    {
        KeyCatalog.Validate(mappings);
        long configuration = ++_configuration;
        _mappings = [];
        Release();
        // SendInput may pump another hook/configuration before returning.
        if (_configuration == configuration)
            _mappings = enabled ? mappings.ToDictionary(map => map.Source, map => map.Target) : [];
    }

    internal bool ShouldSuppress(int source, bool down)
    {
        if (_blockedUntilUp.Contains(source)) return true;
        if (_pressed.TryGetValue(source, out Press? press))
            return press.Mapped || (!down && _pressed.Any(entry => entry.Key != source && entry.Value.Target == press.Target));
        return down ? _mappings.ContainsKey(source) : _pressed.Values.Any(press => press.Target == source);
    }

    internal bool Process(int source, bool down)
    {
        if (_blockedUntilUp.Contains(source))
        {
            if (!down) _blockedUntilUp.Remove(source);
            return true;
        }
        if (down)
        {
            if (_pressed.TryGetValue(source, out Press? held))
            {
                if (held.Mapped) Send(held.Target, true); // Windows key repeat
                return held.Mapped;
            }
            bool mapped = _mappings.TryGetValue(source, out int target);
            if (!mapped) target = source;
            // Reserve the source before injection: a reentrant repeat/up must see
            // this press, and a failing send must still suppress the original key.
            _pressed.Add(source, new(target, mapped));
            if (mapped) Send(target, true);
            return mapped;
        }
        if (!_pressed.TryGetValue(source, out Press? released))
            return _pressed.Values.Any(press => press.Target == source);
        bool stillHeld = _pressed.Any(entry => entry.Key != source && entry.Value.Target == released.Target);
        _pressed.Remove(source);
        if (released.Mapped && !stillHeld) Send(released.Target, false);
        // A real target key must not release a key another remapped source holds.
        if (!released.Mapped && !stillHeld) ForgetTarget(source);
        return released.Mapped || stillHeld;
    }

    private void Send(int target, bool down)
    {
        long injection = ++_injection;
        _targetVersions[target] = injection;
        if (down) _injected[target] = injection;
        else _injected.Remove(target);
        try { _send(target, down); }
        catch
        {
            // A failed/partial injection may have reached Windows. Retain it for
            // cleanup, including failed key-up, rather than leaking a held key.
            if (_targetVersions[target] == injection) _injected[target] = injection;
            throw;
        }
    }

    private void ForgetTarget(int target)
    {
        _targetVersions[target] = ++_injection;
        _injected.Remove(target);
    }

    internal void Release()
    {
        List<Exception> errors = [];
        foreach (var entry in _pressed.Where(entry => entry.Value.Mapped).ToArray())
        {
            _blockedUntilUp.Add(entry.Key);
            _pressed.Remove(entry.Key);
        }
        foreach ((int target, long injection) in _injected.ToArray())
        {
            if (!_injected.TryGetValue(target, out long current) || current != injection) continue;
            if (_pressed.Any(entry => !entry.Value.Mapped && entry.Value.Target == target))
            {
                ForgetTarget(target);
                continue;
            }
            try { Send(target, false); }
            catch (Exception ex) { errors.Add(ex); }
        }
        if (errors.Count != 0) throw new AggregateException("Windows could not release every remapped key.", errors);
    }
}
