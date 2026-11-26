/*
    Kiwi's Co-Op Mod for Half-Life: Alyx
    Copyright (c) 2022 KiwifruitDev
    All rights reserved.
    This software is licensed under the MIT License.
    -----------------------------------------------------------------------------
    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.
    -----------------------------------------------------------------------------
*/
using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KiwisCoOpModCore
{
    // One VConsole2 packet. Payload is what follows the 12-byte header.
    public sealed record VConsolePacket(string Type, uint Version, ushort Handle, byte[] Payload);

    // Framing of VConsole2 packets (UPGRADE_PLAN.md B4). Every packet starts with a 12-byte header:
    // type (4 ASCII bytes), version (uint32 BE, protocol 211 is 0x00D30000),
    // total length including the header (uint16 BE), handle (uint16 BE).
    public static class VConsoleFraming
    {
        public const int HeaderLength = 12;
        public const int MaxPacketLength = ushort.MaxValue;
        // A command is sent with a trailing NUL.
        public const int MaxCommandLength = MaxPacketLength - HeaderLength - 1;
        // PRNT payloads start with the channel id, colour and other fields before the text.
        public const int PrintPrefixLength = 28;

        public static uint VersionFromProtocol(int protocol)
        {
            if (protocol < 0 || protocol > byte.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(protocol), protocol, "VConsole protocol must fit in one byte.");
            return (uint)protocol << 16;
        }

        public static byte[] Encode(string type, uint version, ReadOnlySpan<byte> payload, ushort handle = 0)
        {
            if (type.Length != 4)
                throw new ArgumentException("Packet type must be 4 characters.", nameof(type));
            int length = HeaderLength + payload.Length;
            if (length > MaxPacketLength)
                throw new ArgumentException("Packet is " + length + " bytes, more than " + MaxPacketLength + ".", nameof(payload));
            byte[] packet = new byte[length];
            Encoding.ASCII.GetBytes(type, packet.AsSpan(0, 4));
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4, 4), version);
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(8, 2), (ushort)length);
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(10, 2), handle);
            payload.CopyTo(packet.AsSpan(HeaderLength));
            return packet;
        }

        public static byte[] EncodeCommand(string command, int protocol)
        {
            byte[] payload = new byte[Encoding.ASCII.GetByteCount(command) + 1];
            Encoding.ASCII.GetBytes(command, payload);
            return Encode("CMND", VersionFromProtocol(protocol), payload);
        }

        public static byte[] EncodeWindowFocus(bool focused, int protocol)
        {
            return Encode("VFCS", VersionFromProtocol(protocol), new byte[] { (byte)(focused ? 1 : 0) });
        }

        // What the game sends for a console line. KCOM only reads PRNT packets, so this is for tests and tools.
        public static byte[] EncodePrint(string text, int protocol)
        {
            byte[] payload = new byte[PrintPrefixLength + Encoding.ASCII.GetByteCount(text) + 1];
            Encoding.ASCII.GetBytes(text, payload.AsSpan(PrintPrefixLength));
            return Encode("PRNT", VersionFromProtocol(protocol), payload);
        }

        public static string DecodePrint(ReadOnlySpan<byte> payload)
        {
            if (payload.Length <= PrintPrefixLength)
                return "";
            ReadOnlySpan<byte> text = payload[PrintPrefixLength..];
            int end = text.IndexOf((byte)0);
            return Encoding.ASCII.GetString(end < 0 ? text : text[..end]);
        }

        // Reads one whole packet, however the bytes are split across TCP reads.
        // Throws EndOfStreamException when the stream ends and InvalidDataException when the
        // header makes no sense: the stream is then out of sync and the caller should reconnect.
        public static async Task<VConsolePacket> ReadPacketAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            byte[] header = new byte[HeaderLength];
            await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
            for (int i = 0; i < 4; i++)
            {
                byte b = header[i];
                if (!((b >= 'A' && b <= 'Z') || (b >= '0' && b <= '9')))
                    throw new InvalidDataException("VConsole stream out of sync: invalid packet type " + Convert.ToHexString(header, 0, 4) + ".");
            }
            string type = Encoding.ASCII.GetString(header, 0, 4);
            uint version = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4, 4));
            int length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(8, 2));
            ushort handle = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(10, 2));
            if (length < HeaderLength)
                throw new InvalidDataException("VConsole stream out of sync: " + type + " packet length " + length + " is shorter than its header.");
            byte[] payload = new byte[length - HeaderLength];
            await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
            return new VConsolePacket(type, version, handle, payload);
        }
    }
}
