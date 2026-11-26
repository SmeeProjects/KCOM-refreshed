using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KiwisCoOpModCore;
using Xunit;

namespace KiwisCoOpModCore.Tests
{
    // VConsole framing (UPGRADE_PLAN.md B4).
    public class VConsoleFramingTests
    {
        [Fact]
        public void EncodeCommand_MatchesProtocol211Layout()
        {
            byte[] packet = VConsoleFraming.EncodeCommand("sv_cheats 1", 211);

            byte[] expected = Encoding.ASCII.GetBytes("CMND")
                .Concat(new byte[] { 0x00, 0xD3, 0x00, 0x00, 0x00, 24, 0x00, 0x00 })
                .Concat(Encoding.ASCII.GetBytes("sv_cheats 1"))
                .Append((byte)0)
                .ToArray();
            Assert.Equal(expected, packet);
        }

        [Fact]
        public void EncodeWindowFocus_IsThirteenBytes()
        {
            byte[] packet = VConsoleFraming.EncodeWindowFocus(true, 211);

            Assert.Equal(new byte[] { (byte)'V', (byte)'F', (byte)'C', (byte)'S', 0, 0xD3, 0, 0, 0, 13, 0, 0, 1 }, packet);
        }

        [Fact]
        public void EncodeCommand_Over255Bytes_UsesBigEndianLength()
        {
            string command = "say " + new string('a', 996);

            byte[] packet = VConsoleFraming.EncodeCommand(command, 211);

            Assert.Equal(1013, packet.Length);
            Assert.Equal(0x03, packet[8]);
            Assert.Equal(0xF5, packet[9]);
        }

        [Fact]
        public void EncodeCommand_TooLongForAPacket_Throws()
        {
            Assert.Throws<ArgumentException>(() => VConsoleFraming.EncodeCommand(new string('a', VConsoleFraming.MaxCommandLength + 1), 211));
            Assert.Equal(VConsoleFraming.MaxPacketLength, VConsoleFraming.EncodeCommand(new string('a', VConsoleFraming.MaxCommandLength), 211).Length);
        }

        [Fact]
        public async Task ReadPacket_RoundTripsLongCommand()
        {
            string command = "script " + new string('x', 5000);
            using MemoryStream stream = new(VConsoleFraming.EncodeCommand(command, 211));

            VConsolePacket packet = await VConsoleFraming.ReadPacketAsync(stream, TestContext.Current.CancellationToken);

            Assert.Equal("CMND", packet.Type);
            Assert.Equal(VConsoleFraming.VersionFromProtocol(211), packet.Version);
            Assert.Equal(command + "\0", Encoding.ASCII.GetString(packet.Payload));
        }

        [Fact]
        public async Task ReadPacket_OneByteReads_StillReadsWholePackets()
        {
            byte[] data = VConsoleFraming.EncodePrint(new string('p', 300), 211)
                .Concat(VConsoleFraming.EncodePrint("second", 211)).ToArray();
            using OneByteStream stream = new(data);

            VConsolePacket first = await VConsoleFraming.ReadPacketAsync(stream, TestContext.Current.CancellationToken);
            VConsolePacket second = await VConsoleFraming.ReadPacketAsync(stream, TestContext.Current.CancellationToken);

            Assert.Equal(new string('p', 300), VConsoleFraming.DecodePrint(first.Payload));
            Assert.Equal("second", VConsoleFraming.DecodePrint(second.Payload));
        }

        [Fact]
        public async Task ReadPacket_LengthShorterThanHeader_IsOutOfSync()
        {
            byte[] packet = VConsoleFraming.EncodePrint("hi", 211);
            packet[8] = 0;
            packet[9] = 4;
            using MemoryStream stream = new(packet);

            await Assert.ThrowsAsync<InvalidDataException>(() => VConsoleFraming.ReadPacketAsync(stream, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task ReadPacket_GarbageType_IsOutOfSync()
        {
            using MemoryStream stream = new(Enumerable.Repeat((byte)0xFF, 32).ToArray());

            await Assert.ThrowsAsync<InvalidDataException>(() => VConsoleFraming.ReadPacketAsync(stream, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task ReadPacket_StreamEndsMidPacket_Throws()
        {
            byte[] packet = VConsoleFraming.EncodePrint("cut short", 211);
            using MemoryStream stream = new(packet.AsSpan(0, packet.Length - 3).ToArray());

            await Assert.ThrowsAsync<EndOfStreamException>(() => VConsoleFraming.ReadPacketAsync(stream, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void DecodePrint_StripsPrefixAndNul()
        {
            byte[] packet = VConsoleFraming.EncodePrint("Hello\n", 211);

            Assert.Equal("Hello\n", VConsoleFraming.DecodePrint(packet.AsSpan(VConsoleFraming.HeaderLength)));
            Assert.Equal("", VConsoleFraming.DecodePrint(new byte[10]));
        }

        // Returns at most one byte per read, like the worst split TCP reads.
        private sealed class OneByteStream(byte[] data) : MemoryStream(data)
        {
            public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 1));

            public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length, 1)]);

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                base.ReadAsync(buffer, offset, Math.Min(count, 1), cancellationToken);

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
                base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], cancellationToken);
        }
    }
}
