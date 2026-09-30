using Xunit;
using FileFlow.Core;
using FileFlow.Core.Models;
using FileFlow.Infrastructure.Repositories;

namespace FileFlow.Infrastructure.Tests;

public class TagRepositoryTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly TagRepository _repository;

    public TagRepositoryTests()
    {
        _repository = new TagRepository(_db.Factory);
    }

    [Fact]
    public async Task Create_PersistsTag()
    {
        var tag = await _repository.CreateAsync("Invoices", "#2563EB");

        Assert.True(tag.Id > 0);
        Assert.Equal("Invoices", tag.Name);
        Assert.Equal("#2563EB", tag.Color);
        Assert.Single(await _repository.GetAllAsync());
    }

    [Fact]
    public async Task Create_DuplicateNameIsRejected()
    {
        await _repository.CreateAsync("Work");

        await Assert.ThrowsAsync<FileFlowException>(() => _repository.CreateAsync("Work"));
        await Assert.ThrowsAsync<FileFlowException>(() => _repository.CreateAsync("work")); // case-insensitive unique
    }

    [Fact]
    public async Task Create_EmptyNameIsRejected()
    {
        await Assert.ThrowsAsync<FileFlowException>(() => _repository.CreateAsync("   "));
    }

    [Fact]
    public async Task Rename_UpdatesName()
    {
        var tag = await _repository.CreateAsync("Old");
        await _repository.RenameAsync(tag.Id, "New");

        var all = await _repository.GetAllAsync();
        Assert.Equal("New", Assert.Single(all).Name);
    }

    [Fact]
    public async Task Delete_RemovesRelations()
    {
        var files = new FileRepository(_db.Factory);
        await files.UpsertAsync(
        [
            new FileRecord
            {
                FullPath = @"C:\a.txt",
                FileName = "a.txt",
                CreatedAtUtc = DateTime.UtcNow,
                ModifiedAtUtc = DateTime.UtcNow,
                ScannedAtUtc = DateTime.UtcNow,
            },
        ]);
        var file = (await files.GetByPathAsync(@"C:\a.txt"))!;
        var tag = await _repository.CreateAsync("Temp");
        await _repository.AddTagToFilesAsync(tag.Id, [file.Id]);
        Assert.Single(await _repository.GetTagsForFileAsync(file.Id));

        await _repository.DeleteAsync(tag.Id);

        Assert.Empty(await _repository.GetAllAsync());
        Assert.Empty(await _repository.GetTagsForFileAsync(file.Id));
    }

    [Fact]
    public async Task AddAndRemoveTag_IsIdempotent()
    {
        var files = new FileRepository(_db.Factory);
        await files.UpsertAsync(
        [
            new FileRecord
            {
                FullPath = @"C:\a.txt",
                FileName = "a.txt",
                CreatedAtUtc = DateTime.UtcNow,
                ModifiedAtUtc = DateTime.UtcNow,
                ScannedAtUtc = DateTime.UtcNow,
            },
        ]);
        var file = (await files.GetByPathAsync(@"C:\a.txt"))!;
        var tag = await _repository.CreateAsync("Docs");

        await _repository.AddTagToFilesAsync(tag.Id, [file.Id]);
        await _repository.AddTagToFilesAsync(tag.Id, [file.Id]); // duplicate link ignored

        var links = await _repository.GetTagsForFileAsync(file.Id);
        Assert.Single(links);

        await _repository.RemoveTagFromFilesAsync(tag.Id, [file.Id]);
        Assert.Empty(await _repository.GetTagsForFileAsync(file.Id));
    }

    [Fact]
    public async Task GetAll_ReturnsFileCounts()
    {
        var files = new FileRepository(_db.Factory);
        var records = new List<FileFlow.Core.Models.FileRecord>();
        for (var i = 0; i < 3; i++)
        {
            var path = $@"C:\f{i}.txt";
            records.Add(new FileFlow.Core.Models.FileRecord
            {
                FullPath = path,
                FileName = $"f{i}.txt",
                CreatedAtUtc = DateTime.UtcNow,
                ModifiedAtUtc = DateTime.UtcNow,
                ScannedAtUtc = DateTime.UtcNow,
            });
        }

        await files.UpsertAsync(records);
        var tag = await _repository.CreateAsync("Many");
        await _repository.AddTagToFilesAsync(tag.Id, records.Take(2).Select(r => r.Id = files.GetByPathAsync(r.FullPath).Result!.Id).ToList());

        var all = await _repository.GetAllAsync();
        Assert.Equal(2, Assert.Single(all).FileCount);
    }

    [Fact]
    public async Task GetByName_IsCaseInsensitive()
    {
        await _repository.CreateAsync("Reports");

        Assert.NotNull(await _repository.GetByNameAsync("reports"));
        Assert.Null(await _repository.GetByNameAsync("Missing"));
    }

    public void Dispose() => _db.Dispose();
}
