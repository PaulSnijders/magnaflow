using MagnaFlow.MfCockpit.Infrastructure;
using MagnaFlow.MfCockpit.Prompts;

namespace MagnaFlow.MfCockpit.Models;

/// <summary>The fast half (no git subprocesses — file I/O only): counts/attention/watcher, what
/// matters most for "who needs attention?" at a glance. Git info is a separate, slower request
/// (ProjectGitInfoDto) so a project with an expensive git status never blocks this one.</summary>
public sealed record ProjectSummaryDto(
    string Name,
    string Path,
    bool Exists,
    CommandCounts Counts,
    IReadOnlyList<AttentionItem> Attention,
    bool WatcherAlive,
    LatestCommand? Latest,
    LatestCommand? Running);

/// <summary>GET /api/overview (docs/prompts/0010): the one request every project-scoped page makes
/// for its shared chrome — chat gating, the switcher's project names, and this project's summary,
/// in place of three separate calls. Projects is names only; Project is null when no ?project= was
/// given or the name is unknown, since the nav must still render.</summary>
public sealed record OverviewDto(bool ChatEnabled, IReadOnlyList<string> Projects, ProjectSummaryDto? Project);

/// <summary>The highest-id lane command (ontwerp-v0.5.md item 6): index.html's "Latest" column and
/// the item-2 top bar's "latest command when idle" text both render this, so the two never
/// disagree. Null when the lane is empty. Status is "malformed" for an unparseable cmd file.</summary>
public sealed record LatestCommand(string Id, string Title, string Status);

/// <summary>The slow half: branch/dirty/commits/default-commit-message, all of which cost at
/// least one `git` subprocess spawn and can be seconds-to-minutes on a repo with a large
/// untracked tree. Fetched separately from ProjectSummaryDto so the fast data can render first.</summary>
public sealed record ProjectGitInfoDto(
    bool Available,
    string? Branch,
    bool Dirty,
    IReadOnlyList<GitCommit> Commits,
    string? DefaultCommitMessage,
    int? Ahead = null,
    int? Behind = null);

public sealed record CommandSummaryDto(
    string Id,
    string Title,
    string Status,
    int Attempts,
    int? MaxAttempts,
    string? Branch,
    string? Group,
    bool HasPln,
    bool HasQa,
    bool HasRst,
    bool Malformed,
    string? Error,
    string BaseNumber,
    bool IsFollowUp,
    // The rst's one-line frontmatter summary (docs/decisions/0015-rst-summary-line.md); null when
    // there is no report yet or it predates the convention — the lane then renders nothing.
    string? Summary,
    // First claude.log header to last evidence write (to now while running); null when unknown.
    int? DurationSeconds);

public sealed record LaneFileDto(bool Exists, string? Content);

public sealed record LogTailDto(IReadOnlyList<string> Lines, bool Truncated, bool Exists);

public sealed record CommandDetailDto(
    string Id,
    string Title,
    string Status,
    int Attempts,
    int? MaxAttempts,
    string? Branch,
    string? Base,
    string? Group,
    IReadOnlyList<string> Specs,
    string? Created,
    bool Malformed,
    string? Error,
    LaneFileDto Cmd,
    LaneFileDto Pln,
    LaneFileDto Qa,
    LaneFileDto Rst,
    LogTailDto ClaudeLog,
    LogTailDto BuildLog,
    LogTailDto TestLog,
    string? SessionId,
    // Rendered above the report body; see CommandSummaryDto.Summary.
    string? Summary);

public sealed record WatchDto(LogTailDto Log, bool LockPresent, bool Running);

public sealed record WatchActionResponseDto(bool Success, string? Error);

public sealed record CreateDraftRequest(string Title, string? Body, string? Group, IReadOnlyList<string>? Specs);

public sealed record CreateDraftResponse(string Id, string Path);

public sealed record ChatRequest(string Message, string? ChatId);

public sealed record FollowUpRequest(string Feedback, string? Slug);

public sealed record FollowUpResponse(string Id, string Path, bool ResumeSet, string? Warning);

public sealed record CockpitConfigDto(bool ChatEnabled, string ConfigPath, string RunCommand, string ChatCommand);

public sealed record CommitAllRequest(string Message);

/// <summary>Push is null when nothing was committed or the repo has no remote; otherwise it is the
/// `git push` that followed the commit, rendered inline (same shape as a pull).</summary>
public sealed record CommitAllResponse(bool Committed, GitPullResponse? Push = null);

