# Generic damage attribution spike

**Throwaway research code, not a shipping implementation.** This project is not in `WhoCarried.sln` and is not referenced by the mod. It never launches the game, modifies installed mods, or patches the game process.

See the [findings](../../../docs/investigations/2026-09-18-generic-attribution-spike.md) and [original Burn investigation](../../../docs/investigations/2026-09-18-hextech-burn-attribution.md).

## Run the behavioral probe

Requires the .NET 9 SDK (or a later compatible SDK), the .NET 9 runtime, and the game's `0Harmony.dll`. Configure `GameDir` in the repository's existing `local.props`, or supply `-p:GameData=<directory containing 0Harmony.dll>`. No NuGet packages are used. The metadata inspector references the SDK's `System.Reflection.MetadataLoadContext.dll`.

On this Windows machine, use the x64 dotnet executable:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' run --project tools/spikes/GenericAttribution -c Release
```

Expected result: **18/18**. The probe applies real Harmony prefixes/finalizers to synthetic effect hooks and a `Task<T>` damage boundary inside this console process. The synthetic types model the inspected game call shapes; they are not actual game instances. The checks establish scope behavior, not full game compatibility.

Run the negative control in a **separate process**:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' run --project tools/spikes/GenericAttribution -c Release -- --baseline
```

Expected result: **11/18**, exit code **1**. Seven checks deliberately fail because no attribution patches are installed. A zero exit code here would mean the negative control no longer reproduces the missing-source behavior.

## Inspect installed assemblies without executing them

```powershell
$gameData = 'E:\Games\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64'
$workshop = 'E:\Games\steamapps\workshop\content\2868840'
& 'C:\Program Files\dotnet\dotnet.exe' run --project tools/spikes/GenericAttribution -c Release -- --inspect $gameData `
    "$workshop\3747501308\lib\0.111.0\HextechRunes.dll" `
    "$workshop\3776781597\lib\0.110.1\Oddmelt.dll" `
    "$workshop\3737335127\BaseLib\BaseLib.dll"
```

Paths above identify the installed variants investigated on 2026-09-18. Supply paths appropriate to your installation, including dependencies. The inspector reports concrete model counts, distinct implementations of non-generic `Task` hooks rooted in `AbstractModel`, declaration names, per-hook counts, and SHA-256 hashes. Shared inherited implementations are counted once in the total.

This uses `MetadataLoadContext`, so game/mod initializers and constructors do not execute. It deliberately fails on missing dependencies rather than presenting a partial scan as complete. It is a narrow inspection utility, not a general .NET virtual-slot resolver.

## Files

- `AttributionProbe.cs`: asynchronous effect/damage scopes with original-task lifetime and combat generation checks; no mod names, IDs, or formulas.
- `HookDiscovery.cs`: runtime declaration discovery and the limited metadata equivalent, including inherited compatibility bridges.
- `Probe.cs`: behavioral cases with real suspension, nested calls, overlap, errors, cancellation, and conservative unknown attribution.
- `InspectAssemblies.cs`: read-only metadata scan.
- `Program.cs`: probe/negative-control/inspection entry points.

Do not copy the prototype directly into production. It lacks game lifecycle registration, patch-conflict handling, localization/model extraction, contributor accounting, operational diagnostics, and measured game performance. See the findings for the proposed next stage.
