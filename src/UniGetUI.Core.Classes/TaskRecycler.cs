using System.Collections.Concurrent;

namespace UniGetUI.Core.Classes;

/*
 * This static class can help reduce the CPU
 * impact of calling a CPU-intensive method
 * that is expected to return the same result when
 * called twice concurrently.
 *
 * This can apply to getting the locally installed
 * packages, for example.
 *
 *
 * WARNING: When using TaskRecycler with methods that return instances of classes
 * the return instance WILL BE THE SAME when the call attaches to an existing call.
 * Take this into account when handling received objects.
 */
public static class TaskRecycler<ReturnT>
{
    private static readonly ConcurrentDictionary<int, Task<ReturnT>> _tasks = new();
    private static readonly ConcurrentDictionary<int, Task> _tasks_VOID = new();

    public static Task RunOrAttachAsync_VOID(Action method, int cacheTimeSecs = 0)
    {
        int hash = method.GetHashCode();
        return _runTaskAndWait_VOID(new Task(method), hash, cacheTimeSecs);
    }

    public static Task<ReturnT> RunOrAttachAsync(Func<ReturnT> method, int cacheTimeSecs = 0)
    {
        int hash = method.GetHashCode();
        return _runTaskAndWait(new Task<ReturnT>(method), hash, cacheTimeSecs);
    }

    public static Task<ReturnT> RunOrAttachAsync<ParamT>(
        Func<ParamT, ReturnT> method,
        ParamT arg1,
        int cacheTimeSecs = 0
    )
    {
        int hash = method.GetHashCode() + (arg1?.GetHashCode() ?? 0);
        return _runTaskAndWait(new Task<ReturnT>(() => method(arg1)), hash, cacheTimeSecs);
    }

    public static Task<ReturnT> RunOrAttachAsync<Param1T, Param2T>(
        Func<Param1T, Param2T, ReturnT> method,
        Param1T arg1,
        Param2T arg2,
        int cacheTimeSecs = 0
    )
    {
        int hash = method.GetHashCode() + (arg1?.GetHashCode() ?? 0) + (arg2?.GetHashCode() ?? 0);
        return _runTaskAndWait(new Task<ReturnT>(() => method(arg1, arg2)), hash, cacheTimeSecs);
    }

    public static Task<ReturnT> RunOrAttachAsync<Param1T, Param2T, Param3T>(
        Func<Param1T, Param2T, Param3T, ReturnT> method,
        Param1T arg1,
        Param2T arg2,
        Param3T arg3,
        int cacheTimeSecs = 0
    )
    {
        int hash =
            method.GetHashCode()
            + (arg1?.GetHashCode() ?? 0)
            + (arg2?.GetHashCode() ?? 0)
            + (arg3?.GetHashCode() ?? 0);
        return _runTaskAndWait(
            new Task<ReturnT>(() => method(arg1, arg2, arg3)),
            hash,
            cacheTimeSecs
        );
    }

    public static ReturnT RunOrAttach(Func<ReturnT> method, int cacheTimeSecs = 0) =>
        RunOrAttachAsync(method, cacheTimeSecs).GetAwaiter().GetResult();

    public static ReturnT RunOrAttach<ParamT>(
        Func<ParamT, ReturnT> method,
        ParamT arg1,
        int cacheTimeSecs = 0
    ) => RunOrAttachAsync(method, arg1, cacheTimeSecs).GetAwaiter().GetResult();

    public static ReturnT RunOrAttach<Param1T, Param2T>(
        Func<Param1T, Param2T, ReturnT> method,
        Param1T arg1,
        Param2T arg2,
        int cacheTimeSecs = 0
    ) => RunOrAttachAsync(method, arg1, arg2, cacheTimeSecs).GetAwaiter().GetResult();

    public static ReturnT RunOrAttach<Param1T, Param2T, Param3T>(
        Func<Param1T, Param2T, Param3T, ReturnT> method,
        Param1T arg1,
        Param2T arg2,
        Param3T arg3,
        int cacheTimeSecs = 0
    ) => RunOrAttachAsync(method, arg1, arg2, arg3, cacheTimeSecs).GetAwaiter().GetResult();

