# Investigation: co-op guests lose their stats when the host re-hosts

Investigated 2026-09-22 against repository source (`main` at 4030854), the game's v0.107.1 and v0.111.0 assemblies, and saved runs from September 12 and 16. No code was changed and nothing was reproduced in a live game: that needs two PCs on the public branch.

Comment under review (reported twice):

> When my friends left the game and I re-hosted, they lost records of all fights (points-wise) from before the reload. As host, I did not.

## Verdict

**Confirmed, on the public branch (v0.107).** On a co-op run's **first** reload, every guest's tracker throws away the stats it had and starts over; the host's carries on. The beta (v0.111) is not affected, which is why the one real test of a guest reload passed.

| Claim | Assessment | Evidence |
|---|---|---|
| Guests lose the fights from before the reload | **Confirmed on v0.107** | A guest's reload count is 0 on the first reload, so the resume check fails |
| The host keeps them | **Confirmed** | The host's run key doesn't change, so it matches exactly |
| It happens on every reload | **No, the first only** | The fresh stats take the host's key, so later reloads match exactly |

## How the tracker decides it's the same run

`Tracker.OnRunStarted` (`Tracker.cs:72`) resumes `current_run.dat` only if `RunStatsStore.LoadIfResumable` (`RunStatsStore.cs:35`) says it belongs to this run. The run key is `seed:start time` (`GameReader.RunKey`).

A guest's start time moves. A new co-op run is stamped with each PC's own clock (`SetUpNewMultiplayer` uses `DateTimeOffset.UtcNow`), but a loaded run takes `save.StartTime` from the host's save. The September 12 fix (4ea5167) covered this: when the game is loading a saved run, the seed alone is enough. "Loading a saved run" is read as `RunManager._numReloads > 0` (`GameReader.LoadedFromSave`, `GameReader.cs:258`).

That signal holds on the beta but not on the public branch.

## The two branches load a co-op save differently

`RunManager.SetUpSavedMultiplayer`, decompiled:

```csharp
// v0.111 (beta): every PC bumps the count it was given
await SaveManager.Instance.IncrementNumReloads(save, lobby.NetService.Type);   // save.NumReloads++ first, whatever the type

// v0.107 (public): only the host does
if (lobby.NetService.Type == NetGameType.Host && TestMode.IsOff)
    await SaveManager.Instance.IncrementNumReloads(save, isMultiplayer: true);
```

On v0.107 a guest's `_numReloads` is whatever the host's save held **when the guest joined the lobby**. `LoadRunLobby` sends the save in `ClientLoadJoinResponseMessage` at join time (v0.107 `LoadRunLobby.cs:158`). The host only bumps its own copy later, in `SetUpSavedMultiplayer`, once the run begins. `LobbyBeginLoadedRunMessage` carries nothing. A run that has never been reloaded has `NumReloads = 0` in its save, so on the first reload:

1. The guest's key changes from `seed:<guest's clock>` to `seed:<host's start time>`.
2. `LoadedFromSave()` is false, so only an exact key match counts.
3. `LoadIfResumable` returns null. The tracker copies the old stats to `current_run.previous.dat`, resets `events.log` and starts from zero.

The host's key is the host's own start time both before and after, so it matches exactly and resumes.

A guest escapes only if their clock read the same second as the host's when the run started. The September 16 run's two stamps were 2 seconds apart.

## Supporting evidence

- **September 16, beta, you as a guest:** the log header says `F9RL6739CFWX:1789580126` and the resume line says `resumed run F9RL6739CFWX:1789580128: 23 fights restored`. The key moved and the fallback caught it, because v0.111 bumped the guest's count.
- **September 12, public branch, before the fix:** `runs/_old-RunRecap-data-2026-09-12/` holds `current_run.previous.dat` (`TB20BJ6WUGJ2:1789237698`, 18 fights, unfinished) and `current_run.dat` (`TB20BJ6WUGJ2:1789237700`, 6 fights). This is the same reset, and it's what 4ea5167 set out to fix.
- **Timeline:** 4ea5167 landed on September 12. Support for the beta (`GameCompat`, 54fe237) came on September 15, when the install moved to `public-beta`. The fix has only been seen working on the beta. Its premise, that the game "bumps the save's reload count before loading it", is true there and on the host, but not for a v0.107 guest.
- Every saved `current_run.dat` still deserializes with current code, so an unreadable file isn't a factor.

## What a fix needs

The tracker needs a "this run was loaded" signal that doesn't depend on the reload count. Options, most direct first:

1. **Patch the setup calls.** A Harmony prefix on `RunManager.SetUpSavedSingleplayer` and `SetUpSavedMultiplayer` sets a flag, and prefixes on `SetUpNewSingleplayer` and `SetUpNewMultiplayer` clear it. `OnRunStarted` reads it instead of `_numReloads`. Both branches have these four methods with the same parameters, and the game is saying directly which path it took. Recommended.
2. **Read the run's progress.** A loaded run already has map history (floor above 0) when `RunStarted` fires; a new one doesn't. This needs no patch, but it's an inference, and a run saved before its first room would count as new (with nothing lost).
3. Keep `_numReloads > 0` and use either of the above as a second signal. This is the smallest change, but it keeps a signal we now know is wrong on one branch.

The resume rule itself (`LoadIfResumable` with `loadedFromSave: true`) is already right and tested (`RunStatsStoreTests.LoadedSaveResumesTheSameSeedDespiteANewStartTime`). The bug is only in what `Tracker` passes it, and the Game layer isn't in the test build, so a regression test would need the decision moved into Core.

Affected guests' earlier stats sit in `current_run.previous.dat` until their next reset. The mod has no way to merge them back.

## Fix

Option 1, on `bug/rehost-loses-client-stats`. `NewRunSetUpPatch` and `SavedRunSetUpPatch` (`Game/Patches.cs`) tell `Core/RunOrigin` which set-up ran, and `Tracker.OnRunStarted` asks it instead of reading the count. If neither patch saw a set-up (a replay, or a game version where they fail to apply), the reload count decides as before, so nothing is worse than it was. `RunOriginTests.AGuestsFirstReloadOnThePublicBranchResumes` fails without the fix. The mod builds against both v0.107.1 and v0.111.0. Tested by hand in a real co-op game on 2026-09-22.
