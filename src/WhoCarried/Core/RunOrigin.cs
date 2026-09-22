namespace WhoCarried.Core;

/// <summary>
/// Whether the run starting now was loaded from a save, going by which of the game's set-up calls ran. The save's
/// reload count can't tell on its own: on the public branch (v0.107) only the host bumps it, so on a co-op run's first
/// reload a guest's count is still 0.
/// </summary>
public sealed class RunOrigin
{
    private bool? _loaded;

    public void SetUpNew() => _loaded = false;

    public void SetUpSaved() => _loaded = true;

    /// <summary>
    /// Whether the run starting now was loaded. With no set-up call seen (a replay, or a game whose set-up calls
    /// couldn't be patched), the game's reload count decides. Forgets the answer, so each run asks once.
    /// </summary>
    public bool TakeLoadedFromSave(int numReloads)
    {
        bool loaded = _loaded ?? numReloads > 0;
        _loaded = null;
        return loaded;
    }
}
