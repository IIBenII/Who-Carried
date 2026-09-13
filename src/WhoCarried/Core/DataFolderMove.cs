namespace WhoCarried.Core;

/// <summary>
/// Moves the mod's files out of the folder older versions kept them in (data/ beside the DLL). Where both folders have
/// a file, the newer one wins; an older copy is left where it was rather than deleted. Never throws.
/// </summary>
public static class DataFolderMove
{
    /// <returns>How many files were moved, and the first failure (the rest are still tried).</returns>
    public static (int Moved, string? Error) Run(string from, string to)
    {
        if (!Directory.Exists(from)) return (0, null);
        int moved = 0;
        string? error = null;
        try
        {
            foreach (string source in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(to, Path.GetRelativePath(from, source));
                try
                {
                    if (File.Exists(target) && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(source)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Move(source, target, overwrite: true);
                    moved++;
                }
                catch (Exception e)
                {
                    error ??= $"{Path.GetFileName(source)}: {e.Message}";
                }
            }
            RemoveIfEmpty(from);
        }
        catch (Exception e)
        {
            error ??= e.Message;
        }
        return (moved, error);
    }

    /// <summary>Removes the folder if nothing but empty folders is left in it.</summary>
    private static void RemoveIfEmpty(string dir)
    {
        foreach (string sub in Directory.GetDirectories(dir)) RemoveIfEmpty(sub);
        if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
    }
}
