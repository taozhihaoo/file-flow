using System.Collections.Concurrent;
using FileFlow.Core.Interfaces;

namespace FileFlow.Infrastructure.Services;

/// <summary>
/// Mutual exclusion per path (case-insensitive): prevents rename/move/delete/copy from
/// running against the same file at the same time. Entries are kept for the process
/// lifetime (bounded by the number of paths actually operated on).
/// </summary>
public sealed class KeyedLockRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Blocks until the key is free; dispose the returned scope to release it.</summary>
    public IDisposable Acquire(string key)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        semaphore.Wait();
        return new Releaser(semaphore);
    }

    /// <summary>
    /// Acquires locks for several keys in a deterministic order (avoids deadlocks).
    /// Duplicate keys (e.g. a case-only rename where source equals target) are acquired once;
    /// the underlying semaphores are not reentrant.
    /// </summary>
    public IDisposable AcquireAll(params string[] keys)
    {
        var ordered = keys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var disposables = new List<IDisposable>(ordered.Length);
        try
        {
            foreach (var key in ordered)
            {
                disposables.Add(Acquire(key));
            }
        }
        catch
        {
            foreach (var disposable in disposables)
            {
                disposable.Dispose();
            }

            throw;
        }

        return new Composite(disposables);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }

    private sealed class Composite(IReadOnlyList<IDisposable> disposables) : IDisposable
    {
        public void Dispose()
        {
            foreach (var disposable in disposables)
            {
                disposable.Dispose();
            }
        }
    }
}
