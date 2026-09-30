using Xunit;
using FileFlow.Core.Models;
using FileFlow.Infrastructure.Repositories;

namespace FileFlow.Infrastructure.Tests;

public class HistoryRepositoryTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly HistoryRepository _repository;

    public HistoryRepositoryTests()
    {
        _repository = new HistoryRepository(_db.Factory);
    }

    private static OperationRecord Record(string type = "Rename", int failures = 0) => new()
    {
        OperationType = type,
        Description = $"A {type} operation",
        ItemCount = 10,
        SuccessCount = 10 - failures,
        FailureCount = failures,
        CreatedAtUtc = DateTime.UtcNow,
    };

    [Fact]
    public async Task AddAndGetRecent()
    {
        await _repository.AddAsync(Record("Rename"));
        await _repository.AddAsync(Record("Move"));

        var (items, total) = await _repository.GetRecentAsync(10, 0);

        Assert.Equal(2, total);
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.True(i.Id > 0));
    }

    [Fact]
    public async Task CountFailedOperations_CountsOnlyEntriesWithFailures()
    {
        await _repository.AddAsync(Record("Rename", failures: 2));
        await _repository.AddAsync(Record("Move", failures: 0));
        await _repository.AddAsync(Record("Copy", failures: 1));

        Assert.Equal(2, await _repository.CountFailedOperationsAsync());
    }

    [Fact]
    public async Task GetByType_FiltersCorrectly()
    {
        await _repository.AddAsync(Record("Scan"));
        await _repository.AddAsync(Record("Scan"));
        await _repository.AddAsync(Record("Rename"));

        var scans = await _repository.GetByTypeAsync("Scan", 10);

        Assert.Equal(2, scans.Count);
    }

    [Fact]
    public async Task GetRecent_Paging()
    {
        for (var i = 0; i < 15; i++)
        {
            await _repository.AddAsync(Record());
        }

        var (page, total) = await _repository.GetRecentAsync(10, 0);
        Assert.Equal(15, total);
        Assert.Equal(10, page.Count);

        var (secondPage, _) = await _repository.GetRecentAsync(10, 10);
        Assert.Equal(5, secondPage.Count);
    }

    public void Dispose() => _db.Dispose();
}

public class SettingsRepositoryTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly SettingsRepository _repository;

    public SettingsRepositoryTests()
    {
        _repository = new SettingsRepository(_db.Factory);
    }

    [Fact]
    public async Task Get_ReturnsNullWhenMissing()
    {
        Assert.Null(await _repository.GetAsync("nope"));
    }

    [Fact]
    public async Task SetThenGet_RoundTrips()
    {
        await _repository.SetAsync("key1", "value1");
        Assert.Equal("value1", await _repository.GetAsync("key1"));
    }

    [Fact]
    public async Task Set_OverwritesExistingValue()
    {
        await _repository.SetAsync("key1", "old");
        await _repository.SetAsync("key1", "new");

        Assert.Equal("new", await _repository.GetAsync("key1"));
    }

    public void Dispose() => _db.Dispose();
}
