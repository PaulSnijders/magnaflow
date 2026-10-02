namespace MagnaFlow.MfCockpit.Config;

/// <summary>
/// Mutable in-memory project list, seeded from CockpitConfig.Projects at startup. Write #6's
/// registration step (ontwerp-v0.4.md "Creating a project") appends here in the same request it
/// appends to magnaflow.yml (MagnaflowYmlAppender) so a newly added project is served without a
/// restart. Every endpoint resolves projects through this registry, not CockpitConfig.Projects
/// directly, precisely so the two never drift apart.
/// </summary>
public sealed class ProjectRegistry
{
    private readonly List<ProjectEntry> _projects;
    private readonly object _gate = new();

    public ProjectRegistry(IEnumerable<ProjectEntry> initial) => _projects = [.. initial];

    public IReadOnlyList<ProjectEntry> All
    {
        get { lock (_gate) return [.. _projects]; }
    }

    public ProjectEntry? Find(string name)
    {
        lock (_gate) return _projects.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
    }

    public bool ExistsByName(string name) => Find(name) is not null;

    /// <summary>Duplicate-path check for "mode: existing" (ontwerp-v0.4.md: "duplicate name *or*
    /// path -&gt; 409"). Case-insensitive, full-path comparison — same posture Windows paths need
    /// everywhere else in this codebase.</summary>
    public bool ExistsByPath(string path)
    {
        var full = Path.GetFullPath(path);
        lock (_gate) return _projects.Any(p => string.Equals(Path.GetFullPath(p.Path), full, StringComparison.OrdinalIgnoreCase));
    }

    public void Add(ProjectEntry entry)
    {
        lock (_gate) _projects.Add(entry);
    }

    /// <summary>Write #7's registration removal (ontwerp-v0.5.md item 4): drops the in-memory entry
    /// in the same request the magnaflow.yml text edit removed it, so the project stops being served
    /// without a restart. Returns false when the name wasn't registered.</summary>
    public bool Remove(string name)
    {
        lock (_gate)
        {
            var index = _projects.FindIndex(p => string.Equals(p.Name, name, StringComparison.Ordinal));
            if (index < 0)
                return false;
            _projects.RemoveAt(index);
            return true;
        }
    }
}
