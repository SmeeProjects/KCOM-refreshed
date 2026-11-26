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
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace KiwisCoOpModCore
{
    // Runs queued work one item at a time on a single dedicated thread (UPGRADE_PLAN.md B2).
    // The server's connection list, the gamemode's player and entity lists and the plugin and Lua
    // handlers are only touched from WorkQueue.Server, so they need no locks of their own.
    // Fleck's socket threads, the tick loop, timers and the UI thread post work here instead of
    // running it themselves. Nothing waits on this queue from the UI thread, so the server thread
    // can still Invoke the UI without a deadlock.
    public sealed class WorkQueue
    {
        public static WorkQueue Server { get; } = new("KCOM server");

        private readonly System.Threading.Channels.Channel<Action> work = System.Threading.Channels.Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });
        private readonly Thread thread;
        private readonly string name;
        private readonly Action<Exception> onError;

        public WorkQueue(string name, Action<Exception>? onError = null)
        {
            this.name = name;
            this.onError = onError ?? (e => Log.Error(this.name, e, "Queued work failed"));
            thread = new Thread(Run)
            {
                IsBackground = true,
                Name = name
            };
            thread.Start();
        }

        public bool IsCurrentThread => Thread.CurrentThread == thread;

        // Queues the action and returns at once. Returns false once the queue is completed.
        public bool Post(Action action)
        {
            return work.Writer.TryWrite(action);
        }

        // Queues the action and completes when it has run. Its errors go to onError, not to the task.
        // Never wait on it from the queue's own thread, or from a thread the queued work Invokes.
        public Task RunAsync(Action action)
        {
            TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!Post(() =>
            {
                try
                {
                    action();
                }
                finally
                {
                    done.SetResult();
                }
            }))
                done.SetResult();
            return done.Task;
        }

        // Stops accepting work. What is already queued still runs, then the thread ends.
        public void Complete()
        {
            work.Writer.TryComplete();
        }

        public bool Join(TimeSpan timeout)
        {
            return thread.Join(timeout);
        }

        private void Run()
        {
            ChannelReader<Action> reader = work.Reader;
            while (reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            {
                while (reader.TryRead(out Action? action))
                {
                    try
                    {
                        action();
                    }
                    catch (Exception e)
                    {
                        try
                        {
                            onError(e);
                        }
                        catch
                        {
                            // A failing error handler must not stop the server thread.
                        }
                    }
                }
            }
        }
    }
}
