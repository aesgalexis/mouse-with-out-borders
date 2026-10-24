using System.Security.Cryptography;
using System.Text;

namespace MouseWithOutBorders;

internal static class ClipboardLimits
{
    internal const int MaxTextBytes = 1024 * 1024;
    internal const long MaxFileBytes = 25L * 1024 * 1024;
    internal const int MaxFiles = 20;
    internal const int ChunkBytes = 8 * 1024;
    internal const long CacheBytes = 64L * 1024 * 1024;
    internal static readonly TimeSpan TransferTimeout = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);
    internal static readonly UTF8Encoding Utf8 = new(false, true);

    internal static bool IsSafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name is "." or ".." ||
            name.EndsWith('.') || name.EndsWith(' ') ||
            name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c))) return false;
        string stem = name.Split('.')[0].ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$") &&
            !(stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) &&
              "123456789¹²³".Contains(stem[3]));
    }
}

internal sealed record ClipboardContent(string? Text = null, string[]? Files = null);
internal sealed record ClipboardFileDescriptor(string Name, long Length, string Sha256);
internal sealed record ClipboardMessage(
    string Kind,
    Guid TransferId,
    string? ContentType = null,
    ClipboardFileDescriptor[]? Files = null,
    long TotalBytes = 0,
    int FileIndex = 0,
    int ChunkIndex = 0,
    byte[]? Data = null,
    string? Sha256 = null);

/// <summary>
/// A bounded, transport-independent clipboard protocol. Only complete, verified
/// content reaches the clipboard adapter; remote paths never become local paths.
/// </summary>
internal sealed class ClipboardTransfer : IDisposable
{
    private readonly string _cache;
    private readonly string _cacheRoot;
    private readonly Func<ClipboardMessage, CancellationToken, Task> _send;
    private readonly Action<ClipboardContent> _completed;
    private readonly Action<string>? _status;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _receiveLock = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Queue<Guid> _recent = new();
    private readonly HashSet<Guid> _known = [];
    private readonly LinkedList<(string Path, long Bytes)> _completeFiles = new();
    private readonly System.Threading.Timer _timer;
    private readonly object _generationLock = new();
    private CancellationTokenSource _generation = new();
    private long _generationVersion;
    private Incoming? _incoming;
    private long _cacheBytes;
    private int _disposed;

