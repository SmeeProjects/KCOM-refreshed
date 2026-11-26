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
using KiwisCoOpModCore;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Websocket.Client;

namespace KiwisCoOpMod
{
    public class VConsole
    {
        private WebsocketClient? ws;
        private TcpClient? client;
        private NetworkStream? stream;
        private bool killed = false;
        private List<byte[]> commandQueue = new();
        private StreamWatcher? watcher;
        public VConsole()
        {
        }
        public bool Connect(WebsocketClient ws)
        {
            this.ws = ws;
            return Connect();
        }
        public bool Connect()
        {
            WriteCommand("disconnect");
            // Check if the port is listening before attempting to connect
            System.Net.IPEndPoint[] endPoints = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            bool listening = false;
            foreach (System.Net.IPEndPoint endPoint in endPoints)
            {
                if (endPoint.Port == Settings.Default.VconsolePort)
                {
                    listening = true;
                    break;
                }
            }
            if (!listening)
                return false;
            try
            {
                client = new TcpClient("127.0.0.1", Settings.Default.VconsolePort);
            }
            catch (SocketException)
            {
                // The game closed the port since the check above (e.g. while reconnecting).
                return false;
            }
            stream = client.GetStream();
            watcher = new StreamWatcher(stream);
            watcher.MessageAvailable += MessageAvailable;
            watcher.Faulted += Faulted;
            if (watcher != null)
                watcher.SetWorking(true);
            killed = false;
            WriteWindowFocus(true);
            if (stream != null)
            {
                foreach (byte[] command in commandQueue)
                {
                    stream.Write(command);
                }
                commandQueue.Clear();
            }
            return true;
        }

        private void MessageAvailable(object sender, MessageAvailableEventArgs e)
        {
            if(ws != null)
            {
                string command = e.MessageType.ToUpper();
                switch (command)
                {
                    case "PRNT":
                        string newMessage = VConsoleFraming.DecodePrint(e.Data).Replace("\n", "");
                        if(newMessage != "" && !newMessage.Contains("Command buffer full"))
                        {
                            /*
                            if (newMessage.Contains("Command buffer full"))
                            {
                                vcAmount += 1;
                                if (vcAmount >= 10)
                                {
                                    //Reconnect();
                                    vcAmount = 0;
                                }
                            }
                            else
                            {
                            */
                                Response input = new("print", newMessage);
                                input.timestamp = (long)DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds;
                                ws.Send(JsonConvert.SerializeObject(input));
                            //}
                        }
                        break;
                }
            }
        }

        private void Faulted(object sender, Exception e)
        {
            // Only the current connection's watcher matters, and not once Disconnect() ran.
            if (sender != watcher || killed)
                return;
            Disconnect();
            ConnectionLost?.Invoke(e);
        }

        // Raised when the game closed VConsole or the stream got out of sync (B4).
        // The connection is already closed: call Connect() again to reconnect.
        public event Action<Exception>? ConnectionLost;

        /*
        public void Reconnect()
        {
            killed = true;
            if (stream != null)
                stream.Close();
            if (client != null)
                client.Close();
            if (watcher != null)
                watcher.SetWorking(false);
            client = new TcpClient("127.0.0.1", Settings.Default.VconsolePort);
            stream = client.GetStream();
            Connect();
        }
        */

        public void Disconnect()
        {
            if(!killed)
            {
                killed = true;
                if (stream != null)
                    stream.Close();
                if (client != null)
                    client.Close();
                if (watcher != null)
                    watcher.SetWorking(false);
            }
        }

        public void WriteRaw(byte[] data)
        {
            if (stream != null)
                stream.Write(data);
        }

        public void WriteWindowFocus(bool focused = true)
        {
            if (stream != null)
            {
                byte[] completeData = VConsoleFraming.EncodeWindowFocus(focused, Settings.Default.VconsoleProtocol);
                if (!killed)
                    stream.Write(completeData);
                else
                    commandQueue.Add(completeData);
            }
        }
        public void WriteCommand(string command, bool urgent = false)
        {
            // Longer commands don't fit in a packet's 16-bit length (B4).
            if (stream != null && command.Length <= VConsoleFraming.MaxCommandLength)
            {
                byte[] completeData = VConsoleFraming.EncodeCommand(command, Settings.Default.VconsoleProtocol);
                if (!killed)
                    stream.Write(completeData);
                else if (urgent)
                    commandQueue.Add(completeData);
            }
        }
    }
}