/// <summary>POST /api/projects/{name}/git/pull — write #8 (ontwerp-v0.5.md item 7). Success is
/// ExitCode==0 &amp;&amp; !TimedOut; Output is git's merged stdout+stderr, rendered inline whether the
/// pull succeeded, fast-forwarded nothing, or was refused by git itself.</summary>
public sealed record GitPullResponse(bool Success, int ExitCode, string Output, bool TimedOut);

/// <summary>POST /api/projects/{name}/git/sync (docs/prompts/0025): the `git pull --rebase` in the
/// GitPullResponse shape, the conflicting files when the rebase stopped (and was aborted), and the
/// `git push` that followed a successful pull (null when it failed, or with no remote).</summary>
public sealed record GitSyncResponse(bool Success, int ExitCode, string Output, bool TimedOut,
    IReadOnlyList<string> Conflicts, GitPullResponse? Push);

/// <summary>GET /api/projects/{name}/git/branches — write #10 (docs/prompts/0014). Current is the
/// branch the working copy is on; Branches is what the dropdown offers and, for the checkout below,
/// the whitelist. FetchError is non-null only for ?fetch=true that failed (an offline worker, a
/// credentials prompt): the local list that follows is still perfectly usable, so this is rendered
/// beside it rather than raised as a request failure.</summary>
public sealed record GitBranchesResponse(string? Current, IReadOnlyList<GitBranch> Branches, string? FetchError);

public sealed record GitCheckoutRequest(string Branch);

/// <summary>POST /api/projects/{name}/git/checkout — write #10. Same shape as GitPullResponse (git's
/// own exit code and merged output, rendered inline whether the switch worked or git refused it),
/// plus the branch that was asked for, so a result line can name it without the page guessing.</summary>
public sealed record GitCheckoutResponse(bool Success, int ExitCode, string Output, bool TimedOut, string Branch);

/// <summary>Reason/PortListening mirror mf-run's own additive `status --json` fields (mf-run's
/// README "Status semantics"): Reason explains why Running is false (no-pid-file, process-gone,
/// starttime-mismatch); PortListening is an independent TCP-connect signal, null when the service
/// has no url to probe.</summary>
public sealed record RunServiceStatusDto(string Name, bool Running, int? Pid, string? Url, string? Reason = null, bool? PortListening = null);

/// <summary>Configured=false means the project has no `run:` block at all (ontwerp-v0.3.md "API") —
/// the cockpit never shells out to mf-run for such a project. Error is set when mf-run itself could
/// not be reached or its status --json output didn't parse (a spawn failure, a stale mf-run binary
/// without --json support, ...); Services is empty in that case.</summary>
public sealed record RunStatusDto(bool Configured, IReadOnlyList<RunServiceStatusDto> Services, string? Error = null);

public sealed record RunActionResponseDto(bool Success, int ExitCode, string Output, bool TimedOut);

public sealed record ProjectConfigSummaryDto(
    IReadOnlyList<string> BuildCommands,
    IReadOnlyList<string> TestCommands,
    int MaxAttempts,
    IReadOnlyList<string> RunServices);

public sealed record ProjectConfigDto(string Content, string Hash, ProjectConfigSummaryDto Summary);

public sealed record ProjectConfigSaveRequest(string Content, string BaseHash);

public sealed record ProjectConfigSaveResponse(string Hash, IReadOnlyList<string> Warnings);

/// <summary>The per-project scratchpad (ontwerp-v0.5.md item 8): plain notes kept outside git.
/// SavedAt is null when the note has never been written.</summary>
public sealed record ScratchpadDto(string Content, string Hash, string? SavedAt);

public sealed record ScratchpadSaveRequest(string Content, string BaseHash);

public sealed record ScratchpadSaveResponse(string Hash, string SavedAt);

/// <summary>GET /api/new-project (ontwerp-v0.4.md "The dialog"): names only — template definitions
/// (command/args/source) never leave the server.</summary>
public sealed record NewProjectInfoDto(string? Root, IReadOnlyList<string> Templates, bool SpecKit, string Separator);

/// <summary>POST /api/projects — write #6 (ontwerp-v0.4.md "Creating a project"). Mode is "new" or
/// "existing"; Path is used by "existing" only, Template by "new" only.</summary>
public sealed record CreateProjectRequest(string Mode, string Name, string? Path, string? Template);

public sealed record CreateProjectResponse(string Name, string Path, IReadOnlyList<string> Warnings, string? SeededDraftId);
