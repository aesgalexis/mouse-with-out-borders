using System.Net;
using System.Net.Sockets;

namespace MouseWithOutBorders;

internal static class BackpressureTests
{
    internal static async Task RunAsync()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        TcpListener listener = new(IPAddress.Loopback, MousePeer.Port);
        listener.Start();
        try
        {
            byte[] key = MousePeer.ParseKey(MousePeer.NewKey());
            Task<MousePeer> connecting = MousePeer.ConnectAsync("127.0.0.1", key, timeout.Token);
            using TcpClient socket = await listener.AcceptTcpClientAsync(timeout.Token);
            socket.ReceiveBufferSize = 1024;
            using MousePeer stalledReceiver = await MousePeer.AcceptAsync(socket, key, timeout.Token);
            using MousePeer sender = await connecting.WaitAsync(timeout.Token);

            // Complete real authentication, then leave the receiver unread.
            // Use ordinary packets and a fixed production bound: an unbounded
            // channel would accept every packet and fail this regression.
            const int maximumPackets = 65536;
            bool rejected = false;
            for (int i = 0; i < maximumPackets; i++)
            {
                if ((i & 1023) == 0) timeout.Token.ThrowIfCancellationRequested();
                if (!sender.Queue(new MousePacket("move", Dx: 1, Transfer: 1)))
                {
                    rejected = true;
                    break;
                }
            }
            if (!rejected)
                throw new Exception("A stalled peer accepted an unbounded mouse packet backlog.");

            // Teardown must cancel a writer blocked by socket backpressure and
            // remain safe when both the failure path and owner dispose it.
            sender.Dispose();
            sender.Dispose();
            if (sender.Queue(new MousePacket("button", Flags: NativeMouse.LEFTUP, Transfer: 1)))
                throw new Exception("A disposed backpressured peer still accepted mouse packets.");
            stalledReceiver.Dispose();
            stalledReceiver.Dispose();
        }
        finally { listener.Stop(); }
        Console.WriteLine("PASS: authenticated stalled peer bounds outgoing packets and disposes safely.");
    }
}
