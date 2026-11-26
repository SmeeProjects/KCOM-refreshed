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
using System.IO;

// https://stackoverflow.com/a/2784979

namespace KiwisCoOpModCore
{
    public delegate void MessageAvailableEventHandler(object sender,
    MessageAvailableEventArgs e);

    public delegate void StreamFaultedEventHandler(object sender, Exception e);

    public class MessageAvailableEventArgs : EventArgs
    {
        public MessageAvailableEventArgs(string messageType, byte[] data) : base()
        {
            MessageType = messageType;
            Data = data;
        }

        public string MessageType { get; private set; }
        // The packet's payload, after its 12-byte header (VConsoleFraming).
        public byte[] Data { get; private set;}
    }
    public class StreamWatcher
    {
        private readonly Stream stream;

        private volatile bool working = false;

        public StreamWatcher(Stream stream)
        {
            this.stream = stream;
        }

        public bool IsWorking => working;

        public void SetWorking(bool state)
        {
            bool oldWorking = working;
            working = state;
            if(oldWorking != state && state)
                Read();
        }

        protected void OnMessageAvailable(MessageAvailableEventArgs e)
        {
            MessageAvailable?.Invoke(this, e);
        }

        protected void OnFaulted(Exception e)
        {
            Faulted?.Invoke(this, e);
        }

        protected async void Read()
        {
            while (working)
            {
                VConsolePacket packet;
                try
                {
                    packet = await VConsoleFraming.ReadPacketAsync(stream);
                }
                catch (Exception e)
                {
                    // The stream ended, broke or got out of sync (B4): reading on would only
                    // spin or misread every later packet, so stop and let the owner reconnect.
                    // Nothing to report when SetWorking(false) closed it on purpose.
                    bool wasWorking = working;
                    working = false;
                    if (wasWorking)
                    {
                        Log.Warning("VConsole", "Connection to VConsole lost", e);
                        OnFaulted(e);
                    }
                    return;
                }
                try
                {
                    OnMessageAvailable(new MessageAvailableEventArgs(packet.Type, packet.Payload));
                }
                catch (Exception e)
                {
                    // A failing handler must not stop reading.
                    Log.Error("VConsole", e, "Failed to handle a VConsole message");
                }
            }
        }

        public event MessageAvailableEventHandler? MessageAvailable;
        // Raised once when reading stops on an error; the watcher is then no longer working.
        public event StreamFaultedEventHandler? Faulted;
    }
}
