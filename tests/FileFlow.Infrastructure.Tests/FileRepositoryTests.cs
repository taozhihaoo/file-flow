using Xunit;
using FileFlow.Core.Models;
using FileFlow.Infrastructure.Repositories;

namespace FileFlow.Infrastructure.Tests;

public class FileRepositoryTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly FileRepository _repository;

    public FileRepositoryTests()
    {
        _repository = new FileRepository(_db.Factory);
    }

    private static FileRecord Record(string path, long size = 100, string modified = "2026-03-01T10:00:00.0000000Z")
    {
        var modifiedUtc = DateTimeOffset.Parse(modified).UtcDateTime;
        return new FileRecord
        {
            FullPath = path,
            FileName = System.IO.Path.GetFileName(path),
            Extension = System.IO.Path.GetExtension(path).ToLowerInvariant(),
            SizeBytes = size,
            CreatedAtUtc = modifiedUtc,
            ModifiedAtUtc = modifiedUtc,
            ScannedAtUtc = DateTime.UtcNow,
        };
    }

    [Fact]
    public async Task Upsert_InsertsNewRecords()
    {
        await _repository.UpsertAsync([Record(@"C:\data\a.txt")]);

        var stored = await _repository.GetByPathAsync(@"C:\data\a.txt");
        Assert.NotNull(stored);
        Assert.Equal("a.txt", stored.FileName);
        Assert.Equal(".txt", stored.Extension);
        Assert.Equal(100, stored.SizeBytes);
        Assert.False(stored.IsMissing);
    }

    [Fact]
    public async Task Upsert_IsIdempotentAndUpdates()
    {
        await _repository.UpsertAsync([Record(@"C:\data\a.txt", size: 100)]);
        await _repository.UpsertAsync([Record(@"C:\data\a.txt", size: 250)]);

        var count = await _repository.CountAllAsync();
        var stored = await _repository.GetByPathAsync(@"C:\data\a.txt");
        Assert.Equal(1, count);
        Assert.Equal(250, stored!.SizeBytes);
    }

    [Fact]
    public async Task Search_ByKeywordMatchesNameAndPath()
    {
        await _repository.UpsertAsync(
        [
            Record(@"C:\docs\invoice_2026.pdf"),
            Record(@"C:\invoices\summary.xlsx"),
            Record(@"C:\photos\cat.jpg"),
        ]);

        var (byName, _) = await _repository.SearchAsync(new SearchFilter { Keyword = "invoice" });
        Assert.Equal(2, byName.Count); // matches invoice_2026.pdf and the invoices folder path

        var (byExtensionKeyword, _) = await _repository.SearchAsync(new SearchFilter { Keyword = ".pdf" });
        Assert.Single(byExtensionKeyword);
    }

    [Fact]
    public async Task Search_ByExtensionIsCaseInsensitive()
    {
        await _repository.UpsertAsync([Record(@"C:\a.PDF"), Record(@"C:\b.txt")]);

        var (items, total) = await _repository.SearchAsync(new SearchFilter { Extension = ".pdf" });
        Assert.Equal(1, total);
        Assert.Equal(@"C:\a.PDF", items[0].File.FullPath);
    }

    [Fact]
    public async Task Search_BySizeRange()
    {
        await _repository.UpsertAsync(
        [
            Record(@"C:\small.txt", size: 10),
            Record(@"C:\medium.txt", size: 1000),
            Record(@"C:\large.txt", size: 100000),
        ]);

        var (items, total) = await _repository.SearchAsync(new SearchFilter { MinSizeBytes = 100, MaxSizeBytes = 5000 });
        Assert.Equal(1, total);
        Assert.Equal("medium.txt", items[0].File.FileName);
    }

    [Fact]
    public async Task Search_ByModifiedDateRange()
    {
        await _repository.UpsertAsync(
        [
            Record(@"C:\old.txt", modified: "2026-01-01T00:00:00.0000000Z"),
            Record(@"C:\new.txt", modified: "2026-06-01T00:00:00.0000000Z"),
        ]);

        var from = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var (items, total) = await _repository.SearchAsync(new SearchFilter { ModifiedFromUtc = from });

        Assert.Equal(1, total);
        Assert.Equal("new.txt", items[0].File.FileName);
    }

    [Fact]
    public async Task Search_ByTagRequiresAllTags()
    {
        await _repository.UpsertAsync([Record(@"C:\a.txt"), Record(@"C:\b.txt")]);
        var files = new FileRepository(_db.Factory);
        var a = await files.GetByPathAsync(@"C:\a.txt");
        var b = await files.GetByPathAsync(@"C:\b.txt");
        var tags = new TagRepository(_db.Factory);
        var work = await tags.CreateAsync("Work");
        var important = await tags.CreateAsync("Important");
        await tags.AddTagToFilesAsync(work.Id, [a!.Id, b!.Id]);
        await tags.AddTagToFilesAsync(important.Id, [a!.Id]);

        var (both, bothTotal) = await files.SearchAsync(new SearchFilter { TagIds = [work.Id, important.Id] });
        Assert.Equal(1, bothTotal);
        Assert.Equal("a.txt", both[0].File.FileName);

        var (one, oneTotal) = await files.SearchAsync(new SearchFilter { TagIds = [work.Id] });
        Assert.Equal(2, oneTotal);
        Assert.Contains("Work", one[0].Tags);
    }

    [Fact]
    public async Task Search_MissingFilter()
    {
        await _repository.UpsertAsync([Record(@"C:\a.txt"), Record(@"C:\b.txt")]);
        var a = await _repository.GetByPathAsync(@"C:\a.txt");
        await _repository.SetMissingAsync(a!.Id, true);

        var (missing, missingTotal) = await _repository.SearchAsync(new SearchFilter { Missing = true });
        Assert.Equal(1, missingTotal);

        var (present, presentTotal) = await _repository.SearchAsync(new SearchFilter { Missing = false });
        Assert.Equal(1, presentTotal);
        Assert.Equal("b.txt", present[0].File.FileName);
    }

    [Fact]
    public async Task Search_RootPathScopesResults()
    {
        await _repository.UpsertAsync([Record(@"C:\docs\a.txt"), Record(@"C:\docs-sub\a.txt"), Record(@"C:\other\b.txt")]);

        var (items, total) = await _repository.SearchAsync(new SearchFilter { RootPath = @"C:\docs" });

        // C:\docs-sub must NOT match the C:\docs prefix (boundary check).
        Assert.Equal(1, total);
        Assert.Equal(@"C:\docs\a.txt", items[0].File.FullPath);
    }

    [Fact]
    public async Task Search_PagingReturnsTotal()
    {
        var records = Enumerable.Range(0, 25).Select(i => Record($@"C:\d\file{i:00}.txt")).ToList();
        await _repository.UpsertAsync(records);

        var (page1, total) = await _repository.SearchAsync(new SearchFilter { Limit = 10, Offset = 0 });
        var (page2, _) = await _repository.SearchAsync(new SearchFilter { Limit = 10, Offset = 10 });
        var (page3, _) = await _repository.SearchAsync(new SearchFilter { Limit = 10, Offset = 20 });

        Assert.Equal(25, total);
        Assert.Equal(10, page1.Count);
        Assert.Equal(10, page2.Count);
        Assert.Equal(5, page3.Count);
        Assert.NotEqual(page1[0].File.Id, page2[0].File.Id);
    }

    [Fact]
    public async Task UpdatePath_ChangesLocation()
    {
        await _repository.UpsertAsync([Record(@"C:\old\name.txt")]);
        var record = (await _repository.GetByPathAsync(@"C:\old\name.txt"))!;

        await _repository.UpdatePathAsync(record.Id, @"C:\new\renamed.md", "renamed.md", ".md");

        Assert.Null(await _repository.GetByPathAsync(@"C:\old\name.txt"));
        var moved = await _repository.GetByPathAsync(@"C:\new\renamed.md");
        Assert.NotNull(moved);
        Assert.Equal(record.Id, moved.Id);
        Assert.Equal(".md", moved.Extension);
    }

    [Fact]
    public async Task MarkMissingExcept_MarksUnseenFilesUnderRoot()
    {
        await _repository.UpsertAsync([Record(@"C:\root\seen.txt"), Record(@"C:\root\gone.txt"), Record(@"C:\elsewhere\unrelated.txt")]);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\root\seen.txt" };

        var affected = await _repository.MarkMissingExceptAsync(@"C:\root", seen);

        Assert.Equal(1, affected);
        Assert.True((await _repository.GetByPathAsync(@"C:\root\gone.txt"))!.IsMissing);
        Assert.False((await _repository.GetByPathAsync(@"C:\root\seen.txt"))!.IsMissing);
        Assert.False((await _repository.GetByPathAsync(@"C:\elsewhere\unrelated.txt"))!.IsMissing);
    }

    [Fact]
    public async Task SumSize_TotalsLibrary()
    {
        await _repository.UpsertAsync([Record(@"C:\a.txt", size: 100), Record(@"C:\b.txt", size: 250)]);

        Assert.Equal(350, await _repository.SumSizeAsync());
        Assert.Equal(2, await _repository.CountAllAsync());
    }

    [Fact]
    public async Task GetExtensions_ReturnsDistinctList()
    {
        await _repository.UpsertAsync([Record(@"C:\a.pdf"), Record(@"C:\b.pdf"), Record(@"C:\c.txt")]);

        Assert.Equal([".pdf", ".txt"], await _repository.GetExtensionsAsync());
    }

    [Fact]
    public async Task GetByIds_ReturnsRequestedRecords()
    {
        await _repository.UpsertAsync([Record(@"C:\a.txt"), Record(@"C:\b.txt")]);
        var a = await _repository.GetByPathAsync(@"C:\a.txt");
        var b = await _repository.GetByPathAsync(@"C:\b.txt");

        var results = await _repository.GetByIdsAsync([a!.Id, b!.Id, 99999]);

        Assert.Equal(2, results.Count);
    }

    public void Dispose() => _db.Dispose();
}
