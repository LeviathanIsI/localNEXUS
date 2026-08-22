using System.IO;

namespace LocalNEXUS.App.Services.Files;

/// <summary>
/// Collects every file a run wants to write and applies them together, or not at all.
/// </summary>
/// <remarks>
/// A half applied multi file change is worse than no change: three of five scripts land, the
/// project does not compile, and undoing it is now the person's problem. So nothing touches disk
/// until the whole plan has been generated and checked, and if a write fails partway the files
/// already written are put back as they were.
///
/// Writes are in place, never delete and recreate. A Unity script is bound to scenes and prefabs
/// through the GUID in its <c>.cs.meta</c> sibling, and deleting the script to write a new one
/// makes Unity issue a fresh GUID, which silently unbinds every object that used it.
///
/// Staging a path twice replaces the earlier content rather than appending. That is the fix for a
/// documented agent failure, where two edits to one file are applied in sequence and the second
/// quietly reverts the first.
/// </remarks>
public sealed class ProjectWriteBatch
{
    private readonly Dictionary<string, string> _staged = new(StringComparer.OrdinalIgnoreCase);
    private readonly FileWriter _writer;

    public ProjectWriteBatch(FileWriter writer) => _writer = writer;

    /// <summary>How many distinct files are staged.</summary>
    public int Count => _staged.Count;

    /// <summary>The staged paths, in the order they will be written.</summary>
    public IReadOnlyCollection<string> Paths => _staged.Keys;

    /// <summary>
    /// Stages one file. Staging the same path again replaces what was there, so every change to a
    /// file becomes one write.
    /// </summary>
    public void Stage(string absolutePath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        _staged[Path.GetFullPath(absolutePath)] = content;
    }

    /// <summary>
    /// Checks that what is staged agrees with what is on disk about which files already exist.
    /// </summary>
    /// <exception cref="UnityScriptRuleException">A create would overwrite, or an edit has nothing to edit.</exception>
    public void EnforceExpectedExistence(string absolutePath, bool expectedToExist)
    {
        var full = Path.GetFullPath(absolutePath);
        var exists = File.Exists(full);

        if (expectedToExist && !exists)
        {
            throw new UnityScriptRuleException(
                ProjectWriteRule.FileMustExistToEdit,
                $"{full} was planned as an edit, and there is no such file. Nothing was written.");
        }

        if (!expectedToExist && exists)
        {
            throw new UnityScriptRuleException(
                ProjectWriteRule.FileMustNotExistToCreate,
                $"{full} was planned as a new file, and one already exists there. Overwriting it would discard whatever it "
                + "holds, so nothing was written. Plan this as an edit instead.");
        }
    }

    /// <summary>
    /// Writes everything staged. On failure, restores every file already written in this call and
    /// removes any it created, then rethrows.
    /// </summary>
    /// <returns>What was written, each with the size of the change it made.</returns>
    /// <remarks>
    /// The size of each change comes free here and nowhere else: the original has to be read
    /// anyway so that a failure partway can put it back, so counting what changed costs a
    /// comparison rather than a second read.
    /// </remarks>
    public async Task<IReadOnlyList<WrittenFile>> CommitAsync(CancellationToken ct)
    {
        var originals = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var written = new List<WrittenFile>();

        try
        {
            foreach (var (path, content) in _staged)
            {
                ct.ThrowIfCancellationRequested();

                var original = File.Exists(path)
                    ? await File.ReadAllTextAsync(path, ct).ConfigureAwait(false)
                    : null;

                originals[path] = original;

                var bytes = await _writer.WriteAsync(path, content, ct).ConfigureAwait(false);
                written.Add(new WrittenFile(path, bytes, DiffStat.Between(original, content)));
            }

            return written;
        }
        catch
        {
            Rollback(originals, written.Select(w => w.Path));
            throw;
        }
    }

    /// <summary>Forgets everything staged, without touching disk.</summary>
    public void Clear() => _staged.Clear();

    /// <summary>
    /// Puts back what was there. A file that did not exist before is deleted along with the
    /// <c>.cs.meta</c> Unity may already have written beside it.
    /// </summary>
    private static void Rollback(IReadOnlyDictionary<string, string?> originals, IEnumerable<string> written)
    {
        foreach (var path in written)
        {
            try
            {
                if (!originals.TryGetValue(path, out var original))
                {
                    continue;
                }

                if (original is null)
                {
                    File.Delete(path);

                    var meta = path + ".meta";

                    if (File.Exists(meta))
                    {
                        File.Delete(meta);
                    }

                    continue;
                }

                File.WriteAllText(path, original);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Rolling back is best effort by definition: the failure that got us here may be
                // the same one that stops the restore. The exception being handled is the one
                // worth reporting, so this one is swallowed rather than replacing it.
            }
        }
    }
}