    /// <summary>
    /// Instantly removes a function call from the cache, even if the associated task has not
    /// finished yet. Any previous call will finish as expected. New calls will not attach to any
    /// preexisting Tasks, and a new Task will be created instead.
    /// </summary>
    public static void RemoveFromCache(Func<ReturnT> method) =>
        _tasks.TryRemove(method.GetHashCode(), out _);

    private static Task _runTaskAndWait_VOID(Task task, int hash, int cacheTimeSecs)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task cachedTask = _tasks_VOID.GetOrAdd(hash, completion.Task);
        if (ReferenceEquals(cachedTask, completion.Task))
        {
            _ = _completeAndScheduleCacheRemoval_VOID(hash, task, completion, cacheTimeSecs);
            task.Start();
        }
        else
        {
            Interlocked.Increment(ref TaskRecyclerTelemetry.DeduplicatedCalls);
        }

        return cachedTask;
    }

    private static Task<ReturnT> _runTaskAndWait(Task<ReturnT> task, int hash, int cacheTimeSecs)
    {
        TaskCompletionSource<ReturnT> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<ReturnT> cachedTask = _tasks.GetOrAdd(hash, completion.Task);
        if (ReferenceEquals(cachedTask, completion.Task))
        {
            _ = _completeAndScheduleCacheRemoval(hash, task, completion, cacheTimeSecs);
            task.Start();
        }
        else
        {
            Interlocked.Increment(ref TaskRecyclerTelemetry.DeduplicatedCalls);
        }

        return cachedTask;
    }

    private static Task _completeAndScheduleCacheRemoval(
        int hash,
        Task<ReturnT> task,
        TaskCompletionSource<ReturnT> completion,
        int cacheTimeSecs
    ) =>
        task.ContinueWith(
            completedTask =>
            {
                // Evict before publishing completion so an immediate retry cannot attach to stale work.
                if (completedTask.IsCompletedSuccessfully && cacheTimeSecs > 0)
                    _ = _removeFromCache(hash, completion.Task, cacheTimeSecs);
                else
                    ((ICollection<KeyValuePair<int, Task<ReturnT>>>)_tasks).Remove(new(hash, completion.Task));

                if (completedTask.IsCompletedSuccessfully)
                    completion.SetResult(completedTask.Result);
                else if (completedTask.IsCanceled)
                    completion.SetCanceled();
                else
                    completion.SetException(completedTask.Exception!.InnerExceptions);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );

    private static Task _completeAndScheduleCacheRemoval_VOID(
        int hash,
        Task task,
        TaskCompletionSource completion,
        int cacheTimeSecs
    ) =>
        task.ContinueWith(
            completedTask =>
            {
                if (completedTask.IsCompletedSuccessfully && cacheTimeSecs > 0)
                    _ = _removeFromCache_VOID(hash, completion.Task, cacheTimeSecs);
                else
                    ((ICollection<KeyValuePair<int, Task>>)_tasks_VOID).Remove(new(hash, completion.Task));

                if (completedTask.IsCompletedSuccessfully)
                    completion.SetResult();
                else if (completedTask.IsCanceled)
                    completion.SetCanceled();
                else
                    completion.SetException(completedTask.Exception!.InnerExceptions);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );

    private static async Task _removeFromCache(int hash, Task<ReturnT> task, int cacheTimeSecs)
    {
        await Task.Delay(TimeSpan.FromSeconds(cacheTimeSecs)).ConfigureAwait(false);

        ((ICollection<KeyValuePair<int, Task<ReturnT>>>)_tasks).Remove(new(hash, task));
    }

    private static async Task _removeFromCache_VOID(int hash, Task task, int cacheTimeSecs)
    {
        await Task.Delay(TimeSpan.FromSeconds(cacheTimeSecs)).ConfigureAwait(false);

        ((ICollection<KeyValuePair<int, Task>>)_tasks_VOID).Remove(new(hash, task));
    }

}

public static class TaskRecyclerTelemetry
{
    public static int DeduplicatedCalls;
}
