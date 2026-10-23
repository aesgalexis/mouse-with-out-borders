using MouseWithOutBorders;
using System.Runtime.InteropServices;
using System.Text.Json;

static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid mapping was accepted."); }
KeyMapping[] mappings = [new(122, 8), new(123, 13)];
KeyCatalog.Validate(mappings);
Assert(JsonSerializer.Deserialize<KeyMapping[]>(JsonSerializer.Serialize(mappings))!.SequenceEqual(mappings), "Mappings did not survive JSON persistence.");
Reject(() => KeyCatalog.Validate([new(122, 8), new(122, 13)]));
Reject(() => KeyCatalog.Validate([new(123, 123)]));
Reject(() => KeyCatalog.Validate([new(0, 13)]));
Assert(KeyCatalog.Choices.Select(key => key.Code).Distinct().Count() == KeyCatalog.Choices.Length, "Duplicate keys in selectors.");
List<(int key, bool down)> events = [];
KeyboardRemapper engine = new((key, down) => events.Add((key, down)));
engine.Configure(true, mappings);
Assert(engine.Process(122, true) && engine.Process(122, true) && engine.Process(122, false), "Backspace source was not consumed.");
Assert(engine.Process(123, true) && engine.Process(123, false), "Enter source was not consumed.");
Assert(events.SequenceEqual(new[] { (8, true), (8, true), (8, false), (13, true), (13, false) }), "Repeat or target key transitions were wrong.");
Assert(!engine.Process(65, true) && !engine.Process(65, false), "An unmapped key was swallowed.");
events.Clear();
engine.Configure(true, [new(65, 66), new(66, 65)]);
engine.Process(65, true); engine.Process(65, false); engine.Process(66, true); engine.Process(66, false);
Assert(events.SequenceEqual(new[] { (66, true), (66, false), (65, true), (65, false) }), "Swapping two keys recursively remapped or lost input.");
events.Clear();
engine.Configure(true, [new(122, 13), new(123, 13)]);
engine.Process(122, true); engine.Process(123, true); engine.Process(122, false);
Assert(!events.Contains((13, false)), "One source released another source's held target.");
engine.Process(123, false);
Assert(events.Count(e => e == (13, false)) == 1, "The shared target was not released exactly once.");
events.Clear();
engine.Process(13, true); engine.Process(122, true); engine.Process(122, false);
Assert(!events.Contains((13, false)), "A remap released the physically held destination key.");
Assert(!engine.Process(13, false), "The last real target release was swallowed.");
events.Clear();
engine.Process(122, true); engine.Process(13, true);
Assert(engine.Process(13, false), "A real release interrupted an active remapped target.");
engine.Process(122, false);
Assert(events.Last() == (13, false), "The remaining mapped source left a stuck key.");
events.Clear();
engine.Configure(true, mappings);
engine.Process(123, true);
engine.Configure(false, mappings);
Assert(events.SequenceEqual(new[] { (13, true), (13, false) }), "Disabling left Enter pressed.");
Assert(engine.Process(123, true) && engine.Process(123, false), "Disable did not consume repeats/up belonging to the previous remap.");
Assert(!engine.Process(123, true) && !engine.Process(123, false), "Disabled remapping still swallowed keys.");
events.Clear();
engine.Configure(true, [new(122, 162), new(123, 13)]);
engine.Process(122, true); engine.Process(123, true); engine.Release();
Assert(events.Contains((162, false)) && events.Contains((13, false)), "Cleanup did not release all injected keys.");
bool fail = false;
List<int> attempted = [];
KeyboardRemapper rejected = new((key, down) => { if (!down) { attempted.Add(key); if (fail && key == 13) throw new Exception("Rejected"); } });
rejected.Configure(true, mappings); rejected.Process(122, true); rejected.Process(123, true); fail = true;
try { rejected.Configure(false, []); } catch (AggregateException) { }
Assert(attempted.Contains(8) && attempted.Contains(13), "One failure prevented cleanup of another held key.");
Assert(!rejected.Process(65, true), "A failed reset did not leave unmapped input usable.");
fail = false; rejected.Release();
NativeKeyboard.Inject(KeyCatalog.NumpadEnter, false, (count, input, size) =>
{
    var key = input[0].Data.Keyboard;
    Assert(count == 1 && size == Marshal.SizeOf<NativeMouse.Input>() && input[0].Type == 1 && key.Key == 13 &&
        key.Flags == 3 && key.ExtraInfo == NativeKeyboard.Marker, "Extended key-up or native INPUT layout was wrong.");
    return 1;
});
try { NativeKeyboard.Inject(13, true, (_, _, _) => 0); throw new Exception("Failed SendInput was ignored."); }
catch (System.ComponentModel.Win32Exception) { }
Console.WriteLine("PASS: selectors/validation, saved mappings, Enter/Backspace, repeat, swaps, shared targets, real-key collisions, disable/reconfigure/cleanup and native keyboard INPUT.");
