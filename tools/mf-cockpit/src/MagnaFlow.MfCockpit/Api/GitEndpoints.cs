using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Infrastructure;
using MagnaFlow.MfCockpit.Models;

namespace MagnaFlow.MfCockpit.Api;

/// <summary>Git info (slow half of the old GET /api/projects — branch/dirty/commits/default-
/// commit-message, at least one `git` subprocess spawn) and write #4, "commit all"
/// (ontwerp-v0.1.md "Invariant"): the git commit/push-of-hand-edits action button the v0.1 design
/// doc anticipated but deferred. Literally `git add -A &amp;&amp; git commit -m "..." &amp;&amp; git push` over
/// whatever is currently pending; the cockpit never decides what that is.</summary>
public static class GitEndpoints
{
    public static void MapGitApi(this IEndpointRouteBuilder app)
    {
        // Three `git` subprocesses per call, and every open page re-reads it on every SSE event —
        // so the result is memoized per project for a few seconds (docs/prompts/0010). The writes
        // below invalidate their own project immediately, so a button's own refresh is never stale.
        app.MapGet("/api/projects/{name}/git", async (string name, ProjectRegistry registry, IGitClient git,
            TtlCache<ProjectGitInfoDto> cache) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();
            if (!Directory.Exists(project.Path))
                return Results.Ok(new ProjectGitInfoDto(false, null, false, [], null));

            var dto = await cache.GetOrAddAsync(project.Name, async () =>
            {
                var info = await git.GetInfoAsync(project.Path);
                var defaultMessage = GitCommitMessageSuggester.SuggestDefault(info.ChangedFiles);
                return new ProjectGitInfoDto(info.Available, info.Branch, info.Dirty, info.Commits, defaultMessage);
            });
            return Results.Ok(dto);
        });

        app.MapPost("/api/projects/{name}/git/commit-all", async (string name, CommitAllRequest request, ProjectRegistry registry, IGitClient git,
            TtlCache<ProjectGitInfoDto> cache) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();
            if (string.IsNullOrWhiteSpace(request.Message))
                return Results.BadRequest(new { error = "message is required" });

            try
            {
                var committed = await git.CommitAllAsync(project.Path, request.Message.Trim());
                cache.Invalidate(project.Name); // the tree is clean now — the page's own refresh must see that
                if (!committed)
                    return Results.Ok(new CommitAllResponse(false));

                // Commit, then push. A failed push is still a 200: the commit stands, and git's own
                // refusal is what the card shows.
                var push = await git.PushAsync(project.Path);
                return Results.Ok(new CommitAllResponse(true, push is null
                    ? null
                    : new GitPullResponse(push.Succeeded, push.ExitCode, push.Output, push.TimedOut)));
            }
            catch (GitException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        // Write #8: exactly `git pull --ff-only` (ontwerp-v0.5.md item 7). Guards first — refuse
        // mid-run (409, same reason remove-project does), refuse a dirty tree (point at "commit
        // all", the worker's own dirty-tree reasoning) — then pull and pass the exit code + output
        // straight back for inline rendering. On success the lane files change on disk, so the
        // existing FileSystemWatcher/SSE refreshes the page.
        app.MapPost("/api/projects/{name}/git/pull", async (string name, ProjectRegistry registry, IGitClient git,
            TtlCache<ProjectGitInfoDto> cache) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();
            if (!Directory.Exists(project.Path))
                return Results.BadRequest(new { error = "project path is missing on disk" });
            if (ProjectResolver.HasRunningCommand(project))
                return Results.Conflict(new { error = "a command is currently running — pull would disrupt the live run; wait until it finishes" });

            // Deliberately uncached: this is the guard that decides whether to touch the tree at
            // all, and a few-seconds-old "clean" is not good enough to act on.
            var info = await git.GetInfoAsync(project.Path);
            if (!info.Available)
                return Results.BadRequest(new { error = "git is not available for this project" });
            if (info.Dirty)
                return Results.Conflict(new { error = "the working tree is dirty — commit or discard your changes first (use \"Commit all\"), then pull" });

            var result = await git.PullFastForwardAsync(project.Path);
            cache.Invalidate(project.Name); // new commits (or none) — either way the card refetches for real
            return Results.Ok(new GitPullResponse(result.Succeeded, result.ExitCode, result.Output, result.TimedOut));
        });

        // Write #10, the read half (docs/prompts/0014): what the Git card's branch dropdown offers,
        // and the whitelist the checkout below validates against. Deliberately *not* in the TtlCache
        // — what 0010 made expensive was `git status --untracked-files=all` and `git log`, and this
        // is neither: one local ref read, about what `rev-parse` costs. ?fetch=true adds exactly one
        // `git fetch` in front of it, and that flag is the only thing in this flow that touches the
        // network — never on page load, never on opening the dropdown, only on the human pressing
        // the card's refresh control. On the Linux worker that is the difference between seeing a
        // branch another machine pushed and not seeing it at all.
        app.MapGet("/api/projects/{name}/git/branches", async (string name, bool? fetch, ProjectRegistry registry, IGitClient git) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();
            if (!Directory.Exists(project.Path))
                return Results.BadRequest(new { error = "project path is missing on disk" });

            string? fetchError = null;
            if (fetch == true)
            {
                // A failed fetch is not a failed request: the local list below is still the truth
                // about this working copy, and "offline" is exactly what the card should say.
                var fetched = await git.FetchAsync(project.Path);
                if (!fetched.Succeeded)
                    fetchError = fetched.TimedOut
                        ? "git fetch timed out"
                        : (fetched.Output.Length > 0 ? fetched.Output : $"git fetch failed (exit {fetched.ExitCode})");
            }

            var branches = await git.ListBranchesAsync(project.Path);
            return Results.Ok(new GitBranchesResponse(branches.Current, branches.Branches, fetchError));
        });

