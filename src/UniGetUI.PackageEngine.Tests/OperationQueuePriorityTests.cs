using UniGetUI.PackageEngine.Enums;
using UniGetUI.PackageOperations;

namespace UniGetUI.PackageEngine.Tests;

[Collection(nameof(OperationOrchestrationTestCollection))]
public sealed class OperationQueuePriorityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForegroundPriorityPreservesSlotsPastAManagerWaiter(bool promoteWithRunNext)
    {
        using var pipGate = new SemaphoreSlim(1, 1);
        using var otherGate = new SemaphoreSlim(1, 1);
        using var foregroundGate = new SemaphoreSlim(1, 1);
        var concurrency = new OperationConcurrency();
        using var first = new GateControlledOperation(pipGate, concurrency, background: true);
        using var waiting = new GateControlledOperation(pipGate, concurrency, background: true);
        using var unrelated = new GateControlledOperation(otherGate, concurrency, background: true);
        using var pendingBackground = new GateControlledOperation(otherGate, concurrency, background: true);
        using var foreground = new GateControlledOperation(foregroundGate, concurrency, promoteWithRunNext);
        GateControlledOperation[] operations = [first, waiting, unrelated, pendingBackground, foreground];
        var runs = new List<Task>();
        int previousMax = AbstractOperation.MAX_OPERATIONS;
        AbstractOperation.OperationQueue.Clear();
        AbstractOperation.MAX_OPERATIONS = 2;

        try
        {
            runs.Add(first.MainThread());
            await first.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            runs.Add(waiting.MainThread());
            await waiting.WaitingForManager.Task.WaitAsync(TimeSpan.FromSeconds(5));
            runs.Add(unrelated.MainThread());
            await unrelated.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, concurrency.Active);

            runs.Add(pendingBackground.MainThread());
            runs.Add(foreground.MainThread());
            if (promoteWithRunNext)
                foreground.RunNext();

            // The manager waiter releases its global slot, so the second occupied slot is
            // beyond the first two physical queue entries. Priority must not overtake it.
            AbstractOperation[] expected = [first, waiting, unrelated, foreground, pendingBackground];
            Assert.Equal(expected, AbstractOperation.OperationQueue);
            Assert.False(foreground.Entered.Task.IsCompleted);
            Assert.Equal(2, concurrency.Maximum);

            unrelated.Finish.TrySetResult();
            await runs[2].WaitAsync(TimeSpan.FromSeconds(5));
            await foreground.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(pendingBackground.Entered.Task.IsCompleted);
            Assert.Equal(2, concurrency.Active);
            Assert.Equal(2, concurrency.Maximum);

            foreground.Finish.TrySetResult();
            await runs[4].WaitAsync(TimeSpan.FromSeconds(5));
            await pendingBackground.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            pendingBackground.Finish.TrySetResult();
            await runs[3].WaitAsync(TimeSpan.FromSeconds(5));
            first.Finish.TrySetResult();
            await waiting.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            waiting.Finish.TrySetResult();
            await Task.WhenAll(runs).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.All(operations, operation => Assert.Equal(OperationStatus.Succeeded, operation.Status));
            Assert.Equal(2, concurrency.Maximum);
            Assert.Empty(AbstractOperation.OperationQueue);
        }
        finally
        {
            foreach (var operation in operations)
            {
                operation.Cancel();
                operation.Finish.TrySetResult();
            }
            try
            {
                await Task.WhenAll(runs).WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                AbstractOperation.MAX_OPERATIONS = previousMax;
                AbstractOperation.OperationQueue.Clear();
            }
        }
    }

    private sealed class OperationConcurrency
    {
        private readonly object _lock = new();
        public int Active { get; private set; }
        public int Maximum { get; private set; }

        public void Enter()
        {
            lock (_lock)
            {
                Active++;
                Maximum = Math.Max(Maximum, Active);
            }
        }

        public void Leave()
        {
            lock (_lock)
                Active--;
        }
    }

    private sealed class GateControlledOperation : AbstractOperation
    {
        private readonly SemaphoreSlim _managerGate;
        private readonly OperationConcurrency _concurrency;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource WaitingForManager { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public GateControlledOperation(SemaphoreSlim managerGate, OperationConcurrency concurrency, bool background)
            : base(queue_enabled: true)
        {
            _managerGate = managerGate;
            _concurrency = concurrency;
            IsBackgroundOperation = background;
            Metadata.Status = "Gate-controlled operation";
            Metadata.Title = "Gate-controlled operation";
            Metadata.OperationInformation = "Gate-controlled operation";
            Metadata.SuccessTitle = "Completed";
            Metadata.SuccessMessage = "Completed";
            Metadata.FailureTitle = "Failed";
            Metadata.FailureMessage = "Failed";
        }

        protected override void ApplyRetryAction(string retryMode) { }

        protected override async Task<OperationVeredict> PerformOperation()
        {
            if (!await _managerGate.WaitAsync(0))
            {
                IsWaitingForManager = true;
                WaitingForManager.TrySetResult();
                try
                {
                    await _managerGate.WaitAsync(CancellationToken);
                }
                catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
                {
                    return OperationVeredict.Canceled;
                }
                finally
                {
                    IsWaitingForManager = false;
                }
            }

            try
            {
                _concurrency.Enter();
                Entered.TrySetResult();
                await Finish.Task.WaitAsync(CancellationToken);
                return OperationVeredict.Success;
            }
            catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
            {
                return OperationVeredict.Canceled;
            }
            finally
            {
                _concurrency.Leave();
                _managerGate.Release();
            }
        }

        public override Task<Uri> GetOperationIcon() => Task.FromResult(new Uri("about:blank"));
    }
}
