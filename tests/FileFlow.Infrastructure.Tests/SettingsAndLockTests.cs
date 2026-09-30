using Xunit;
using FileFlow.Core;
using FileFlow.Infrastructure.Repositories;
using FileFlow.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileFlow.Infrastructure.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        _service = new SettingsService(
            new SettingsRepository(_db.Factory),
            NullLogger<SettingsService>.Instance);
    }

    [Fact]
    public async Task Load_WithoutStoredSettings_UsesDefaults()
    {
        await _service.LoadAsync();

        Assert.Equal(4, _service.Current.MaxConcurrentOperations);
        Assert.True(_service.Current.ScanRecursive);
        Assert.Equal("Information", _service.Current.LogLevel);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTrips()
    {
        var settings = FileFlow.Core.Models.AppSettings.Defaults();
        settings.MaxConcurrentOperations = 8;
        settings.LogLevel = "Debug";
        settings.ExportDirectory = _db.Directory.Path;
        await _service.SaveAsync(settings);

        var fresh = new SettingsService(new SettingsRepository(_db.Factory), NullLogger<SettingsService>.Instance);
        await fresh.LoadAsync();

        Assert.Equal(8, fresh.Current.MaxConcurrentOperations);
        Assert.Equal("Debug", fresh.Current.LogLevel);
        Assert.Equal(_db.Directory.Path, fresh.Current.ExportDirectory);
    }

    [Fact]
    public async Task Save_ClampsConcurrencyToValidRange()
    {
        var settings = FileFlow.Core.Models.AppSettings.Defaults();
        settings.MaxConcurrentOperations = 99;
        await _service.SaveAsync(settings);

        Assert.Equal(16, _service.Current.MaxConcurrentOperations);
    }

    [Fact]
    public async Task Save_NormalizesLogLevelCasing()
    {
        var settings = FileFlow.Core.Models.AppSettings.Defaults();
        settings.LogLevel = "warning";
        await _service.SaveAsync(settings);

        Assert.Equal("Warning", _service.Current.LogLevel);
    }

    [Fact]
    public async Task Save_InvalidExportDirectoryThrows()
    {
        var settings = FileFlow.Core.Models.AppSettings.Defaults();
        settings.ExportDirectory = "not-absolute";

        await Assert.ThrowsAsync<FileFlowException>(() => _service.SaveAsync(settings));
    }

    [Fact]
    public async Task Saved_EventIsRaised()
    {
        var raised = false;
        _service.Saved += (_, _) => raised = true;

        await _service.SaveAsync(FileFlow.Core.Models.AppSettings.Defaults());

        Assert.True(raised);
    }

    public void Dispose() => _db.Dispose();
}

public class KeyedLockRegistryTests
{
    [Fact]
    public void Acquire_SerializesAccessToSameKey()
    {
        var locks = new KeyedLockRegistry();
        var counter = 0;
        const int iterations = 2000;

        Parallel.For(0, 4, _ =>
        {
            for (var i = 0; i < iterations; i++)
            {
                using (locks.Acquire("same-key"))
                {
                    counter++;
                    counter--; // would tear without mutual exclusion
                    counter++;
                }
            }
        });

        Assert.Equal(4 * iterations, counter);
    }

    [Fact]
    public void Acquire_DifferentKeysDoNotBlock()
    {
        var locks = new KeyedLockRegistry();
        using (locks.Acquire("key-a"))
        {
            using (locks.Acquire("key-b"))
            {
                Assert.True(true); // no deadlock
            }
        }
    }

    [Fact]
    public async Task AcquireAll_IsReentrantAcrossDifferentThreadsAndOrdered()
    {
        var locks = new KeyedLockRegistry();
        var barrier = new System.Threading.Barrier(2);

        // Thread 1 acquires (a, b); thread 2 acquires (b, a). Sorted ordering prevents deadlock.
        var t1 = Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(5));
            using var scope = locks.AcquireAll(@"C:\a", @"C:\b");
            await Task.Delay(50);
        });
        var t2 = Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(5));
            using var scope = locks.AcquireAll(@"C:\b", @"C:\a");
            await Task.Delay(50);
        });

        await Task.WhenAll(t1, t2).WaitAsync(TimeSpan.FromSeconds(15));
    }
}
