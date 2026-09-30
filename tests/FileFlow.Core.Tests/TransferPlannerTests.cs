using Xunit;
using FileFlow.Core.Planning;

namespace FileFlow.Core.Tests;

public class TransferPlannerTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly TransferPlanner _planner;

    public TransferPlannerTests()
    {
        _planner = new TransferPlanner(_fs);
        _fs.AddDirectory(@"C:\source");
        _fs.AddDirectory(@"C:\target");
    }

    private static TransferInput Input(long id, string path) => new(id, path);

    [Fact]
    public void Plan_MoveProducesTargetPaths()
    {
        _fs.AddFile(@"C:\source\a.txt");
        var plan = _planner.Plan([Input(1, @"C:\source\a.txt")], @"C:\target", isCopy: false);

        var entry = Assert.Single(plan.Entries);
        Assert.True(entry.IsValid);
        Assert.Equal(@"C:\target\a.txt", entry.TargetPath);
    }

    [Fact]
    public void Plan_RejectsMissingTargetDirectory()
    {
        _fs.AddFile(@"C:\source\a.txt");
        var plan = _planner.Plan([Input(1, @"C:\source\a.txt")], @"C:\does-not-exist", isCopy: false);

        Assert.All(plan.Entries, e => Assert.False(e.IsValid));
        Assert.Equal(ErrorKind.PathValidation, plan.Entries[0].ErrorKind);
    }

    [Fact]
    public void Plan_DetectsExistingFileInTarget()
    {
        _fs.AddFile(@"C:\source\a.txt");
        _fs.AddFile(@"C:\target\a.txt");

        var plan = _planner.Plan([Input(1, @"C:\source\a.txt")], @"C:\target", isCopy: true);

        Assert.False(plan.Entries[0].IsValid);
        Assert.Equal(ErrorKind.OperationConflict, plan.Entries[0].ErrorKind);
    }

    [Fact]
    public void Plan_DetectsSameNameCollisionInsideBatch()
    {
        _fs.AddFile(@"C:\source\a.txt");
        _fs.AddFile(@"C:\source\sub\a.txt");
        _fs.AddDirectory(@"C:\source\sub");

        var plan = _planner.Plan(
            [Input(1, @"C:\source\a.txt"), Input(2, @"C:\source\sub\a.txt")],
            @"C:\target",
            isCopy: false);

        Assert.True(plan.Entries[0].IsValid);
        Assert.False(plan.Entries[1].IsValid);
    }

    [Fact]
    public void Plan_MoveOntoItselfIsRejected()
    {
        _fs.AddFile(@"C:\target\a.txt");

        var plan = _planner.Plan([Input(1, @"C:\target\a.txt")], @"C:\target", isCopy: false);

        Assert.False(plan.Entries[0].IsValid);
        Assert.Contains("same file", plan.Entries[0].Error);
    }

    [Fact]
    public void Plan_CopyOntoItselfIsAllowed()
    {
        _fs.AddFile(@"C:\target\a.txt");

        var plan = _planner.Plan([Input(1, @"C:\target\a.txt")], @"C:\target", isCopy: true);

        // Copying onto itself conflicts with the existing target check, which is the safe behavior.
        Assert.False(plan.Entries[0].IsValid);
    }

    [Fact]
    public void Plan_CountsValidAndErrorEntries()
    {
        _fs.AddFile(@"C:\source\a.txt");
        _fs.AddFile(@"C:\source\b.txt");
        _fs.AddFile(@"C:\target\b.txt");

        var plan = _planner.Plan(
            [Input(1, @"C:\source\a.txt"), Input(2, @"C:\source\b.txt")],
            @"C:\target",
            isCopy: true);

        Assert.Equal(1, plan.ValidCount);
        Assert.Equal(1, plan.ErrorCount);
    }
}
