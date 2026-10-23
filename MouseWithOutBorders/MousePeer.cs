using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace MouseWithOutBorders;

internal sealed record MousePacket(
    string Kind,
    int Dx = 0,
    int Dy = 0,
    int Data = 0,
    double Ratio = 0,
    uint Flags = 0,
    long Transfer = 0,
    int Origin = 0);

/// <summary>
/// One authenticated, two-way TCP connection. Each message has a monotonically
/// increasing sequence number and a challenge-bound HMAC. No message is accepted
/// from a peer without the pairing key. Contents are not encrypted.
/// </summary>
internal sealed class MousePeer : IDisposable
{
    internal const int Port = 42187;
    internal const int PairingCodeLength = 8;
    private const string PairingAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly TcpClient _client;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly byte[] _key;
    private readonly string _nonce;
    private readonly Channel<MousePacket> _outgoing =
        Channel.CreateUnbounded<MousePacket>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _stopping = new();
    private long _sent;
    private long _received;
    private int _disposed;

    private MousePeer(TcpClient client, StreamReader reader, StreamWriter writer, byte[] key, string nonce)
    {
        _client = client;
        _reader = reader;
        _writer = writer;
        _key = key;
        _nonce = nonce;
        _ = WriteLoopAsync();
        _ = HeartbeatLoopAsync();
    }

    private static (StreamReader reader, StreamWriter writer) OpenStreams(TcpClient client)
    {
        client.NoDelay = true;
        NetworkStream stream = client.GetStream();
        return (
            new StreamReader(stream, new UTF8Encoding(false), false, 4096, true),
            new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true });
    }

    internal static string NormalizeKey(string code) =>
        new(code.Where(c => !char.IsWhiteSpace(c) && c != '-').Select(char.ToUpperInvariant).ToArray());

    internal static byte[] ParseKey(string code)
    {
        string normalized = NormalizeKey(code);
        if (normalized.Length == PairingCodeLength && normalized.All(PairingAlphabet.Contains))
            return Encoding.ASCII.GetBytes(normalized);

        // Keep connecting to receivers that still run the original app.
        if (normalized.Length == 32 && normalized.All(Uri.IsHexDigit))
            return Convert.FromHexString(normalized);

        throw new ArgumentException("Enter the receiving PC's 8-character pairing code. Older 32-character keys also work.");
    }

    internal static string NewKey() => string.Create(PairingCodeLength, 0, (characters, _) =>
    {
        for (int i = 0; i < characters.Length; i++)
            characters[i] = PairingAlphabet[RandomNumberGenerator.GetInt32(PairingAlphabet.Length)];
    });

    private static string Mac(byte[] key, string message) =>
        Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(message)));

    private static bool SameMac(byte[] key, string message, string actual)
    {
        try
        {
            byte[] received = Convert.FromHexString(actual);
            byte[] expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(message));
            return CryptographicOperations.FixedTimeEquals(received, expected);
        }
        catch (FormatException) { return false; }
    }

    internal static async Task<MousePeer> ConnectAsync(string host, byte[] key, CancellationToken ct)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        CancellationToken connecting = deadline.Token;
        TcpClient client = new();
        try
        {
            await client.ConnectAsync(host, Port, connecting);
            var (reader, writer) = OpenStreams(client);
            string hello = await reader.ReadLineAsync(connecting) ?? throw new IOException("Receiver disconnected.");
            if (!hello.StartsWith("MOUSE2|", StringComparison.Ordinal))
                throw new NotSupportedException("Update MWB on both PCs to use two-way mouse sharing.");
            string nonce = hello["MOUSE2|".Length..];
            if (nonce.Length != 48 || !nonce.All(Uri.IsHexDigit))
                throw new IOException("Invalid receiver challenge.");

            await writer.WriteLineAsync(("PROOF|" + Mac(key, "CLIENT|" + nonce)).AsMemory(), connecting);
            string response = await reader.ReadLineAsync(connecting) ?? throw new IOException("Receiver disconnected.");
            if (!response.StartsWith("OK|", StringComparison.Ordinal) ||
                !SameMac(key, "SERVER|" + nonce, response["OK|".Length..]))
                throw new UnauthorizedAccessException("Pairing key rejected by receiver.");

            return new MousePeer(client, reader, writer, key, nonce);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            client.Dispose();
            throw new TimeoutException("Receiver connection or pairing timed out after 5 seconds. Check the receiving PC and try again.", ex);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    internal static async Task<MousePeer> AcceptAsync(TcpClient client, byte[] key, CancellationToken ct)
    {
        try
        {
            var (reader, writer) = OpenStreams(client);
            string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            await writer.WriteLineAsync(("MOUSE2|" + nonce).AsMemory(), ct);
            string proof = await reader.ReadLineAsync(ct) ?? "";
            if (!proof.StartsWith("PROOF|", StringComparison.Ordinal) ||
                !SameMac(key, "CLIENT|" + nonce, proof["PROOF|".Length..]))
            {
                await writer.WriteLineAsync("DENIED".AsMemory(), ct);
                throw new UnauthorizedAccessException("Client pairing key rejected.");
            }
            await writer.WriteLineAsync(("OK|" + Mac(key, "SERVER|" + nonce)).AsMemory(), ct);
            return new MousePeer(client, reader, writer, key, nonce);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    internal bool Queue(MousePacket packet) => Volatile.Read(ref _disposed) == 0 && _outgoing.Writer.TryWrite(packet);

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (MousePacket packet in _outgoing.Reader.ReadAllAsync(_stopping.Token))
            {
                long seq = ++_sent;
                string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(packet)));
                string signed = $"{_nonce}|{seq}|{payload}";
                string line = $"{seq}|{payload}|{Mac(_key, signed)}";
                await _writer.WriteLineAsync(line.AsMemory(), _stopping.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { Dispose(); }
        catch (ObjectDisposedException) { }
        catch (SocketException) { Dispose(); }
    }

    private async Task HeartbeatLoopAsync()
    {
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), _stopping.Token);
                Queue(new MousePacket("ping"));
            }
        }
        catch (OperationCanceledException) { }
    }

    internal async Task ReadLoopAsync(Action<MousePacket> onPacket, CancellationToken ct)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stopping.Token);
        while (!linked.IsCancellationRequested)
        {
            // Every connected peer emits a heartbeat each second. A dead or
            // unplugged PC must not leave the origin's cursor locked forever.
            using CancellationTokenSource deadline =
                CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));

            string? line;
            try { line = await _reader.ReadLineAsync(deadline.Token); }
            catch (OperationCanceledException) when (!linked.IsCancellationRequested)
            {
                throw new IOException("Other PC stopped responding (heartbeat timeout).");
            }
            if (line is null) throw new IOException("Other PC disconnected.");
            if (line.Length > 4096) throw new IOException("Oversized mouse message.");

            string[] parts = line.Split('|', 3);
            if (parts.Length != 3 || !long.TryParse(parts[0], out long seq) ||
                seq != _received + 1 || !SameMac(_key, $"{_nonce}|{seq}|{parts[1]}", parts[2]))
                throw new IOException("Invalid or replayed mouse message.");

            _received = seq;
            MousePacket? packet = JsonSerializer.Deserialize<MousePacket>(Convert.FromBase64String(parts[1]));
            if (packet is null) throw new IOException("Invalid mouse packet.");
            if (packet.Kind != "ping") onPacket(packet);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stopping.Cancel();
        _outgoing.Writer.TryComplete();
        _client.Dispose();
    }
}
