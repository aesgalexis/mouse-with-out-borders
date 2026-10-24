using MouseWithOutBorders;

internal static class ReentrancyTests
{
    internal static void Run()
    {
        SameSourceReentersDuringDown();
        KeyUpReentersDuringDown();
        DisableReentersDuringDown();
        ReconfigureReentersDuringDown();
        NewConfigurationSurvivesOlderConfigure();
        ReleaseReentersDuringRelease();
        NewHoldSurvivesOldReleaseSnapshot();
        FailedMappedInputRemainsSuppressed();
        FailedKeyUpRemainsAvailableForCleanup();
        FailedOldInputDoesNotResurrectReleasedState();
        EveryMappingConsumesItsSource();
        OemMappingsWithEveryModifierCombination();
        Console.WriteLine("PASS: reentrant keyboard down/up/configure/release, failure suppression, every key pair and OEM mappings with all Shift/Ctrl/Alt/AltGr/Windows modifier combinations.");
    }

    private static void Assert(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private static void SameSourceReentersDuringDown()
    {
        List<(int key, bool down)> events = [];
        KeyboardRemapper? engine = null;
        bool reentered = false;
        engine = new((key, down) =>
        {
            events.Add((key, down));
            if (!down || reentered) return;
            reentered = true;
            Assert(engine!.Process(191, true), "A reentrant mapped repeat leaked its original printable key.");
        });
        engine.Configure(true, [new(191, 8)]);
        Assert(engine.Process(191, true), "The original printable key leaked after a reentrant repeat.");
        Assert(engine.Process(191, false), "A reentrant mapped press lost its matching release.");
        Assert(events.SequenceEqual(new[] { (8, true), (8, true), (8, false) }),
            "Reentrant down did not preserve exactly two destination downs and one up.");
    }

    private static void KeyUpReentersDuringDown()
    {
        List<(int key, bool down)> events = [];
        KeyboardRemapper? engine = null;
        bool reentered = false;
        engine = new((key, down) =>
        {
            events.Add((key, down));
            if (!down || reentered) return;
            reentered = true;
            Assert(engine!.Process(191, false), "An up arriving during down injection was not consumed.");
        });
        engine.Configure(true, [new(191, 8)]);
        Assert(engine.Process(191, true), "A down leaked its printable source after a nested key-up.");
        engine.Release();
        Assert(events.SequenceEqual(new[] { (8, true), (8, false) }),
            "A nested key-up left stale state or caused a duplicate destination release.");
        Assert(engine.Process(191, true) && engine.Process(191, false), "The next press did not work after a nested key-up.");
        Assert(events.SequenceEqual(new[] { (8, true), (8, false), (8, true), (8, false) }),
            "A nested key-up poisoned the next press.");
    }

    private static void DisableReentersDuringDown()
    {
        List<(int key, bool down)> events = [];
        KeyboardRemapper? engine = null;
        bool reentered = false;
        engine = new((key, down) =>
        {
            events.Add((key, down));
            if (!down || reentered) return;
            reentered = true;
            engine!.Configure(false, []);
        });
        engine.Configure(true, [new(191, 8)]);
        Assert(engine.Process(191, true), "Disabling during injection leaked the original source.");
        Assert(engine.Process(191, true) && engine.Process(191, false),
            "Disabling during injection did not consume the old press's repeats and up.");
        engine.Release();
        Assert(events.SequenceEqual(new[] { (8, true), (8, false) }),
            "Disabling during injection left a target held or sent a stale release.");
        Assert(!engine.Process(191, true) && !engine.Process(191, false),
            "A new press stayed blocked after the old disabled press ended.");
    }

    private static void ReconfigureReentersDuringDown()
    {
        List<(int key, bool down)> events = [];
        KeyboardRemapper? engine = null;
        bool reentered = false;
        engine = new((key, down) =>
        {
            events.Add((key, down));
            if (!down || reentered) return;
            reentered = true;
            engine!.Configure(true, [new(191, 46)]);
        });
        engine.Configure(true, [new(191, 8)]);
        Assert(engine.Process(191, true), "Reconfiguring during injection leaked the old source.");
        Assert(engine.Process(191, true) && engine.Process(191, false), "Reconfiguring released the old press into the new mapping.");
        Assert(engine.Process(191, true) && engine.Process(191, false), "The replacement mapping did not activate for the next press.");
        engine.Release();
        Assert(events.SequenceEqual(new[] { (8, true), (8, false), (46, true), (46, false) }),
            "An outer injection overwrote state created by reentrant reconfiguration.");
    }

    private static void ReleaseReentersDuringRelease()
    {
        List<(int key, bool down)> events = [];
        KeyboardRemapper? engine = null;
        bool reentered = false;
        engine = new((key, down) =>
        {
            events.Add((key, down));
            if (down || reentered) return;
            reentered = true;
            engine!.Release();
        });
        engine.Configure(true, [new(191, 8), new(190, 46)]);
        Assert(engine.Process(191, true) && engine.Process(190, true), "The release test could not start both remaps.");
        engine.Release();
        engine.Release();
        Assert(events.Count(e => e == (8, false)) == 1 && events.Count(e => e == (46, false)) == 1,
            "Reentrant cleanup released a destination twice or missed a held destination.");
        Assert(engine.Process(191, false) && engine.Process(190, false), "Cleanup leaked the physical source releases.");
    }

    private static void NewConfigurationSurvivesOlderConfigure()
    {
        List<(int key, bool down)> events = [];
        KeyboardRemapper? engine = null;
        bool reentered = false;
        engine = new((key, down) =>
        {
            events.Add((key, down));
            if (down || reentered) return;
            reentered = true;
            engine!.Configure(true, [new(190, 46)]);
        });
        engine.Configure(true, [new(191, 8)]);
        engine.Process(191, true);
        engine.Configure(true, [new(191, 13)]);
        Assert(engine.Process(191, false), "A reentrant configuration leaked the previous source release.");
        Assert(engine.Process(190, true) && engine.Process(190, false), "An older configure overwrote the newer reentrant mapping.");
        Assert(!engine.Process(191, true) && !engine.Process(191, false), "The older replacement mapping escaped the newer configuration.");
        Assert(events.SequenceEqual(new[] { (8, true), (8, false), (46, true), (46, false) }),
            "Reentrant configuration changed an already completed destination or retained an obsolete mapping.");
    }

    private static void FailedMappedInputRemainsSuppressed()
    {
        List<(int key, bool down)> events = [];
        bool fail = true;
        KeyboardRemapper engine = new((key, down) =>
        {
            events.Add((key, down));
            if (down && fail) throw new IOException("Simulated native input failure after beginning delivery.");
        });
        engine.Configure(true, [new(191, 8)]);
        bool suppress = engine.ShouldSuppress(191, true);
        try
        {
            engine.Process(191, true);
            throw new Exception("A simulated native injection failure was ignored.");
        }
        catch (IOException)
        {
            fail = false;
            engine.Configure(false, []);
        }
        Assert(suppress, "The error path would forward the original printable source after attempting its target.");
        Assert(engine.Process(191, true) && engine.Process(191, false),
            "The error path leaked repeats or an up belonging to an already remapped press.");
        engine.Release();
        Assert(events.Contains((8, false)), "The error path could not clean up a possibly delivered destination down.");
        Assert(!engine.Process(191, true) && !engine.Process(191, false), "A failed mapping blocked later keys after it was disabled.");
    }

    private static void NewHoldSurvivesOldReleaseSnapshot()
    {
        List<(int key, bool down)> events = [];
        KeyboardRemapper? engine = null;
        bool reentered = false;
        engine = new((key, down) =>
        {
            events.Add((key, down));
            if (down || key != 8 || reentered) return;
            reentered = true;
            engine!.Release();
            Assert(engine.Process(188, true), "A new source was not mapped during reentrant cleanup.");
        });
        engine.Configure(true, [new(191, 8), new(190, 46), new(188, 46)]);
        engine.Process(191, true);
        engine.Process(190, true);
        engine.Release();
        Assert(events.SequenceEqual(new[] { (8, true), (46, true), (8, false), (46, false), (46, true) }),
            "A stale cleanup snapshot released a new hold of the same destination.");
        Assert(engine.Process(188, false), "A new press lost its release after reentrant cleanup.");
        Assert(events.Last() == (46, false) && events.Count(e => e == (46, false)) == 2,
            "The new destination hold did not end exactly once with its own source release.");
        Assert(engine.Process(191, false) && engine.Process(190, false), "Old source releases leaked after reentrant cleanup.");
    }

    private static void FailedKeyUpRemainsAvailableForCleanup()
    {
        List<(int key, bool down)> events = [];
        bool fail = true;
        KeyboardRemapper engine = new((key, down) =>
        {
            events.Add((key, down));
            if (!down && fail) throw new IOException("Simulated rejected key-up.");
        });
        engine.Configure(true, [new(191, 8)]);
        Assert(engine.Process(191, true), "A mapped down leaked before the key-up failure test.");
        bool suppress = engine.ShouldSuppress(191, false);
        try
        {
            engine.Process(191, false);
            throw new Exception("A simulated rejected key-up was ignored.");
        }
        catch (IOException)
        {
            fail = false;
            engine.Configure(false, []);
        }
        engine.Release();
        Assert(suppress, "The failure path would forward the source key-up of a mapped press.");
        Assert(events.SequenceEqual(new[] { (8, true), (8, false), (8, false) }),
            "A failed destination key-up was lost or retried after successful cleanup.");
        Assert(!engine.Process(191, true) && !engine.Process(191, false), "A rejected key-up blocked future physical input.");
    }

    private static void FailedOldInputDoesNotResurrectReleasedState()
    {
        List<(int key, bool down)> events = [];
        KeyboardRemapper? engine = null;
        bool reentered = false;
        engine = new((key, down) =>
        {
            events.Add((key, down));
            if (!down || reentered) return;
            reentered = true;
            engine!.Configure(false, []);
            throw new IOException("Simulated late failure after a nested release completed.");
        });
        engine.Configure(true, [new(191, 8)]);
        bool suppress = engine.ShouldSuppress(191, true);
        try
        {
            engine.Process(191, true);
            throw new Exception("A simulated late native input failure was ignored.");
        }
        catch (IOException) { engine.Configure(false, []); }
        engine.Release();
        Assert(suppress && engine.Process(191, false), "A late failure leaked its mapped source.");
        Assert(events.SequenceEqual(new[] { (8, true), (8, false) }),
            "A failed old send resurrected already released state after a nested disable.");
    }

    private static void EveryMappingConsumesItsSource()
    {
        List<(int key, bool down)> events = [];
        KeyboardRemapper engine = new((key, down) => events.Add((key, down)));
        foreach (KeyChoice source in KeyCatalog.Choices)
        foreach (KeyChoice target in KeyCatalog.Choices)
        {
            if (source.Code == target.Code) continue;
            events.Clear();
            engine.Configure(true, [new(source.Code, target.Code)]);
            Assert(engine.ShouldSuppress(source.Code, true) && engine.Process(source.Code, true) &&
                engine.ShouldSuppress(source.Code, true) && engine.Process(source.Code, true) &&
                engine.ShouldSuppress(source.Code, false) && engine.Process(source.Code, false),
                $"Mapping {source.Name} to {target.Name} leaked an original down, repeat or up.");
            Assert(events.SequenceEqual(new[] { (target.Code, true), (target.Code, true), (target.Code, false) }),
                $"Mapping {source.Name} to {target.Name} produced something other than its destination.");
        }
    }

    private static void OemMappingsWithEveryModifierCombination()
    {
        int[] modifiers = [160, 161, 162, 163, 164, 165, 91, 92];
        int[] oemKeys = [186, 187, 188, 189, 190, 191, 192, 219, 220, 221, 222, 223, 226];
        List<(int key, bool down)> events = [];
        KeyboardRemapper engine = new((key, down) => events.Add((key, down)));
        foreach (int source in oemKeys)
        foreach (int target in new[] { 8, 46 })
        for (int combination = 0; combination < 1 << modifiers.Length; combination++)
        {
            foreach (bool releaseModifiersFirst in new[] { false, true })
            {
                events.Clear();
                engine.Configure(true, [new(source, target)]);
                int[] held = modifiers.Where((_, index) => (combination & 1 << index) != 0).ToArray();
                foreach (int modifier in held)
                    Assert(!engine.ShouldSuppress(modifier, true) && !engine.Process(modifier, true), "An unmapped modifier down was swallowed.");
                Assert(engine.Process(source, true) && engine.Process(source, true), "A modified OEM key leaked a printable source down or repeat.");
                if (releaseModifiersFirst) ReleaseModifiers();
                Assert(engine.Process(source, false), "A modified OEM key leaked its source release.");
                if (!releaseModifiersFirst) ReleaseModifiers();
                Assert(events.SequenceEqual(new[] { (target, true), (target, true), (target, false) }),
                    $"OEM {source} to {target} with modifier mask {combination} emitted an original key or lost a target transition.");

                void ReleaseModifiers()
                {
                    foreach (int modifier in held.Reverse())
                        Assert(!engine.ShouldSuppress(modifier, false) && !engine.Process(modifier, false), "An unmapped modifier up was swallowed.");
                }
            }
        }
    }
}
