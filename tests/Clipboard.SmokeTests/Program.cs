using MouseWithOutBorders;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics;

string sandbox = Path.Combine(AppContext.BaseDirectory, "mwb-clipboard-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(sandbox);
try
{
    await TextAndFilesRoundTrip();
    await RejectUntrustedMetadataAndChunks();
    await SourceValidationAndCancellation();
    await CacheBudgetAndPersistence();
    await FailedPartialCleanupKeepsCacheReservation();
    await JunctionAndLifecycleBoundaries();
    Console.WriteLine("PASS: bounded Unicode text and files, JSON wire chunks, checksums, ordered chunks, malformed metadata, paths/links, cancellation/disconnection, cache retention and disk budget.");
}
finally { Directory.Delete(sandbox, true); }

void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
string Root(string name) => Path.Combine(sandbox, name);
string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
Task NoSend(ClipboardMessage _, CancellationToken __) => Task.CompletedTask;

async Task TextAndFilesRoundTrip()
{
    List<ClipboardContent> received = [];
    List<string> status = [];
    int messages = 0;
    using ClipboardTransfer receiver = new(Root("round-trip-receiver"), NoSend, received.Add, status.Add);
    using ClipboardTransfer sender = new(Root("round-trip-sender"), async (message, ct) =>
    {
        string json = JsonSerializer.Serialize(message);
        Assert(Encoding.UTF8.GetByteCount(json) < 16 * 1024, "Clipboard message exceeded its transport allocation.");
        ClipboardMessage decoded = JsonSerializer.Deserialize<ClipboardMessage>(json)!;
        messages++;
        await receiver.ReceiveAsync(decoded, ct);
    }, _ => throw new Exception("Sender received its own clipboard."));

    string text = string.Concat(Enumerable.Repeat("Clipboard: á字🙂\r\n", 15000));
    await sender.SendAsync(new(Text: text));
    Assert(received.Count == 1 && received[0].Text == text && messages > 10, "Unicode text did not stream intact.");
    await sender.SendAsync(new(Text: ""));
    Assert(received.Count == 2 && received[^1].Text == "", "Empty clipboard text was lost.");
    string exactLimit = new('x', ClipboardLimits.MaxTextBytes);
    await sender.SendAsync(new(Text: exactLimit));
    Assert(received[^1].Text == exactLimit, "Exact text byte limit was rejected.");

    string source = Root("source-files");
    Directory.CreateDirectory(source);
    string large = Path.Combine(source, "résumé.bin"), empty = Path.Combine(source, "empty.txt");
    byte[] bytes = RandomNumberGenerator.GetBytes(ClipboardLimits.ChunkBytes * 3 + 149);
    await File.WriteAllBytesAsync(large, bytes);
    await File.WriteAllBytesAsync(empty, []);
    await sender.SendAsync(new(Files: [empty, large]));
    Assert(received[^1].Files is not null, "File clipboard was rejected: " + string.Join("; ", status));
    string[] result = received[^1].Files!;
    Assert(result.Length == 2 && Path.GetFileName(result[1]) == "résumé.bin" &&
        !result[1].Equals(large, StringComparison.OrdinalIgnoreCase), "File paths were not safely remapped into the cache.");
    Assert(File.ReadAllBytes(result[1]).SequenceEqual(bytes) && new FileInfo(result[0]).Length == 0, "Files or empty file changed in transit.");
    await receiver.ResetAsync();
    Assert(File.Exists(result[0]) && File.Exists(result[1]), "Disconnect deleted the published file clipboard.");
    receiver.Dispose();
    Assert(File.Exists(result[0]) && File.Exists(result[1]), "App exit deleted the published file clipboard.");
    Assert(status.Count == 0, "Valid clipboard content reported an error.");
}

async Task RejectUntrustedMetadataAndChunks()
{
    List<ClipboardContent> received = [];
    List<string> errors = [];
    string cache = Root("malformed");
    using ClipboardTransfer receiver = new(cache, NoSend, received.Add, errors.Add);
    byte[] data = Encoding.UTF8.GetBytes("1234567890");
    ClipboardFileDescriptor valid = new("safe.txt", data.Length, Hash(data));
    string[] unsafeNames = ["../escape.txt", "..\\escape.txt", "C:\\escape.txt", "x:y", "\\server\\share", "CON.txt", "nul", "COM1.bin", "LPT²", "trailing.", "trailing ", ".", "..", "bad\n.txt", "a/b.txt", "a?b", new('x', 129)];
    foreach (string name in unsafeNames)
        await receiver.ReceiveAsync(new("begin", Guid.NewGuid(), "files", [valid with { Name = name }], data.Length));
    await receiver.ReceiveAsync(new("begin", Guid.NewGuid(), "files", [valid, valid with { Name = "SAFE.TXT" }], data.Length * 2));
    await receiver.ReceiveAsync(new("begin", Guid.NewGuid(), "files", [valid with { Length = -1 }], 0));
    await receiver.ReceiveAsync(new("begin", Guid.NewGuid(), "files", [valid], data.Length + 1));
    await receiver.ReceiveAsync(new("begin", Guid.NewGuid(), "files", [valid with { Length = ClipboardLimits.MaxFileBytes + 1 }], ClipboardLimits.MaxFileBytes + 1));
    await receiver.ReceiveAsync(new("begin", Guid.NewGuid(), "files", Enumerable.Range(0, 21).Select(i => valid with { Name = i + ".txt" }).ToArray(), data.Length * 21));
    await receiver.ReceiveAsync(new("begin", Guid.NewGuid(), "files", [null!], data.Length));
    await receiver.ReceiveAsync(new("begin", Guid.NewGuid(), "text", TotalBytes: ClipboardLimits.MaxTextBytes + 1, Sha256: Hash(data)));
    await receiver.ReceiveAsync(new("begin", Guid.NewGuid(), "text", TotalBytes: data.Length, Sha256: "bad"));
    Assert(!Directory.EnumerateFiles(cache, "*", SearchOption.AllDirectories).Any() &&
        Directory.EnumerateDirectories(cache, "partial-*", SearchOption.AllDirectories).Count() == 0, "Invalid metadata allocated disk files.");

    async Task RejectedTransfer(ClipboardMessage[] messages)
    {
        int before = errors.Count;
        bool rejected = false;
        foreach (ClipboardMessage message in messages) rejected |= !await receiver.ReceiveAsync(message);
        Assert(received.Count == 0 && errors.Count > before, "Malformed transfer published a clipboard or was not rejected.");
        Assert(rejected, "Rejected clipboard transfer reported a successful acknowledgement.");
        Assert(!Directory.EnumerateFileSystemEntries(cache, "partial-*", SearchOption.AllDirectories).Any(), "Rejected transfer left partial files.");
    }
    Guid id = Guid.NewGuid();
    await RejectedTransfer([
        new("begin", id, "files", [valid], data.Length),
        new("chunk", id, ChunkIndex: 1, Data: data), new("end", id)]);
    id = Guid.NewGuid();
    await RejectedTransfer([
        new("begin", id, "files", [valid], data.Length),
        new("chunk", id, FileIndex: 1, Data: data), new("end", id)]);
    id = Guid.NewGuid();
    await RejectedTransfer([
        new("begin", id, "files", [valid], data.Length),
        new("chunk", id, Data: new byte[data.Length + 1]), new("end", id)]);
    id = Guid.NewGuid();
    await RejectedTransfer([
        new("begin", id, "files", [valid], data.Length),
        new("chunk", id, Data: new byte[data.Length]), new("end", id)]);
    id = Guid.NewGuid();
    await RejectedTransfer([
        new("begin", id, "files", [valid], data.Length), new("end", id)]);
    id = Guid.NewGuid();
    await RejectedTransfer([
        new("begin", id, "text", TotalBytes: data.Length, Sha256: Hash(data)),
        new("chunk", id, Data: new byte[ClipboardLimits.ChunkBytes + 1]), new("end", id)]);
    id = Guid.NewGuid();
    byte[] invalidUtf8 = [0xff];
    await RejectedTransfer([
        new("begin", id, "text", TotalBytes: 1, Sha256: Hash(invalidUtf8)),
        new("chunk", id, Data: invalidUtf8), new("end", id)]);
    id = Guid.NewGuid();
    byte[] embeddedNull = [65, 0, 66];
    await RejectedTransfer([
        new("begin", id, "text", TotalBytes: embeddedNull.Length, Sha256: Hash(embeddedNull)),
        new("chunk", id, Data: embeddedNull), new("end", id)]);
    id = Guid.NewGuid();
    await RejectedTransfer([
        new("begin", id, "files", [valid], data.Length),
        new("chunk", id, Data: data), new("chunk", id, Data: data), new("end", id)]);

    id = Guid.NewGuid();
    await receiver.ReceiveAsync(new("begin", id, "files", [valid], data.Length));
    await receiver.ReceiveAsync(new("chunk", id, Data: data[..3]));
    await receiver.ReceiveAsync(new("cancel", id));
    await receiver.ReceiveAsync(new("end", id));
    Assert(received.Count == 0 && !Directory.EnumerateFiles(cache, "*", SearchOption.AllDirectories).Any(), "Cancel published or kept partial files.");
    id = Guid.NewGuid();
    await receiver.ReceiveAsync(new("begin", id, "files", [valid], data.Length));
    await receiver.ReceiveAsync(new("chunk", id, Data: data[..3]));
    await receiver.ResetAsync();
    Assert(!Directory.EnumerateFiles(cache, "*", SearchOption.AllDirectories).Any(), "Disconnect left partial files.");

    // Successful IDs are remembered: retries and echo do not republish old clipboard data.
    id = Guid.NewGuid();
    ClipboardMessage[] successful = [new("begin", id, "text", TotalBytes: data.Length, Sha256: Hash(data)), new("chunk", id, Data: data), new("end", id)];
    foreach (ClipboardMessage message in successful) Assert(await receiver.ReceiveAsync(message), "Valid clipboard message was not acknowledged.");
    foreach (ClipboardMessage message in successful) Assert(!await receiver.ReceiveAsync(message), "Replayed clipboard message was acknowledged.");
    Assert(received.Count == 1 && received[0].Text == "1234567890", "Replay or echo updated the clipboard twice.");
}

async Task SourceValidationAndCancellation()
{
    List<ClipboardMessage> sent = [];
    using ClipboardTransfer sender = new(Root("source-validation"), (message, _) => { sent.Add(message); return Task.CompletedTask; }, _ => { });
    async Task Reject(ClipboardContent content)
    {
        int before = sent.Count;
        try { await sender.SendAsync(content); throw new Exception("Invalid local clipboard was accepted."); }
        catch (ArgumentException) { }
        Assert(sent.Count == before, "Invalid clipboard source sent metadata before validation.");
    }
    await Reject(new());
    await Reject(new(Text: "text", Files: []));
    await Reject(new(Files: []));
    await Reject(new(Text: new string('é', ClipboardLimits.MaxTextBytes / 2 + 1)));
    await Reject(new(Text: "\ud800"));
    await Reject(new(Text: "embedded\0null"));
    await Reject(new(Files: Enumerable.Repeat("unused", 21).ToArray()));
    string dir = Root("local-input");
    Directory.CreateDirectory(dir);
    await Reject(new(Files: [dir]));
    string tooBig = Path.Combine(dir, "too-big.bin");
    using (FileStream stream = File.Create(tooBig)) stream.SetLength(ClipboardLimits.MaxFileBytes + 1);
    await Reject(new(Files: [tooBig]));
    string normal = Path.Combine(dir, "normal.txt");
    File.WriteAllText(normal, "test");
    await Reject(new(Files: [normal, normal]));
    string link = Path.Combine(dir, "link.txt");
    bool linked = false;
    try
    {
        File.CreateSymbolicLink(link, normal);
        linked = true;
    }
    catch (Exception ex) when (ex is UnauthorizedAccessException || (ex is IOException && (ex.HResult & 0xffff) == 1314))
    { Console.WriteLine("SKIP: symlink creation requires Windows developer mode or elevation."); }
    if (linked) await Reject(new(Files: [link]));

    List<ClipboardContent> completed = [];
    using ClipboardTransfer receiver = new(Root("cancel-receiver"), NoSend, completed.Add);
    using CancellationTokenSource cancellation = new();
    int chunks = 0;
    bool sentCancel = false;
    using ClipboardTransfer cancelledSender = new(Root("cancel-sender"), async (message, ct) =>
    {
        if (message.Kind == "chunk" && ++chunks == 2) cancellation.Cancel();
        if (message.Kind == "cancel") sentCancel = true;
        await receiver.ReceiveAsync(message, ct);
    }, _ => { });
    try { await cancelledSender.SendAsync(new(Text: new string('x', 50000)), cancellation.Token); throw new Exception("Cancelled send completed."); }
    catch (OperationCanceledException) { }
    Assert(sentCancel && completed.Count == 0, "Cancelled send did not cancel the partial remote transfer.");
    await cancelledSender.SendAsync(new(Text: "recovered"));
    Assert(completed.Count == 1 && completed[0].Text == "recovered", "Cancellation prevented the next clipboard transfer.");
}

async Task CacheBudgetAndPersistence()
{
    string source = Root("budget-source.bin");
    using (FileStream stream = File.Create(source)) stream.SetLength(ClipboardLimits.MaxFileBytes);
    string cache = Root("budget-cache");
    List<string> results = [];
    using ClipboardTransfer receiver = new(cache, NoSend, content => results.Add(content.Files![0]));
    using ClipboardTransfer sender = new(Root("budget-sender"), receiver.ReceiveAsync, _ => { });
    await sender.SendAsync(new(Files: [source]));
    await sender.SendAsync(new(Files: [source]));
    Assert(File.Exists(results[0]) && File.Exists(results[1]), "Cache discarded files too early.");
    await sender.SendAsync(new(Files: [source]));
    Assert(!File.Exists(results[0]) && File.Exists(results[1]) && File.Exists(results[2]), "Cache did not prune oldest files while preserving the latest clipboard.");
    long bytes = Directory.EnumerateFiles(cache, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length);
    Assert(bytes <= ClipboardLimits.CacheBytes, "Clipboard disk cache exceeded its budget.");
    receiver.Dispose();
    string abandoned = Path.Combine(cache, "session-" + Guid.NewGuid().ToString("N"), "partial-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(abandoned);
    File.WriteAllText(Path.Combine(abandoned, "interrupted.txt"), "incomplete");
    using ClipboardTransfer restarted = new(cache, NoSend, _ => { });
    Assert(File.Exists(results[^1]), "Startup cleanup deleted the latest published file clipboard.");
    Assert(!Directory.Exists(abandoned), "Quick restart retained an abandoned partial transfer.");
}

async Task JunctionAndLifecycleBoundaries()
{
    string target = Root("junction-target");
    string junction = Root("junction");
    Directory.CreateDirectory(target);
    string sentinel = Path.Combine(target, "sentinel.txt");
    File.WriteAllText(sentinel, "must remain intact");
    if (OperatingSystem.IsWindows())
    {
        using Process process = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{junction}\" \"{target}\"")
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        })!;
        await process.WaitForExitAsync();
        if (process.ExitCode == 0)
        {
            try
            {
                foreach (string unsafeCache in new[] { junction, Path.Combine(junction, "nested-cache") })
                {
                    try
                    {
                        using ClipboardTransfer invalid = new(unsafeCache, NoSend, _ => { });
                        throw new Exception("Cache accepted a junction root or ancestor.");
                    }
                    catch (IOException) { }
                }
                using ClipboardTransfer sender = new(Root("junction-source-cache"), NoSend, _ => { });
                try
                {
                    await sender.SendAsync(new(Files: [Path.Combine(junction, "sentinel.txt")]));
                    throw new Exception("Clipboard accepted a file inside a junction.");
                }
                catch (ArgumentException) { }
                Assert(File.ReadAllText(sentinel) == "must remain intact" && Directory.EnumerateFileSystemEntries(target).Count() == 1,
                    "Junction validation wrote or deleted content outside the cache.");
            }
            finally { Directory.Delete(junction); }
        }
        else Console.WriteLine("SKIP: Windows did not allow creation of a test junction.");
    }

    TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int begins = 0;
    using ClipboardTransfer resetting = new(Root("reset-lifecycle"), async (message, ct) =>
    {
        if (message.Kind == "begin" && Interlocked.Increment(ref begins) == 1)
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        }
    }, _ => { });
    Task first = resetting.SendAsync(new(Text: "first"));
    await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Task waiting = resetting.SendAsync(new(Text: "queued before disconnect"));
    await resetting.ResetAsync();
    foreach (Task task in new[] { first, waiting })
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(5)); throw new Exception("Disconnect left an active or queued send alive."); }
        catch (OperationCanceledException) { }
    }
    await resetting.SendAsync(new(Text: "fresh connection"));
    Assert(begins == 2, "A stale queued send escaped disconnect cancellation.");

    ClipboardTransfer? reentrant = null;
    int callbacks = 0;
    reentrant = new(Root("reentrant-completion"), NoSend, _ =>
    {
        callbacks++;
        reentrant!.ResetAsync().GetAwaiter().GetResult();
        reentrant.Dispose();
    });
    using (reentrant)
    {
        Guid id = Guid.NewGuid();
        await reentrant.ReceiveAsync(new("begin", id, "text", Sha256: Hash([])));
        await reentrant.ReceiveAsync(new("end", id)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert(callbacks == 1, "Reset/dispose from the completed callback deadlocked or lost completion.");
    }

    ClipboardTransfer? rejected = null;
    rejected = new(Root("reentrant-rejection"), NoSend, _ => { }, _ => rejected!.ResetAsync().GetAwaiter().GetResult());
    using (rejected)
        await rejected.ReceiveAsync(new("begin", Guid.NewGuid(), "text", TotalBytes: -1)).WaitAsync(TimeSpan.FromSeconds(5));
}