        // Write #10, the write half: exactly `git switch <branch>`. Guards in the same order and for
        // the same reasons as the pull above — unknown project, then the branch whitelist, then
        // refuse mid-run (a checkout would swap the tree under the agent), then refuse a dirty tree
        // (pointing at "commit all"). The lane is *branch* state, not project state — bookkeeping
        // commits on the invoking branch — so this is the one control that moves a whole machine
        // from one fase to the next. On the desktop you would type it; on the Linux worker there is
        // no terminal in that flow, and this is the whole reason the button exists.
        app.MapPost("/api/projects/{name}/git/checkout", async (string name, GitCheckoutRequest request, ProjectRegistry registry, IGitClient git,
            TtlCache<ProjectGitInfoDto> cache) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();
            if (!Directory.Exists(project.Path))
                return Results.BadRequest(new { error = "project path is missing on disk" });

            var branch = (request.Branch ?? string.Empty).Trim();
            // The list *is* the whitelist: no caller-supplied text ever reaches a git argument.
            // It also fails closed — a repo whose ref read failed offers nothing to switch to.
            var branches = await git.ListBranchesAsync(project.Path);
            if (!branches.Branches.Any(b => string.Equals(b.Name, branch, StringComparison.Ordinal)))
                return Results.BadRequest(new { error = $"unknown branch \"{branch}\" — refresh the branch list and try again" });

            if (ProjectResolver.HasRunningCommand(project))
                return Results.Conflict(new { error = "a command is currently running — switching branches would swap the tree under it; wait until it finishes" });

            // Deliberately uncached, exactly as the pull reads it: this is the guard that decides
            // whether to touch the tree at all, and a few-seconds-old "clean" is not good enough.
            var info = await git.GetInfoAsync(project.Path);
            if (!info.Available)
                return Results.BadRequest(new { error = "git is not available for this project" });
            if (info.Dirty)
                return Results.Conflict(new { error = "the working tree is dirty — commit or discard your changes first (use \"Commit all\"), then switch" });

            var result = await git.SwitchAsync(project.Path, branch);
            cache.Invalidate(project.Name); // a different branch means a different HEAD and log — never serve the old one
            return Results.Ok(new GitCheckoutResponse(result.Succeeded, result.ExitCode, result.Output, result.TimedOut, branch));
        });
    }
}
