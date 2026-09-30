using Xunit;
using FileFlow.Core.Models;
using FileFlow.Infrastructure.Repositories;
using FileFlow.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileFlow.Infrastructure.Tests;

public class ImportExportServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly TempDirectory _temp = new();
    private readonly ImportExportService _service;
    private readonly FileRepository _files;
    private readonly TagRepository _tags;

    public ImportExportServiceTests()
    {
        _files = new FileRepository(_db.Factory);
        _tags = new TagRepository(_db.Factory);
        _service = new ImportExportService(_files, _tags, new HistoryRepository(_db.Factory), NullLogger<ImportExportService>.Instance);
    }

    private async Task SeedAsync()
    {
        await _files.UpsertAsync(
        [
            new FileRecord
            {
                FullPath = @"C:\docs\invoice.pdf",
                FileName = "invoice.pdf",
                Extension = ".pdf",
                SizeBytes = 2048,
                CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ModifiedAtUtc = new DateTime(2026, 3, 15, 12, 30, 0, DateTimeKind.Utc),
                ScannedAtUtc = DateTime.UtcNow,
            },
            new FileRecord
            {
                FullPath = @"C:\docs\note, with comma.txt",
                FileName = "note, with comma.txt",
                Extension = ".txt",
                SizeBytes = 10,
                CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ModifiedAtUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                ScannedAtUtc = DateTime.UtcNow,
            },
        ]);
        var invoice = (await _files.GetByPathAsync(@"C:\docs\invoice.pdf"))!;
        var tag = await _tags.CreateAsync("Invoices");
        await _tags.AddTagToFilesAsync(tag.Id, [invoice.Id]);
    }

    [Fact]
    public async Task Export_WritesHeadersAndRows()
    {
        await SeedAsync();
        var target = _temp.FilePath("export.csv");

        var result = await _service.ExportAsync(target, null);

        Assert.Equal(2, result.FileCount);
        var content = System.IO.File.ReadAllText(target);
        var lines = System.IO.File.ReadAllLines(target);
        Assert.Equal("file_name,full_path,extension,size_bytes,modified_at,tags", lines[0]);
        Assert.Contains("invoice.pdf", content);
        Assert.Contains("Invoices", content);
        Assert.Contains("note, with comma.txt", content);
    }

    [Fact]
    public async Task Export_QuotesFieldsContainingCommas()
    {
        await SeedAsync();
        var target = _temp.FilePath("export.csv");

        await _service.ExportAsync(target, null);

        Assert.Contains("\"note, with comma.txt\"", System.IO.File.ReadAllText(target));
    }

    [Fact]
    public async Task Export_ThenImport_RoundTrips()
    {
        await SeedAsync();
        var target = _temp.FilePath("roundtrip.csv");
        await _service.ExportAsync(target, null);

        // Import into the same library: everything already exists -> updates, no duplicates.
        var result = await _service.ImportAsync(target);

        Assert.Equal(2, result.RowsRead);
        Assert.Equal(0, result.Imported);
        Assert.Equal(2, result.Updated);
        Assert.Equal(2, await _files.CountAllAsync());
    }

    [Fact]
    public async Task Import_CreatesNewRecordsAndLinksTags()
    {
        var source = _temp.FilePath("in.csv");
        var modified = DateTime.UtcNow.ToString("O");
        await System.IO.File.WriteAllLinesAsync(source,
        [
            "file_name,full_path,extension,size_bytes,modified_at,tags",
            $"new.doc,C:\\new\\new.doc,.doc,42,{modified},Work;Urgent",
        ]);

        var result = await _service.ImportAsync(source);

        Assert.Equal(1, result.Imported);
        var record = await _files.GetByPathAsync(@"C:\new\new.doc");
        Assert.NotNull(record);
        Assert.Equal(42, record!.SizeBytes);
        var tags = await _tags.GetTagsForFileAsync(record.Id);
        Assert.Equal(["Urgent", "Work"], tags.Select(t => t.Name).OrderBy(n => n).ToArray());
    }

    [Fact]
    public async Task Import_SkipsDuplicateRowsInsideTheFile()
    {
        var source = _temp.FilePath("dupes.csv");
        var modified = DateTime.UtcNow.ToString("O");
        await System.IO.File.WriteAllLinesAsync(source,
        [
            "file_name,full_path,extension,size_bytes,modified_at",
            $"a.txt,C:\\a.txt,.txt,1,{modified}",
            $"a.txt,C:\\a.txt,.txt,1,{modified}",
        ]);

        var result = await _service.ImportAsync(source);

        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.SkippedDuplicates);
        Assert.Equal(1, await _files.CountAllAsync());
    }

    [Fact]
    public async Task Import_ReportsInvalidRowsWithRowNumbers()
    {
        var source = _temp.FilePath("invalid.csv");
        var modified = DateTime.UtcNow.ToString("O");
        await System.IO.File.WriteAllLinesAsync(source,
        [
            "file_name,full_path,extension,size_bytes,modified_at",
            $"ok.txt,C:\\ok.txt,.txt,1,{modified}",
            "bad,C:\\bad.txt,.txt,not-a-number," + modified,
            "bad2,C:\\bad2.txt,.txt,5,not-a-date",
            ",,.txt,5," + modified,
        ]);

        var result = await _service.ImportAsync(source);

        Assert.Equal(1, result.Imported);
        Assert.Equal(3, result.Errors.Count);
        Assert.Equal(3, result.Errors[0].Row);
        Assert.Contains("size", result.Errors[0].Reason);
        Assert.Equal(4, result.Errors[1].Row);
        Assert.Contains("date", result.Errors[1].Reason);
        Assert.Equal(5, result.Errors[2].Row);
    }

    [Fact]
    public async Task Import_RejectsWrongHeaders()
    {
        var source = _temp.FilePath("wrong-header.csv");
        await System.IO.File.WriteAllTextAsync(source, "name,path\na,b\n");

        await Assert.ThrowsAsync<FileFlow.Core.FileFlowException>(() => _service.ImportAsync(source));
    }

    [Fact]
    public async Task Import_MissingFileThrowsNotFound()
    {
        var ex = await Assert.ThrowsAsync<FileFlow.Core.FileFlowException>(
            () => _service.ImportAsync(_temp.FilePath("missing.csv")));
        Assert.Equal(FileFlow.Core.ErrorKind.FileNotFound, ex.Kind);
    }

    public void Dispose()
    {
        _db.Dispose();
        _temp.Dispose();
    }
}
