using MagnaFlow.MfCockpit.Infrastructure;

namespace MagnaFlow.MfCockpit.Tests;

public class GitCommitMessageSuggesterTests
{
    [Fact]
    public void Suggests_the_single_changed_spec_files_name()
    {
        var suggestion = GitCommitMessageSuggester.SuggestDefault(["docs/specs/frontend/page.md", "src/Foo.cs"]);
        Assert.Equal("page.md", suggestion);
    }

    [Fact]
    public void Returns_null_when_no_spec_file_changed()
    {
        Assert.Null(GitCommitMessageSuggester.SuggestDefault(["src/Foo.cs", "README.md"]));
    }

    [Fact]
    public void Returns_null_when_multiple_spec_files_changed()
    {
        var suggestion = GitCommitMessageSuggester.SuggestDefault(["docs/specs/a.md", "docs/specs/b.md"]);
        Assert.Null(suggestion);
    }

    [Fact]
    public void Returns_null_for_an_empty_change_set()
    {
        Assert.Null(GitCommitMessageSuggester.SuggestDefault([]));
    }

    [Fact]
    public void Matches_specs_path_case_insensitively_and_handles_backslashes()
    {
        if (!OperatingSystem.IsWindows()) return; // backslash is a path separator only on Windows
        var suggestion = GitCommitMessageSuggester.SuggestDefault(["Docs\\Specs\\overview.md"]);
        Assert.Equal("overview.md", suggestion);
    }
}
