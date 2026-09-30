using Xunit;
using FileFlow.Core.Planning;

namespace FileFlow.Core.Tests;

public class RenamePlannerTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly RenamePlanner _planner;

    public RenamePlannerTests()
    {
        _planner = new RenamePlanner(_fs);
        _fs.AddDirectory(@"C:\data");
    }

    private static RenameInput Input(string path) => new(1, path);

    [Fact]
    public void Plan_PrefixIsPrepended()
    {
        _fs.AddFile(@"C:\data\invoice.pdf");
        var plan = _planner.Plan([Input(@"C:\data\invoice.pdf")], new RenameRules { Prefix = "2026_" });

        var entry = Assert.Single(plan.Entries);
        Assert.True(entry.IsValid);
        Assert.Equal("2026_invoice.pdf", entry.NewName);
        Assert.Equal(@"C:\data\2026_invoice.pdf", entry.TargetPath);
    }

    [Fact]
    public void Plan_SuffixIsAppendedBeforeExtension()
    {
        _fs.AddFile(@"C:\data\invoice.pdf");
        var plan = _planner.Plan([Input(@"C:\data\invoice.pdf")], new RenameRules { Suffix = "_draft" });

        Assert.Equal("invoice_draft.pdf", Assert.Single(plan.Entries).NewName);
    }

    [Fact]
    public void Plan_ReplaceIsCaseInsensitive()
    {
        _fs.AddFile(@"C:\data\OldReport.pdf");
        var plan = _planner.Plan([Input(@"C:\data\OldReport.pdf")], new RenameRules { FindText = "old", ReplaceText = "new" });

        Assert.Equal("newReport.pdf", Assert.Single(plan.Entries).NewName);
    }

    [Fact]
    public void Plan_ReplaceWithoutMatchKeepsName()
    {
        _fs.AddFile(@"C:\data\invoice.pdf");
        var plan = _planner.Plan([Input(@"C:\data\invoice.pdf")], new RenameRules { FindText = "nomatch", ReplaceText = "x" });

        Assert.Equal("invoice.pdf", Assert.Single(plan.Entries).NewName);
    }

    [Fact]
    public void Plan_SequentialNumbersAreZeroPadded()
    {
        _fs.AddFile(@"C:\data\a.pdf");
        _fs.AddFile(@"C:\data\b.pdf");
        _fs.AddFile(@"C:\data\c.pdf");

        var plan = _planner.Plan(
            [Input(@"C:\data\a.pdf"), Input(@"C:\data\b.pdf"), Input(@"C:\data\c.pdf")],
            new RenameRules { UseSequentialNumbers = true });

        Assert.Equal(["a_001.pdf", "b_002.pdf", "c_003.pdf"], plan.Entries.Select(e => e.NewName).ToArray());
    }

    [Fact]
    public void Plan_SequentialHonorsStartAndPadding()
    {
        _fs.AddFile(@"C:\data\a.pdf");
        var plan = _planner.Plan(
            [Input(@"C:\data\a.pdf")],
            new RenameRules { UseSequentialNumbers = true, SequentialStart = 42, SequentialPadding = 4 });

        Assert.Equal("a_0042.pdf", Assert.Single(plan.Entries).NewName);
    }

    [Fact]
    public void Plan_CombinedRulesApplyInOrder()
    {
        _fs.AddFile(@"C:\data\invoice.pdf");
        var plan = _planner.Plan(
            [Input(@"C:\data\invoice.pdf")],
            new RenameRules { Prefix = "2026_", FindText = "invoice", ReplaceText = "receipt", UseSequentialNumbers = true });

        Assert.Equal("2026_receipt_001.pdf", Assert.Single(plan.Entries).NewName);
    }

    [Fact]
    public void Plan_DetectsDuplicateTargetsInsideBatch()
    {
        // Same file name differing only in case -> both rules produce the same target path.
        _fs.AddFile(@"C:\data\a.txt");
        _fs.AddFile(@"C:\data\A.TXT");

        var plan = _planner.Plan(
            [Input(@"C:\data\a.txt"), Input(@"C:\data\A.TXT")],
            new RenameRules { Prefix = "y_" });

        Assert.True(plan.Entries[0].IsValid);
        Assert.False(plan.Entries[1].IsValid);
        Assert.Equal(ErrorKind.OperationConflict, plan.Entries[1].ErrorKind);
        Assert.Contains("same target", plan.Entries[1].Error);
    }

    [Fact]
    public void Plan_DetectsExistingTargetOnDisk()
    {
        _fs.AddFile(@"C:\data\a.txt");
        _fs.AddFile(@"C:\data\b.txt");

        var plan = _planner.Plan(
            [Input(@"C:\data\a.txt")],
            new RenameRules { FindText = "a", ReplaceText = "b" });

        Assert.False(plan.Entries[0].IsValid);
        Assert.Equal(ErrorKind.OperationConflict, plan.Entries[0].ErrorKind);
        Assert.Contains("already exists", plan.Entries[0].Error);
    }

    [Fact]
    public void Plan_CaseOnlyRenameIsAllowed()
    {
        _fs.AddFile(@"C:\data\Report.txt");

        var plan = _planner.Plan(
            [Input(@"C:\data\Report.txt")],
            new RenameRules { FindText = "report", ReplaceText = "REPORT" });

        var entry = Assert.Single(plan.Entries);
        Assert.True(entry.IsValid);
        Assert.Equal("REPORT.txt", entry.NewName);
    }

    [Fact]
    public void Plan_RejectsInvalidResultingName()
    {
        _fs.AddFile(@"C:\data\a.txt");
        var plan = _planner.Plan(
            [Input(@"C:\data\a.txt")],
            new RenameRules { Prefix = "bad?name_" });

        Assert.False(plan.Entries[0].IsValid);
        Assert.Equal(ErrorKind.PathValidation, plan.Entries[0].ErrorKind);
    }

    [Fact]
    public void Plan_RejectsInvalidRuleCharacters()
    {
        _fs.AddFile(@"C:\data\a.txt");
        var plan = _planner.Plan(
            [Input(@"C:\data\a.txt")],
            new RenameRules { Suffix = "<evil>" });

        Assert.False(plan.Entries[0].IsValid);
        Assert.Equal(ErrorKind.PathValidation, plan.Entries[0].ErrorKind);
    }

    [Fact]
    public void Plan_NoOpRulesProduceValidEntries()
    {
        _fs.AddFile(@"C:\data\a.txt");
        var plan = _planner.Plan([Input(@"C:\data\a.txt")], new RenameRules());

        var entry = Assert.Single(plan.Entries);
        Assert.True(entry.IsValid);
        Assert.Equal(@"C:\data\a.txt", entry.TargetPath);
    }

    [Fact]
    public void PlanExtensionChange_ChangesExtension()
    {
        _fs.AddFile(@"C:\data\photo.jpeg");
        var plan = _planner.PlanExtensionChange([Input(@"C:\data\photo.jpeg")], ".jpg");

        Assert.Equal("photo.jpg", Assert.Single(plan.Entries).NewName);
    }

    [Fact]
    public void PlanExtensionChange_EmptyRemovesExtension()
    {
        _fs.AddFile(@"C:\data\archive.zip");
        var plan = _planner.PlanExtensionChange([Input(@"C:\data\archive.zip")], "");

        Assert.Equal("archive", Assert.Single(plan.Entries).NewName);
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData(".tar.gz")]
    [InlineData(".")]
    [InlineData(".p?d")]
    public void PlanExtensionChange_RejectsInvalidExtensions(string extension)
    {
        _fs.AddFile(@"C:\data\a.txt");
        var plan = _planner.PlanExtensionChange([Input(@"C:\data\a.txt")], extension);

        Assert.False(plan.Entries[0].IsValid);
        Assert.Equal(ErrorKind.PathValidation, plan.Entries[0].ErrorKind);
    }

    [Fact]
    public void Plan_TreatsLeadingDotFileAsBaseNameWithoutExtension()
    {
        _fs.AddFile(@"C:\data\.gitignore");
        var plan = _planner.Plan([Input(@"C:\data\.gitignore")], new RenameRules { Prefix = "new_" });

        Assert.Equal("new_.gitignore", Assert.Single(plan.Entries).NewName);
    }
}
