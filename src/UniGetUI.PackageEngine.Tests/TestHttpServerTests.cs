using UniGetUI.PackageEngine.Tests.Infrastructure.Helpers;

namespace UniGetUI.PackageEngine.Tests;

public sealed class TestHttpServerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void ImmediateStartupAndDisposeDoesNotLeakAcceptFailures()
    {
        // A bounded stress supplement; this does not claim to force Windows' internal bind race.
        for (int attempt = 0; attempt < 100; attempt++)
        {
            using var server = new TestHttpServer(_ => (200, "ok", "text/plain"));
        }
    }

    [Fact]
    public async Task DisposeCompletesAfterActiveHandlerIsReleased()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int handled = 0;
        var server = new TestHttpServer(_ =>
        {
            Interlocked.Increment(ref handled);
            entered.Set();
            if (!release.Wait(Timeout))
                throw new TimeoutException("The test did not release the active handler.");
            return (200, "ok", "text/plain");
        });
        using var client = new HttpClient { Timeout = Timeout };
        using var cancelRequest = new CancellationTokenSource();
        Task<HttpResponseMessage> request = client.GetAsync(server.BaseUri, cancelRequest.Token);
        Task? disposal = null;
        try
        {
            Assert.True(entered.Wait(Timeout), "The handler did not start.");
            var disposeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            disposal = Task.Run(() =>
            {
                disposeStarted.SetResult();
                server.Dispose();
            });
            await disposeStarted.Task.WaitAsync(Timeout);
            Assert.False(disposal.IsCompleted);
            release.Set();
            await disposal.WaitAsync(Timeout);
            Assert.Equal(1, Volatile.Read(ref handled));
        }
        finally
        {
            release.Set();
            cancelRequest.Cancel();
            try
            {
                if (disposal is not null)
                    await disposal.WaitAsync(Timeout);
                else
                    server.Dispose();
            }
            finally
            {
                await ObserveShutdownRequest(request);
            }
        }
    }

    [Fact]
    public async Task DisposePreservesTheOriginalHandlerArgumentException()
    {
        using var entered = new ManualResetEventSlim();
        var expected = new ArgumentException("Handler sentinel; must not be mistaken for listener shutdown.", "handlerArgument");
        var server = new TestHttpServer(_ =>
        {
            entered.Set();
            throw expected;
        });
        using var client = new HttpClient { Timeout = Timeout };
        using var cancelRequest = new CancellationTokenSource();
        Task<HttpResponseMessage> request = client.GetAsync(server.BaseUri, cancelRequest.Token);
        bool disposed = false;
        try
        {
            Assert.True(entered.Wait(Timeout), "The handler did not start.");
            disposed = true;
            ArgumentException actual = Assert.Throws<ArgumentException>(server.Dispose);
            Assert.Same(expected, actual);
        }
        finally
        {
            cancelRequest.Cancel();
            try
            {
                if (!disposed)
                    server.Dispose();
            }
            finally
            {
                await ObserveShutdownRequest(request);
            }
        }
    }

    private static async Task ObserveShutdownRequest(Task<HttpResponseMessage> request)
    {
        try
        {
            using HttpResponseMessage response = await request.WaitAsync(Timeout);
        }
        catch (HttpRequestException)
        {
            // Closing the listener may abort the client's connection.
        }
        catch (OperationCanceledException)
        {
            // The test cancels its own client request during cleanup.
        }
    }
}
