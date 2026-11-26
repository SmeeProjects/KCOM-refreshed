using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Channels;
using System.Threading.Tasks;
using KiwisCoOpModCore;
using Xunit;

namespace KiwisCoOpModCore.Tests
{
    // The client side of VConsole (StreamWatcher and VConsoleFraming) against the fake server (B4, W8).
    // StreamWatcher logs lost connections, so these share the "Log" collection with LogTests.
    [Collection("Log")]
    public class FakeVConsoleServerTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        [Fact]
        public async Task Server_RecordsCommands_IncludingOver255Bytes()
        {
            await using FakeVConsoleServer server = new();
            using TcpClient client = await server.ConnectClientAsync();
            NetworkStream stream = client.GetStream();
            string longCommand = "say " + new string('k', 1200);

            await stream.WriteAsync(VConsoleFraming.EncodeWindowFocus(true, FakeVConsoleServer.Protocol), TestContext.Current.CancellationToken);
            await stream.WriteAsync(VConsoleFraming.EncodeCommand("addon_play a3_hotel_lobby_basement", FakeVConsoleServer.Protocol), TestContext.Current.CancellationToken);
            await stream.WriteAsync(VConsoleFraming.EncodeCommand(longCommand, FakeVConsoleServer.Protocol), TestContext.Current.CancellationToken);

            Assert.Equal("addon_play a3_hotel_lobby_basement", await server.NextCommandAsync());
            Assert.Equal(longCommand, await server.NextCommandAsync());
            Assert.Equal("VFCS", (await ReadAsync(server.Packets)).Type);
        }

        [Fact]
        public async Task Watcher_ReadsPrintsSplitAcrossReads()
        {
            await using FakeVConsoleServer server = new();
            using TcpClient client = await server.ConnectClientAsync();
            Channel<string> lines = WatchPrints(client, out _);
            string longLine = new('z', 700);

            await server.SendSplitAsync(VConsoleFraming.EncodePrint(longLine, FakeVConsoleServer.Protocol), 5, TimeSpan.FromMilliseconds(1));
            await server.SendSplitAsync(VConsoleFraming.EncodePrint("after", FakeVConsoleServer.Protocol), 1, TimeSpan.FromMilliseconds(1));

            Assert.Equal(longLine, await ReadAsync(lines.Reader));
            Assert.Equal("after", await ReadAsync(lines.Reader));
        }

        [Fact]
        public async Task Watcher_ReadsPrintsMergedInOneRead()
        {
            await using FakeVConsoleServer server = new();
            using TcpClient client = await server.ConnectClientAsync();
            Channel<string> lines = WatchPrints(client, out _);
            string[] sent = Enumerable.Range(0, 20).Select(i => "line " + i + " " + new string('m', i * 40)).ToArray();

            // Every packet in a single write, the last one split with the next write.
            byte[] all = sent.SelectMany(line => VConsoleFraming.EncodePrint(line, FakeVConsoleServer.Protocol)).ToArray();
            await server.SendRawAsync(all[..^7]);
            await Task.Delay(20, TestContext.Current.CancellationToken);
            await server.SendRawAsync(all[^7..]);

            foreach (string line in sent)
                Assert.Equal(line, await ReadAsync(lines.Reader));
        }

        [Fact]
        public async Task Watcher_OutOfSyncStream_FaultsOnceAndStops()
        {
            await using FakeVConsoleServer server = new();
            using TcpClient client = await server.ConnectClientAsync();
            Channel<string> lines = WatchPrints(client, out Channel<Exception> faults);

            await server.SendPrintAsync("before");
            await server.SendRawAsync(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0, 0, 0, 0, 0, 0, 0, 0 });
            await server.SendPrintAsync("never read");

            Assert.Equal("before", await ReadAsync(lines.Reader));
            Assert.IsType<System.IO.InvalidDataException>(await ReadAsync(faults.Reader));
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.False(lines.Reader.TryRead(out _));
            Assert.False(faults.Reader.TryRead(out _));
        }

        [Fact]
        public async Task Watcher_GameQuits_FaultsAndReconnectWorks()
        {
            await using FakeVConsoleServer server = new();
            TcpClient client = await server.ConnectClientAsync();
            Channel<string> lines = WatchPrints(client, out Channel<Exception> faults);

            server.DropClient();
            Assert.IsAssignableFrom<Exception>(await ReadAsync(faults.Reader));
            client.Dispose();

            // What VConsole.Connect() does after ConnectionLost.
            using TcpClient again = await server.ConnectClientAsync();
            Channel<string> newLines = WatchPrints(again, out _);
            await server.SendPrintAsync("welcome back");
            Assert.Equal("welcome back", await ReadAsync(newLines.Reader));
        }

        [Fact]
        public async Task Watcher_StoppedOnPurpose_DoesNotFault()
        {
            await using FakeVConsoleServer server = new();
            TcpClient client = await server.ConnectClientAsync();
            Channel<Exception> faults = System.Threading.Channels.Channel.CreateUnbounded<Exception>();
            StreamWatcher watcher = new(client.GetStream());
            watcher.Faulted += (_, e) => faults.Writer.TryWrite(e);
            watcher.SetWorking(true);

            // As VConsole.Disconnect() does.
            watcher.SetWorking(false);
            client.Dispose();

            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.False(faults.Reader.TryRead(out _));
        }

        private static Channel<string> WatchPrints(TcpClient client, out Channel<Exception> faults)
        {
            Channel<string> lines = System.Threading.Channels.Channel.CreateUnbounded<string>();
            Channel<Exception> faulted = System.Threading.Channels.Channel.CreateUnbounded<Exception>();
            StreamWatcher watcher = new(client.GetStream());
            watcher.MessageAvailable += (_, e) =>
            {
                if (e.MessageType == "PRNT")
                    lines.Writer.TryWrite(VConsoleFraming.DecodePrint(e.Data));
            };
            watcher.Faulted += (_, e) => faulted.Writer.TryWrite(e);
            watcher.SetWorking(true);
            faults = faulted;
            return lines;
        }

        private static async Task<T> ReadAsync<T>(ChannelReader<T> reader)
        {
            return await reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(Timeout, TestContext.Current.CancellationToken);
        }
    }
}
