using MagnaFlow.WorkerController.Execution;

namespace MagnaFlow.WorkerController.Tests;

public class ResumeResolverTests : IDisposable
{
    private readonly TempProject _project = new();

    [Fact]
    public void RawSessionId_IsPassedThroughVerbatim()
    {
        var (session, error) = ResumeResolver.Resolve("6a1f0e6e-1234-4bcd-9ef0-abcdef012345", _project.Root);

        Assert.Null(error);
        Assert.Equal("6a1f0e6e-1234-4bcd-9ef0-abcdef012345", session);
    }

    [Fact]
    public void CommandReference_ResolvesToTheRecordedSession()
    {
        _project.WriteSessionEvidence("0001-earlier", "sess-from-evidence");

        var (session, error) = ResumeResolver.Resolve("0001-earlier", _project.Root);

        Assert.Null(error);
        Assert.Equal("sess-from-evidence", session);
    }

    [Fact]
    public void CommandReference_ToUnknownCommand_IsAnError()
    {
        var (session, error) = ResumeResolver.Resolve("0009-does-not-exist", _project.Root);

        Assert.Null(session);
        Assert.Contains("did not resolve", error);
        Assert.Contains("0009-does-not-exist", error);
    }

    [Fact]
    public void CommandReference_WithoutEvidence_IsAnError()
    {
        _project.WriteCmd("0001-nooit-gedraaid", TempProject.CmdMarkdown(branch: null));

        var (session, error) = ResumeResolver.Resolve("0001-nooit-gedraaid", _project.Root);

        Assert.Null(session);
        Assert.Contains("did not resolve", error);
        Assert.Contains("0001-nooit-gedraaid", error);
    }

    [Fact]
    public void CmdFilename_ResolvesToTheCommandsRecordedSession()
    {
        _project.WriteSessionEvidence("0008-funnel", "sess-funnel");

        var (session, error) = ResumeResolver.Resolve("0008-cmd-funnel", _project.Root);

        Assert.Null(error);
        Assert.Equal("sess-funnel", session);
    }

    [Fact]
    public void SiblingFilePath_WithDirectoryAndExtension_ResolvesToTheCommandsSession()
    {
        _project.WriteSessionEvidence("0008B-funnel", "sess-b");

        var (session, error) = ResumeResolver.Resolve("docs/prompts/0008B-rst-funnel.md", _project.Root);

        Assert.Null(error);
        Assert.Equal("sess-b", session);
    }

    [Fact]
    public void SlugWithLaneWordOutsideInfixPosition_IsResolvedUntouched()
    {
        // `qa` sits mid-slug, not directly after the number — a plain string replace would corrupt it.
        _project.WriteSessionEvidence("0012-fix-qa-export", "sess-qa-export");

        var (session, error) = ResumeResolver.Resolve("0012-fix-qa-export", _project.Root);

        Assert.Null(error);
        Assert.Equal("sess-qa-export", session);
    }

    [Fact]
    public void VerbatimMatch_WinsOverTheNormalizedForm()
    {
        _project.WriteSessionEvidence("0008-cmd-funnel", "sess-verbatim");
        _project.WriteSessionEvidence("0008-funnel", "sess-normalized");

        var (session, error) = ResumeResolver.Resolve("0008-cmd-funnel", _project.Root);

        Assert.Null(error);
        Assert.Equal("sess-verbatim", session);
    }

    [Fact]
    public void UnresolvableFilename_ErrorNamesEveryIdTried()
    {
        var (session, error) = ResumeResolver.Resolve("0008-cmd-funnel", _project.Root);

        Assert.Null(session);
        Assert.Contains("0008-cmd-funnel", error);
        Assert.Contains("0008-funnel", error);
    }

    [Fact]
    public void UnresolvableError_ListsTheCommandsThatHaveRun()
    {
        _project.WriteSessionEvidence("0003-earlier", "sess-earlier");

        var (session, error) = ResumeResolver.Resolve("0009-does-not-exist", _project.Root);

        Assert.Null(session);
        Assert.Contains("0003-earlier", error);
    }

    [Fact]
    public void CommandReference_WithoutRecordedSession_IsAnError()
    {
        _project.WriteSessionEvidence("0001-without-session", sessionId: null);

        var (session, error) = ResumeResolver.Resolve("0001-without-session", _project.Root);

        Assert.Null(session);
        Assert.Contains("no recorded session", error);
    }

    public void Dispose() => _project.Dispose();
}
