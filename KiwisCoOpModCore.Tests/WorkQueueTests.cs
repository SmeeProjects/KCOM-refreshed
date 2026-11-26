using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KiwisCoOpModCore;
using Xunit;

namespace KiwisCoOpModCore.Tests
{
    // The server runs all its work on one WorkQueue, so its plain lists are never changed from two threads at once (UPGRADE_PLAN.md B2).
    public class WorkQueueTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        [Fact]
        public async Task ManyConnectsAndDisconnects_FromManyThreads_KeepTheListConsistent()
        {
            ConcurrentQueue<Exception> errors = new();
            WorkQueue queue = new("stress", errors.Enqueue);
            // Shaped like ServerProgram: one list of connections, changed by socket callbacks and enumerated by every handler and the tick loop.
            List<Guid> connections = new();
            HashSet<Guid> everConnected = new();
            int ticks = 0;
            int broadcasts = 0;
            const int sockets = 32;
            const int cyclesPerSocket = 300;

            using CancellationTokenSource stopTicking = new();
            Task tickLoop = Task.Run(async () =>
            {
                while (!stopTicking.IsCancellationRequested)
                {
                    await queue.RunAsync(() =>
                    {
                        foreach (Guid id in connections)
                            Assert.NotEqual(Guid.Empty, id);
                        ticks++;
                    });
                }
            }, TestContext.Current.CancellationToken);

            Task[] socketThreads = Enumerable.Range(0, sockets).Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < cyclesPerSocket; i++)
                {
                    Guid id = Guid.NewGuid();
                    // OnMessage("client"): add the connection, then tell everyone.
                    queue.Post(() =>
                    {
                        connections.Add(id);
                        everConnected.Add(id);
                        foreach (Guid other in connections)
                            broadcasts++;
                    });
                    // OnClose: tell everyone, then remove it.
                    queue.Post(() =>
                    {
                        foreach (Guid other in connections)
                            broadcasts++;
                        Assert.True(connections.Remove(id));
                    });
                }
            }, TestContext.Current.CancellationToken)).ToArray();

            await Task.WhenAll(socketThreads).WaitAsync(Timeout, TestContext.Current.CancellationToken);
            await queue.RunAsync(() => { }).WaitAsync(Timeout, TestContext.Current.CancellationToken);
            stopTicking.Cancel();
            await tickLoop.WaitAsync(Timeout, TestContext.Current.CancellationToken);

            Assert.Empty(errors);
            await queue.RunAsync(() =>
            {
                Assert.Empty(connections);
                Assert.Equal(sockets * cyclesPerSocket, everConnected.Count);
            }).WaitAsync(Timeout, TestContext.Current.CancellationToken);
            Assert.Empty(errors);
            Assert.True(ticks > 0);
            Assert.True(broadcasts >= 2 * sockets * cyclesPerSocket);
            queue.Complete();
            Assert.True(queue.Join(Timeout));
        }

        [Fact]
        public async Task Post_RunsInOrderOnOneDedicatedThread()
        {
            WorkQueue queue = new("order");
            List<int> order = new();
            HashSet<int> threadIds = new();
            bool onQueueThread = true;
            for (int i = 0; i < 1000; i++)
            {
                int value = i;
                queue.Post(() =>
                {
                    order.Add(value);
                    threadIds.Add(Environment.CurrentManagedThreadId);
                    onQueueThread &= queue.IsCurrentThread;
                });
            }
            await queue.RunAsync(() => { }).WaitAsync(Timeout, TestContext.Current.CancellationToken);

            Assert.Equal(Enumerable.Range(0, 1000), order);
            Assert.Single(threadIds);
            Assert.True(onQueueThread);
            Assert.False(queue.IsCurrentThread);
            queue.Complete();
        }

        [Fact]
        public async Task FailingWork_GoesToTheErrorHandler_AndLaterWorkStillRuns()
        {
            ConcurrentQueue<Exception> errors = new();
            WorkQueue queue = new("errors", errors.Enqueue);
            bool ranAfter = false;

            queue.Post(() => throw new InvalidOperationException("boom"));
            await queue.RunAsync(() => throw new ArgumentException("bang")).WaitAsync(Timeout, TestContext.Current.CancellationToken);
            await queue.RunAsync(() => ranAfter = true).WaitAsync(Timeout, TestContext.Current.CancellationToken);

            Assert.True(ranAfter);
            Assert.Collection(errors,
                e => Assert.IsType<InvalidOperationException>(e),
                e => Assert.IsType<ArgumentException>(e));
            queue.Complete();
        }

        [Fact]
        public async Task Complete_RunsWhatIsQueued_ThenRefusesNewWork()
        {
            WorkQueue queue = new("complete");
            int ran = 0;
            for (int i = 0; i < 100; i++)
                queue.Post(() => ran++);
            queue.Complete();

            Assert.True(queue.Join(Timeout));
            Assert.Equal(100, ran);
            Assert.False(queue.Post(() => ran++));
            await queue.RunAsync(() => ran++).WaitAsync(Timeout, TestContext.Current.CancellationToken);
            Assert.Equal(100, ran);
        }

        [Fact]
        public async Task Run_FromAnotherThread_WaitsForTheWorkOnTheQueueThread()
        {
            // The server and client socket threads wait like this for the Lua thread (UPGRADE_PLAN.md B3).
            ConcurrentQueue<Exception> errors = new();
            WorkQueue queue = new("run", errors.Enqueue);
            bool ran = false;
            bool onQueueThread = false;

            await Task.Run(() =>
            {
                queue.Run(() =>
                {
                    Thread.Sleep(50);
                    onQueueThread = queue.IsCurrentThread;
                    ran = true;
                });
                Assert.True(ran);
                queue.Run(() => throw new InvalidOperationException("boom"));
            }, TestContext.Current.CancellationToken).WaitAsync(Timeout, TestContext.Current.CancellationToken);

            Assert.True(onQueueThread);
            Assert.IsType<InvalidOperationException>(Assert.Single(errors));
            queue.Complete();
        }

        [Fact]
        public async Task Run_OnTheQueueThread_RunsAtOnceInsteadOfDeadlocking()
        {
            // A Lua handler that calls back into the Lua environment (refresh, refresh_all) does this.
            ConcurrentQueue<Exception> errors = new();
            WorkQueue queue = new("nested", errors.Enqueue);
            List<string> order = new();

            await queue.RunAsync(() =>
            {
                order.Add("outer");
                queue.Run(() => order.Add("inner"));
                queue.Run(() => throw new InvalidOperationException("boom"));
                order.Add("after");
            }).WaitAsync(Timeout, TestContext.Current.CancellationToken);

            Assert.Equal(new[] { "outer", "inner", "after" }, order);
            Assert.IsType<InvalidOperationException>(Assert.Single(errors));
            queue.Complete();
        }

        [Fact]
        public void Run_AfterComplete_ReturnsWithoutRunning()
        {
            WorkQueue queue = new("closed");
            queue.Complete();
            Assert.True(queue.Join(Timeout));
            bool ran = false;
            queue.Run(() => ran = true);
            Assert.False(ran);
        }
    }
}
