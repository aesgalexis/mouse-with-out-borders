using System.Net;
using System.Net.Sockets;

namespace MouseWithOutBorders;

internal static class Program
{
    private static async Task Main()
    {
        Console.WriteLine("Mouse peer protocol smoke tests...");

        byte[] key = MousePeer.ParseKey(MousePeer.NewKey());
        if (key.Length != 16) throw new Exception("Incorrect key length.");
        await TestAuthenticatedRoundTrip(key);
        Console.WriteLine("PASS: authenticated mouse data roundtrip.");
        await TestIncorrectKeyRejected(key);
        Console.WriteLine("PASS: incorrect pairing key denied.");

        Console.WriteLine("All protocol smoke tests passed.");
    }

    private static async Task TestAuthenticatedRoundTrip(byte[] key)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(12));
        TcpListener listener = new(IPAddress.Loopback, MousePeer.Port);
        listener.Start();
        try
        {
            TaskCompletionSource<MousePacket> gotMove = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<MousePacket> gotReply = new(TaskCreationOptions.RunContinuationsAsynchronously);

            Task server = Task.Run(async () =>
            {
                using TcpClient socket = await listener.AcceptTcpClientAsync(timeout.Token);
                using MousePeer peer = await MousePeer.AcceptAsync(socket, key, timeout.Token);
                await peer.ReadLoopAsync(packet =>
                {
                    if (packet.Kind == "move")
                    {
                        gotMove.TrySetResult(packet);
                        peer.Queue(new MousePacket("return", Ratio: 0.4));
                    }
                }, timeout.Token);
            }, timeout.Token);

            using MousePeer controller = await MousePeer.ConnectAsync("127.0.0.1", key, timeout.Token);
            Task read = controller.ReadLoopAsync(packet =>
            {
                if (packet.Kind == "return") gotReply.TrySetResult(packet);
            }, timeout.Token);

            if (!controller.Queue(new MousePacket("move", Dx: 26, Dy: -11)))
                throw new Exception("Could not queue mouse move.");

            MousePacket move = await gotMove.Task.WaitAsync(timeout.Token);
            MousePacket reply = await gotReply.Task.WaitAsync(timeout.Token);
            if (move.Dx != 26 || move.Dy != -11 || reply.Ratio != 0.4)
                throw new Exception("Mouse packets were corrupted in transit.");

            timeout.Cancel();
            try { await Task.WhenAll(read, server); }
            catch (OperationCanceledException) { }
        }
        finally { listener.Stop(); }
    }

    private static async Task TestIncorrectKeyRejected(byte[] key)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(12));
        TcpListener listener = new(IPAddress.Loopback, MousePeer.Port);
        listener.Start();
        try
        {
            Task server = Task.Run(async () =>
            {
                using TcpClient tcp = await listener.AcceptTcpClientAsync(timeout.Token);
                try
                {
                    using MousePeer _ = await MousePeer.AcceptAsync(tcp, key, timeout.Token);
                    throw new Exception("Receiver accepted a wrong pairing key.");
                }
                catch (UnauthorizedAccessException) { /* expected */ }
            }, timeout.Token);

            byte[] wrong = MousePeer.ParseKey(MousePeer.NewKey());
            try
            {
                using MousePeer _ = await MousePeer.ConnectAsync("127.0.0.1", wrong, timeout.Token);
                throw new Exception("Controller connected using wrong pairing key.");
            }
            catch (UnauthorizedAccessException) { /* expected */ }
            await server;
        }
        finally { listener.Stop(); }
    }
}
