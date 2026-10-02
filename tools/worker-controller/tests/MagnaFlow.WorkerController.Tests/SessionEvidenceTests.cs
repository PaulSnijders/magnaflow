using MagnaFlow.WorkerController.Execution;

namespace MagnaFlow.WorkerController.Tests;

public class SessionEvidenceTests : IDisposable
{
    private readonly TempProject _project = new();

    [Fact]
    public void Write_ThenReadSessionId_RoundTrips()
    {
        var dir = Path.Combine(_project.Root, ".magnaflow", "0001-test");

        SessionEvidence.Write(dir, "sess-42");

        Assert.Equal("sess-42", SessionEvidence.ReadSessionId(dir));
        Assert.Contains("session: sess-42", File.ReadAllText(SessionEvidence.SessionPath(dir)));
    }

    [Fact]
    public void Write_DoesNothingWhenSessionIdIsNull()
    {
        var dir = Path.Combine(_project.Root, ".magnaflow", "0001-test");

        SessionEvidence.Write(dir, null);

        Assert.False(File.Exists(SessionEvidence.SessionPath(dir)));
    }

    [Fact]
    public void ReadSessionId_IsNullWhenFileAbsent() =>
        Assert.Null(SessionEvidence.ReadSessionId(Path.Combine(_project.Root, ".magnaflow", "0001-never-run")));

    [Fact]
    public void Write_OverwritesPreviousSession()
    {
        var dir = Path.Combine(_project.Root, ".magnaflow", "0001-test");

        SessionEvidence.Write(dir, "sess-old");
        SessionEvidence.Write(dir, "sess-new");

        Assert.Equal("sess-new", SessionEvidence.ReadSessionId(dir));
    }

    public void Dispose() => _project.Dispose();
}
