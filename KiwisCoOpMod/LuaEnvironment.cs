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
using NLua;
using KiwisCoOpModCore;
using Fleck;
using Newtonsoft.Json;

namespace KiwisCoOpMod
{
    // Lua runs only on its own thread (UPGRADE_PLAN.md B3): an NLua state can't be used from two threads,
    // and running it on the UI thread made every network message wait for the window.
    // Every caller waits for its handler, so a Pre* handler still runs before what it precedes, the Lua
    // handlers keep their order around the gamemode's, and they only touch the server's lists while the
    // server thread waits for them (B2). The Lua thread never waits for another one, so this can't deadlock:
    // Lua must not Invoke the UI, and LogToOutput, LogToOutputGeneric and SetStatus work from any thread.
    public sealed class LuaEnvironment
    {
        public static readonly LuaEnvironment instance = new();
        private readonly WorkQueue queue = new("KCOM Lua", e => Log.Error("Lua", e));
        Lua lua;
        public LuaEnvironment()
        {
            lua = new();
            queue.Post(() =>
            {
                lua.LoadCLRPackage();
                // Just in case the front-end Lua scripts don't implement __init.lua
                lua.DoString(@"
                    printerr = function(...)
                        local channel = Channel('ERR', 'Lua Errors', Color.Red)
                        if Program.userInterface then
                            Program.userInterface:LogToOutput(channel, ...)
                        end
                    end
                ");
                LoadFiles();
            });
        }
        public void Dispose()
        {
            queue.Post(lua.Dispose);
            queue.Complete();
        }
        // How long the UI thread waits for Lua. A script that still Invokes the window (an old __init.lua's
        // print) would otherwise hang both threads; past this the UI thread goes on and the handler runs late.
        private static readonly TimeSpan uiWaitTimeout = TimeSpan.FromSeconds(2);
        // Runs the action on the Lua thread and returns once it has run (at once when already there).
        private void OnLuaThread(Action action)
        {
            if (Thread.CurrentThread != Program.uiThread)
                queue.Run(action);
            // While the window is being built, Program.userInterface is still null and the Lua startup
            // waits for it: only queue (UserInterface_Initialized).
            else if (Program.userInterface == null)
                queue.Post(action);
            else if (!queue.Run(action, uiWaitTimeout))
                Log.Warning("Lua", "A Lua handler took over " + uiWaitTimeout.TotalSeconds + " s, so the window stopped waiting for it. Scripts must not call Invoke.");
        }
        public void LoadFiles()
        {
            OnLuaThread(() =>
            {
                // Create "scripts" directory if it doesn't exist
                if (!Directory.Exists("scripts"))
                    Directory.CreateDirectory("scripts");
                // Load scripts (.lua files) in "scripts" directory
                List<string> foundFiles = Directory.GetFiles("scripts", "*.lua", SearchOption.AllDirectories).ToList();
                foundFiles.Sort(); // Sort alphabetically
                foreach (string file in foundFiles)
                {
                    lua.DoFile(file);
                }
            });
        }
        public void RunFile(string file)
        {
            OnLuaThread(() =>
            {
                try
                {
                    lua.DoFile(file);
                }
                catch (Exception e)
                {
                    LuaError(e);
                }
            });
        }
        public void RunLua(string luaCode)
        {
            OnLuaThread(() =>
            {
                try
                {
                    lua.DoString(luaCode);
                }
                catch (Exception e)
                {
                    LuaError(e);
                }
            });
        }

        // Lua does not implement splitting
        public string StringSplit(string str, string delimiter)
        {
            return JsonConvert.SerializeObject(str.Split(delimiter));
        }
        public void LuaError(Exception e)
        {
            // printerr shows it in the "Lua Errors" tab, so the log only writes it to the file.
            LuaFunction? printerror = lua["printerr"] as LuaFunction;
            if (printerror != null)
            {
                try
                {
                    printerror.Call(e.Message);
                    Log.Error("Lua", e, notify: false);
                    return;
                }
                catch (Exception printerrError)
                {
                    Log.Error("Lua", printerrError, "printerr failed");
                }
            }
            Log.Error("Lua", e);
        }
        public void Handle(PluginHandleType handleType, params object[] vs)
        {
            OnLuaThread(() => HandleInternal(handleType, vs));
        }
        public void HandleInternal(PluginHandleType handleType, params object[] vs)
        {
            // Load cached scripts
            try
            {
                LuaFunction? scriptFunc = lua["handle"] as LuaFunction;
                if (scriptFunc != null && vs != null)
                {
                    switch (handleType)
                    {
                        case PluginHandleType.Client_PreResponse:
                        case PluginHandleType.Client_PostResponse:
                            scriptFunc.Call(handleType.ToString(), (Response)vs[0]);
                            break;
                        case PluginHandleType.Server_PostGamemode_PreStart:
                        case PluginHandleType.Server_PreGamemode_PostStart:
                        case PluginHandleType.Server_PostGamemode_PostStart:
                        case PluginHandleType.Server_PreGamemode_PreClose:
                        case PluginHandleType.Server_PostGamemode_PreClose:
                        case PluginHandleType.Server_PreGamemode_PostClose:
                        case PluginHandleType.Server_PostGamemode_PostClose:
                            scriptFunc.Call(handleType.ToString(), (Type)vs[0], (List<Type>)vs[1], (string)vs[2]);
                            break;
                        case PluginHandleType.Server_PreGamemode_PreResponse:
                        case PluginHandleType.Server_PostGamemode_PreResponse:
                        case PluginHandleType.Server_PreGamemode_PostResponse:
                        case PluginHandleType.Server_PostGamemode_PostResponse:
                            scriptFunc.Call(handleType.ToString(), (Response)vs[0], (List<IndexedClient>)vs[1], (IWebSocketConnection)vs[2], (string)vs[3]);
                            break;
                        case PluginHandleType.Server_PreGamemode_ClientOpen:
                        case PluginHandleType.Server_PostGamemode_ClientOpen:
                        case PluginHandleType.Server_PreGamemode_ClientClose:
                        case PluginHandleType.Server_PostGamemode_ClientClose:
                            scriptFunc.Call(handleType.ToString(), (List<IndexedClient>)vs[0], (IWebSocketConnection)vs[1], (string)vs[2]);
                            break;
                        case PluginHandleType.Server_PreGamemode_Command:
                        case PluginHandleType.Server_PostGamemode_Command:
                            scriptFunc.Call(handleType.ToString(), (List<string>)vs[0], (List<IndexedClient>)vs[1], (Type)vs[2]);
                            break;
                        case PluginHandleType.Server_PreGamemode_Think:
                        case PluginHandleType.Server_PostGamemode_Think:
                            scriptFunc.Call(handleType.ToString(), (int)vs[0], (List<IndexedClient>)vs[1], (string)vs[2]);
                            break;
                        default:
                            scriptFunc.Call(handleType.ToString());
                            break;
                    }
                }
                else if (scriptFunc != null)
                {
                    scriptFunc.Call(handleType);
                }
            }
            catch (Exception e)
            {
                LuaError(e);
            }
        }
    }
}