    internal ClipboardTransfer(string cacheRoot,
        Func<ClipboardMessage, CancellationToken, Task> send,
        Action<ClipboardContent> completed, Action<string>? status = null)
    {
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(completed);
        _cacheRoot = Path.GetFullPath(cacheRoot);
        RejectDirectoryLinks(_cacheRoot, allowMissing: true);
        _cache = Path.Combine(_cacheRoot, "session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cache);
        RejectDirectoryLinks(_cache);
        _send = send;
        _completed = completed;
        _status = status;
        LoadCache(_cacheRoot);
        _timer = new System.Threading.Timer(_ => _ = ExpireAsync(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    internal async Task SendAsync(ClipboardContent content, CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource deadline = LinkOperation(cancellationToken, out _) ??
            throw new ObjectDisposedException(nameof(ClipboardTransfer));
        deadline.CancelAfter(ClipboardLimits.TransferTimeout);
        CancellationToken ct = deadline.Token;
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        Guid id = Guid.NewGuid();
        bool began = false;
        List<FileStream> streams = [];
        try
        {
            if ((content.Text is null) == (content.Files is null))
                throw new ArgumentException("Choose text or files to share.");
            await RememberAsync(id, ct).ConfigureAwait(false);
            if (content.Text is string text)
            {
                if (text.Contains('\0')) throw new ArgumentException("Clipboard text contains an unsupported null character.");
                // Count before encoding, so an oversized clipboard cannot allocate a second large copy.
                int length = ClipboardLimits.Utf8.GetByteCount(text);
                if (length > ClipboardLimits.MaxTextBytes)
                    throw new ArgumentException("Clipboard text exceeds the 1 MB limit.");
                byte[] data = ClipboardLimits.Utf8.GetBytes(text);
                await _send(new("begin", id, "text", TotalBytes: data.Length,
                    Sha256: Convert.ToHexString(SHA256.HashData(data))), ct).ConfigureAwait(false);
                began = true;
                using MemoryStream textStream = new(data, false);
                await SendBytesAsync(id, 0, textStream, ct).ConfigureAwait(false);
            }
            else
            {
                string[] paths = content.Files!;
                if (paths.Length is < 1 or > ClipboardLimits.MaxFiles)
                    throw new ArgumentException("Share between 1 and 20 files at a time.");
                List<ClipboardFileDescriptor> files = [];
                HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
                long total = 0;
                foreach (string path in paths)
                {
                    ct.ThrowIfCancellationRequested();
                    string fullPath = Path.GetFullPath(path);
                    string name = Path.GetFileName(fullPath);
                    if (!ClipboardLimits.IsSafeFileName(name) || !names.Add(name))
                        throw new ArgumentException("Clipboard files must have distinct, valid file names.");
                    RejectLinksAndDirectories(fullPath);
                    FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                        ClipboardLimits.ChunkBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    streams.Add(stream);
                    if (stream.Length > ClipboardLimits.MaxFileBytes - total)
                        throw new ArgumentException("Clipboard files exceed the 25 MB total limit.");
                    total += stream.Length;
                    string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
                    stream.Position = 0;
                    files.Add(new(name, stream.Length, hash));
                }
                await _send(new("begin", id, "files", files.ToArray(), total), ct).ConfigureAwait(false);
                began = true;
                for (int index = 0; index < streams.Count; index++)
                    await SendBytesAsync(id, index, streams[index], ct).ConfigureAwait(false);
            }
            await _send(new("end", id), ct).ConfigureAwait(false);
        }
        catch
        {
            if (began)
            {
                try
                {
                    using CancellationTokenSource cancelDeadline = new(TimeSpan.FromSeconds(1));
                    await _send(new("cancel", id), cancelDeadline.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException) { }
            }
            throw;
        }
        finally
        {
            try
            {
                foreach (FileStream stream in streams)
                {
                    try { await stream.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
            finally { _sendLock.Release(); }
        }
    }

    private async Task SendBytesAsync(Guid id, int fileIndex, Stream stream, CancellationToken ct)
    {
        byte[] buffer = new byte[ClipboardLimits.ChunkBytes];
        int index = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            await _send(new("chunk", id, FileIndex: fileIndex, ChunkIndex: index++, Data: buffer[..read]), ct).ConfigureAwait(false);
    }

    internal async Task<bool> ReceiveAsync(ClipboardMessage message, CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource? linked = LinkOperation(cancellationToken, out long generation);
        if (linked is null) return false;
        await _receiveLock.WaitAsync(linked.Token).ConfigureAwait(false);
        ClipboardContent? completed = null;
        string? rejected = null;
        bool accepted = false;
        try
        {
            if (message.TransferId == Guid.Empty) throw new InvalidDataException("Invalid clipboard transfer identifier.");
            if (message.Kind == "begin")
            {
                if (_known.Contains(message.TransferId)) return false;
                AbortIncoming();
                Remember(message.TransferId);
                _incoming = Begin(message);
                accepted = true;
            }
            else if (_incoming?.Id == message.TransferId)
            {
                if (DateTime.UtcNow - _incoming.LastActivity > ClipboardLimits.IdleTimeout ||
                    DateTime.UtcNow - _incoming.Started > ClipboardLimits.TransferTimeout)
                    throw new InvalidDataException("Clipboard transfer timed out.");
                _incoming.LastActivity = DateTime.UtcNow;
                if (message.Kind == "chunk") await ReceiveChunkAsync(_incoming, message, linked.Token).ConfigureAwait(false);
                else if (message.Kind is "end" or "cancel")
                {
                    if (message.Data is not null || message.ContentType is not null || message.Files is not null ||
                        message.TotalBytes != 0 || message.FileIndex != 0 || message.ChunkIndex != 0 || message.Sha256 is not null)
                        throw new InvalidDataException("Invalid clipboard completion metadata.");
                    if (message.Kind == "end") completed = CompleteIncoming(_incoming);
                    else AbortIncoming();
                }
                else throw new InvalidDataException("Invalid clipboard message.");
                accepted = true;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or UnauthorizedAccessException or CryptographicException)
        {
            AbortIncoming();
            rejected = "Clipboard transfer was rejected: " + ex.Message;
        }
        catch (OperationCanceledException)
        {
            AbortIncoming();
            throw;
        }
        finally { _receiveLock.Release(); }
        if (rejected is not null) Report(rejected);
        if (completed is not null)
        {
            string? failure = null;
            lock (_generationLock)
            {
                // Reset/disconnect and completion have one publication boundary.
                // User callbacks run after releasing the stream/state lock.
                if (generation != _generationVersion || Volatile.Read(ref _disposed) != 0) return false;
                try { _completed(completed); }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    accepted = false;
                    failure = "Could not update the clipboard: " + ex.Message;
                }
            }
            if (failure is not null) Report(failure);
        }
        return accepted;
    }

    private Incoming Begin(ClipboardMessage message)
    {
        if (message.Data is not null || message.FileIndex != 0 || message.ChunkIndex != 0)
            throw new InvalidDataException("Invalid clipboard metadata.");
        if (message.ContentType == "text")
        {
            if (message.Files is not null || message.TotalBytes is < 0 or > ClipboardLimits.MaxTextBytes || !IsHash(message.Sha256))
                throw new InvalidDataException("Invalid clipboard text size or hash.");
            return new(message.TransferId, message.TotalBytes, message.Sha256!);
        }
        if (message.ContentType != "files" || message.Files is not { Length: >= 1 and <= ClipboardLimits.MaxFiles } files ||
            message.TotalBytes is < 0 or > ClipboardLimits.MaxFileBytes || message.Sha256 is not null)
            throw new InvalidDataException("Invalid clipboard file batch.");
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (ClipboardFileDescriptor? file in files)
        {
            if (file is null || !ClipboardLimits.IsSafeFileName(file.Name) || !names.Add(file.Name) ||
                file.Length < 0 || file.Length > ClipboardLimits.MaxFileBytes - total || !IsHash(file.Sha256))
                throw new InvalidDataException("Invalid clipboard file name, size or hash.");
            total += file.Length;
        }
        if (total != message.TotalBytes) throw new InvalidDataException("Clipboard batch size does not match its files.");
        TrimCache(total);
        // No disk space or file handles are allocated until every remote descriptor is valid.
        string partial = Path.Combine(_cache, "partial-" + Guid.NewGuid().ToString("N"));
        RejectDirectoryLinks(_cache);
        Directory.CreateDirectory(partial);
        RejectDirectoryLinks(partial);
        try { return new(message.TransferId, total, files.Select(f => f with { }).ToArray(), partial); }
        catch { DeleteDirectory(partial); throw; }
    }

    private static async Task ReceiveChunkAsync(Incoming incoming, ClipboardMessage message, CancellationToken ct)
    {
        if (message.ContentType is not null || message.Files is not null || message.TotalBytes != 0 || message.Sha256 is not null ||
            message.Data is not { Length: > 0 and <= ClipboardLimits.ChunkBytes } data)
            throw new InvalidDataException("Invalid clipboard data chunk.");
        if (incoming.IsText)
        {
            if (message.FileIndex != 0 || message.ChunkIndex != incoming.ChunkIndex || data.Length > incoming.TotalBytes - incoming.Bytes)
                throw new InvalidDataException("Clipboard text chunks are out of order or too large.");
            await incoming.Text!.WriteAsync(data, ct).ConfigureAwait(false);
        }
        else
        {
            incoming.AdvanceEmptyOrCompleteFiles();
            if (message.FileIndex != incoming.FileIndex || message.FileIndex >= incoming.Files!.Length ||
                message.ChunkIndex != incoming.ChunkIndex || data.Length > incoming.Files[message.FileIndex].Length - incoming.FileBytes)
                throw new InvalidDataException("Clipboard file chunks are out of order or too large.");
            incoming.OpenFile();
            await incoming.Stream!.WriteAsync(data, ct).ConfigureAwait(false);
            incoming.Hash!.AppendData(data);
            incoming.FileBytes += data.Length;
        }
        incoming.Bytes += data.Length;
        incoming.ChunkIndex++;
    }

    private ClipboardContent CompleteIncoming(Incoming incoming)
    {
        if (incoming.Bytes != incoming.TotalBytes) throw new InvalidDataException("Clipboard transfer is incomplete.");
        ClipboardContent content;
        if (incoming.IsText)
        {
            byte[] bytes = incoming.Text!.ToArray();
            if (!SameHash(bytes, incoming.TextHash!)) throw new InvalidDataException("Clipboard text checksum failed.");
            string text = ClipboardLimits.Utf8.GetString(bytes);
            if (text.Contains('\0')) throw new InvalidDataException("Clipboard text contains an unsupported null character.");
            content = new(Text: text);
        }
        else
        {
            incoming.AdvanceEmptyOrCompleteFiles();
            if (incoming.FileIndex != incoming.Files!.Length) throw new InvalidDataException("Clipboard files are incomplete.");
            string complete = Path.Combine(_cache, "files-" + Guid.NewGuid().ToString("N"));
            RejectDirectoryLinks(incoming.Directory!);
            Directory.Move(incoming.Directory!, complete);
            incoming.Directory = null;
            _completeFiles.AddLast((complete, incoming.TotalBytes));
            _cacheBytes += incoming.TotalBytes;
            content = new(Files: incoming.Files.Select(file => Path.Combine(complete, file.Name)).ToArray());
            TrimCache(0);
        }
        incoming.Dispose();
        _incoming = null;
        return content;
    }

    internal async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource previous;
        lock (_generationLock)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            previous = _generation;
            _generation = new();
            _generationVersion++;
        }
        previous.Cancel();
        previous.Dispose();
        await _receiveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { AbortIncoming(); }
        finally { _receiveLock.Release(); }
    }

    private CancellationTokenSource? LinkOperation(CancellationToken ct, out long generation)
    {
        lock (_generationLock)
        {
            generation = _generationVersion;
            return Volatile.Read(ref _disposed) == 0 ?
                CancellationTokenSource.CreateLinkedTokenSource(ct, _stopping.Token, _generation.Token) : null;
        }
    }

    private void LoadCache(string root)
    {
        RejectDirectoryLinks(root);
        List<(string Path, long Bytes, DateTime Created)> batches = [];
        foreach (string session in System.IO.Directory.EnumerateDirectories(root, "session-*"))
        {
            if (!IsCacheDirectory(session, "session-")) continue;
            foreach (string path in System.IO.Directory.EnumerateDirectories(session))
            {
                // The app holds a per-user single-instance mutex. Partials from
                // an earlier engine are abandoned, even after a quick crash/restart.
                bool partial = IsCacheDirectory(path, "partial-");
                if (partial && DeleteDirectory(path)) continue;
                if (partial || IsCacheDirectory(path, "files-"))
                {
                    long bytes = 0;
                    bool valid = true;
                    foreach (string file in System.IO.Directory.EnumerateFileSystemEntries(path))
                    {
                        FileInfo info = new(file);
                        if ((info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.Device)) != 0 ||
                            !ClipboardLimits.IsSafeFileName(info.Name)) { valid = false; break; }
                        bytes += info.Length;
                    }
                    if (valid) batches.Add((path, bytes, partial ? DateTime.MinValue : System.IO.Directory.GetCreationTimeUtc(path)));
                }
            }
            if (session != _cache && !System.IO.Directory.EnumerateFileSystemEntries(session).Any()) DeleteDirectory(session);
        }
        foreach (var batch in batches.OrderBy(batch => batch.Created))
        {
            _completeFiles.AddLast((batch.Path, batch.Bytes));
            _cacheBytes += batch.Bytes;
        }
        TrimCache(0);
    }

    private static bool IsCacheDirectory(string path, string prefix)
    {
        string name = Path.GetFileName(path);
        return name.Length == prefix.Length + 32 && name.StartsWith(prefix, StringComparison.Ordinal) &&
            Guid.TryParseExact(name[prefix.Length..], "N", out _) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
    }

    private void TrimCache(long reserved)
    {
        var node = _completeFiles.First;
        while (node?.Next is not null)
        {
            var next = node.Next;
            var old = node.Value;
            if ((_cacheBytes + reserved > ClipboardLimits.CacheBytes || _completeFiles.Count > 32 ||
                DateTime.UtcNow - System.IO.Directory.GetCreationTimeUtc(old.Path) > TimeSpan.FromHours(24)) && DeleteDirectory(old.Path))
            {
                _cacheBytes -= old.Bytes;
                _completeFiles.Remove(node);
            }
            node = next;
        }
        if (_cacheBytes + reserved > ClipboardLimits.CacheBytes)
            throw new IOException("The clipboard cache is full. Try again after the previous files are no longer in use.");
    }

    private async Task RememberAsync(Guid id, CancellationToken ct)
    {
        await _receiveLock.WaitAsync(ct).ConfigureAwait(false);
        try { Remember(id); }
        finally { _receiveLock.Release(); }
    }

    private void Remember(Guid id)
    {
        if (!_known.Add(id)) return;
        _recent.Enqueue(id);
        while (_recent.Count > 128) _known.Remove(_recent.Dequeue());
    }

    private async Task ExpireAsync()
    {
        try
        {
            if (Volatile.Read(ref _disposed) != 0 || !await _receiveLock.WaitAsync(0).ConfigureAwait(false)) return;
            bool expired = false;
            try
            {
                if (_incoming is not null && (DateTime.UtcNow - _incoming.LastActivity > ClipboardLimits.IdleTimeout ||
                    DateTime.UtcNow - _incoming.Started > ClipboardLimits.TransferTimeout))
                {
                    AbortIncoming();
                    expired = true;
                }
            }
            finally { _receiveLock.Release(); }
            if (expired) Report("Clipboard transfer timed out.");
        }
        catch (Exception ex) when (ex is ObjectDisposedException or IOException or UnauthorizedAccessException) { }
    }

    private void AbortIncoming()
    {
        Incoming? incoming = _incoming;
        _incoming = null;
        if (incoming is null) return;
        string? partial = incoming.Directory;
        incoming.Dispose();
        if (partial is not null && Directory.Exists(partial))
        {
            // Antivirus or a filesystem error may temporarily hold a closed
            // partial. Keep its reservation until a later prune can remove it.
            _completeFiles.AddFirst((partial, incoming.TotalBytes));
            _cacheBytes += incoming.TotalBytes;
        }
    }

    private void Report(string status)
    {
        try { _status?.Invoke(status); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
    }

    private static bool IsHash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
    private static bool SameHash(byte[] bytes, string hash) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(hash));

    private static void RejectLinksAndDirectories(string fullPath)
    {
        FileAttributes attributes = File.GetAttributes(fullPath);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
            throw new ArgumentException("Only regular files can be shared. Folders and links are excluded.");
        DirectoryInfo? parent = Directory.GetParent(fullPath);
        while (parent is not null)
        {
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Files inside linked folders cannot be shared.");
            parent = parent.Parent;
        }
    }

    private static void RejectDirectoryLinks(string fullPath, bool allowMissing = false)
    {
        for (DirectoryInfo? directory = new(Path.GetFullPath(fullPath)); directory is not null; directory = directory.Parent)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(directory.FullName); }
            catch (Exception ex) when (allowMissing && ex is (FileNotFoundException or DirectoryNotFoundException)) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
                throw new IOException("The clipboard cache must use ordinary folders without links.");
        }
    }

    private static bool DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                RejectDirectoryLinks(path);
                Directory.Delete(path, true);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _timer.Dispose();
        _stopping.Cancel();
        lock (_generationLock)
        {
            _generationVersion++;
            _generation.Cancel();
            _generation.Dispose();
        }
        // A pending writer may still own its stream. Receive never awaits while
        // holding this lock except for bounded local writes, so finish cleanup after it.
        _receiveLock.Wait();
        try
        {
            AbortIncoming();
            // File-drop clipboard data references these paths after disconnection
            // and app exit. Keep completed batches until a later bounded prune.
            if (!System.IO.Directory.EnumerateFileSystemEntries(_cache).Any()) DeleteDirectory(_cache);
        }
        finally { _receiveLock.Release(); }
        // Semaphores and the CTS remain available to in-flight continuations.
    }

    private sealed class Incoming : IDisposable
    {
        internal readonly Guid Id;
        internal readonly long TotalBytes;
        internal readonly DateTime Started = DateTime.UtcNow;
        internal DateTime LastActivity = DateTime.UtcNow;
        internal readonly MemoryStream? Text;
        internal readonly string? TextHash;
        internal readonly ClipboardFileDescriptor[]? Files;
        internal string? Directory;
        internal FileStream? Stream;
        internal IncrementalHash? Hash;
        internal long Bytes;
        internal long FileBytes;
        internal int FileIndex;
        internal int ChunkIndex;
        internal bool IsText => Text is not null;

        internal Incoming(Guid id, long total, string hash)
        {
            Id = id; TotalBytes = total; TextHash = hash;
            Text = new MemoryStream((int)total);
        }

        internal Incoming(Guid id, long total, ClipboardFileDescriptor[] files, string directory)
        {
            Id = id; TotalBytes = total; Files = files; Directory = directory;
        }

        internal void OpenFile()
        {
            if (Stream is not null) return;
            RejectDirectoryLinks(Directory!);
            Stream = new FileStream(Path.Combine(Directory!, Files![FileIndex].Name), FileMode.CreateNew,
                FileAccess.Write, FileShare.None, ClipboardLimits.ChunkBytes, FileOptions.Asynchronous);
            Hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        }

        internal void AdvanceEmptyOrCompleteFiles()
        {
            while (FileIndex < Files!.Length && FileBytes == Files[FileIndex].Length)
            {
                OpenFile();
                byte[] actual = Hash!.GetHashAndReset();
                if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(Files[FileIndex].Sha256)))
                    throw new InvalidDataException("Clipboard file checksum failed.");
                Stream!.Dispose(); Stream = null;
                Hash.Dispose(); Hash = null;
                FileIndex++; FileBytes = 0; ChunkIndex = 0;
            }
        }

        public void Dispose()
        {
            try { Stream?.Dispose(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            Hash?.Dispose(); Text?.Dispose();
            if (Directory is not null) DeleteDirectory(Directory);
        }
    }
}