async Task FailedPartialCleanupKeepsCacheReservation()
{
    if (!OperatingSystem.IsWindows()) return;
    string cache = Root("partial-reservation-cache");
    using ClipboardTransfer receiver = new(cache, NoSend, _ => { });
    byte[] first = [65];
    Guid id = Guid.NewGuid();
    ClipboardFileDescriptor[] files = [new("held.txt", 1, Hash(first)),
        new("incomplete.bin", ClipboardLimits.MaxFileBytes - 1, new string('0', 64))];
    Assert(await receiver.ReceiveAsync(new("begin", id, "files", files, ClipboardLimits.MaxFileBytes)), "Valid partial metadata was rejected.");
    await receiver.ReceiveAsync(new("chunk", id, Data: first));
    await receiver.ReceiveAsync(new("chunk", id, FileIndex: 1, Data: first));
    string held = Directory.EnumerateFiles(cache, "held.txt", SearchOption.AllDirectories).Single();
    File.SetAttributes(held, FileAttributes.ReadOnly);
    try
    {
        Assert(await receiver.ReceiveAsync(new("cancel", id)), "Valid cancellation was rejected.");
        Assert(File.Exists(held), "Test did not retain the readonly partial file.");
        string large = Root("partial-reservation-source.bin");
        using (FileStream file = File.Create(large)) file.SetLength(ClipboardLimits.MaxFileBytes);
        using ClipboardTransfer sender = new(Root("partial-reservation-sender"), receiver.ReceiveAsync, _ => { });
        await sender.SendAsync(new(Files: [large]));
        ClipboardFileDescriptor next = new("next.bin", ClipboardLimits.MaxFileBytes, new string('0', 64));
        Assert(!await receiver.ReceiveAsync(new("begin", Guid.NewGuid(), "files", [next], ClipboardLimits.MaxFileBytes)),
            "Undeleted partial cache was omitted from the next transfer's disk reservation.");
        File.SetAttributes(held, FileAttributes.Normal);
        Guid recovered = Guid.NewGuid();
        Assert(await receiver.ReceiveAsync(new("begin", recovered, "files", [next], ClipboardLimits.MaxFileBytes)),
            "Removing the partial file hold did not recover clipboard transfers.");
        await receiver.ReceiveAsync(new("cancel", recovered));
        Assert(!File.Exists(held), "Recovered pruning kept the abandoned partial.");
    }
    finally { if (File.Exists(held)) File.SetAttributes(held, FileAttributes.Normal); }
}
