using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using KiwisCoOpModCore;

namespace KiwisCoOpModCore.Tests
{
    // Stands in for Half-Life: Alyx's VConsole port (UPGRADE_PLAN.md W8): speaks protocol 211,
    // records the CMND packets it receives and sends scripted PRNT lines, so client-side tests
    // (B4, S1, S2) run without the game. One client at a time, on a free loopback port.
    public sealed class FakeVConsoleServer : IAsyncDisposable
    {
        public const int Protocol = 211;

        private readonly TcpListener listener;
        private readonly CancellationTokenSource cancellation = new();
        private readonly Channel<string> commands = System.Threading.Channels.Channel.CreateUnbounded<string>();
        private readonly Channel<VConsolePacket> packets = System.Threading.Channels.Channel.CreateUnbounded<VConsolePacket>();
        private TcpClient? client;
        private NetworkStream? stream;
        private Task? readLoop;

        public FakeVConsoleServer()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
        }

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        // Every packet received, in order, CMND included.
        public ChannelReader<VConsolePacket> Packets => packets.Reader;

        // The text of each CMND packet received, in order.
        public ChannelReader<string> Commands => commands.Reader;

        // Connects a client to this server, as KCOM does to the game, and waits for the server side.
        public async Task<TcpClient> ConnectClientAsync()
        {
            TcpClient connecting = new();
            Task<TcpClient> accept = listener.AcceptTcpClientAsync(cancellation.Token).AsTask();
            await connecting.ConnectAsync(IPAddress.Loopback, Port);
            await AcceptAsync(accept);
            return connecting;
        }

        // Waits for a client that connects some other way.
        public Task AcceptAsync() => AcceptAsync(listener.AcceptTcpClientAsync(cancellation.Token).AsTask());

        private async Task AcceptAsync(Task<TcpClient> accept)
        {
            client?.Dispose();
            client = await accept;
            client.NoDelay = true;
            stream = client.GetStream();
            NetworkStream current = stream;
            readLoop = Task.Run(() => ReadLoopAsync(current));
        }

        private async Task ReadLoopAsync(NetworkStream from)
        {
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    VConsolePacket packet = await VConsoleFraming.ReadPacketAsync(from, cancellation.Token);
                    await packets.Writer.WriteAsync(packet);
                    if (packet.Type == "CMND")
                    {
                        string text = Encoding.ASCII.GetString(packet.Payload);
                        await commands.Writer.WriteAsync(text.TrimEnd('\0'));
                    }
                }
            }
            catch (Exception e) when (e is IOException or EndOfStreamException or OperationCanceledException or ObjectDisposedException or InvalidDataException)
            {
                // Client gone or server stopping.
            }
        }

        public async Task<string> NextCommandAsync(TimeSpan? timeout = null)
        {
            using CancellationTokenSource cts = new(timeout ?? TimeSpan.FromSeconds(5));
            return await commands.Reader.ReadAsync(cts.Token);
        }

        public Task SendPrintAsync(string line) => SendRawAsync(VConsoleFraming.EncodePrint(line, Protocol));

        // Sends bytes as they are: whole packets, several at once, part of one, or garbage.
        public async Task SendRawAsync(byte[] data)
        {
            NetworkStream to = stream ?? throw new InvalidOperationException("No client connected.");
            await to.WriteAsync(data, cancellation.Token);
            await to.FlushAsync(cancellation.Token);
        }

        // Sends data in pieces of the given size, pausing between them, so the client sees split reads.
        public async Task SendSplitAsync(byte[] data, int chunkSize, TimeSpan pause)
        {
            for (int offset = 0; offset < data.Length; offset += chunkSize)
            {
                await SendRawAsync(data.AsSpan(offset, Math.Min(chunkSize, data.Length - offset)).ToArray());
                await Task.Delay(pause, cancellation.Token);
            }
        }

        // Drops the current client, as the game does when it quits.
        public void DropClient()
        {
            client?.Dispose();
            client = null;
            stream = null;
        }

        public async ValueTask DisposeAsync()
        {
            cancellation.Cancel();
            DropClient();
            listener.Stop();
            if (readLoop != null)
                await readLoop;
            cancellation.Dispose();
        }
    }
}
