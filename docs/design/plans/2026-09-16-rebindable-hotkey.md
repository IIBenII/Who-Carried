# Rebindable recap hotkey — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a player change the key that opens the recap, from inside the game, without adding a dependency.

**Architecture:** The key is stored as a Godot `Key` enum *name* in `settings.json` in the mod's data folder. `Core/Settings` owns the file and stays free of engine types so the existing test runner covers it; `UI/HotkeyBinding` converts the name to a `Key` and is what everything else reads; `UI/HotkeyRebind` is the press-a-key prompt, started by clicking the recap's status label. Nothing touches Godot's `InputMap` or the game's input manager.

**Tech Stack:** C# / .NET 9, Godot 4.5.1 mono, Harmony 2.4.2, `System.Text.Json` source generation (no runtime reflection).

Spec: [`docs/design/specs/2026-09-16-rebindable-hotkey-design.md`](../specs/2026-09-16-rebindable-hotkey-design.md)

## Global Constraints

- **No new dependencies.** Not BaseLib, not RitsuLib, no NuGet package. The manifest's dependency list stays empty.
- **No new `GameCompat` entries.** This feature must not read the game's input internals.
- **`Core/` has no Godot or game types.** Godot's `Key` may only appear under `UI/`.
- **JSON goes through `Core/WhoCarriedJson.cs`.** The mod runs with reflection-free serialization; a type not registered there will fail at runtime, not at build.
- **Nothing here may take the recap down.** Every entry point is wrapped; a failure logs one line through `Tracker.LogError` and leaves the recap working.
- **No absolute paths in tracked files.** `GameDir` lives only in untracked `local.props`.
- **Build with the x64 SDK.** An x86 `dotnet.exe` shadows it on PATH — always call `C:\Program Files\dotnet\dotnet.exe` by full path.
- **Never push.** Commit on `feature/rebindable-hotkey`; the owner decides when anything leaves the machine.
- **Don't deploy or launch the game without asking.** The game locks the DLL, and the owner may be playing.

## Investigation amendments (2026-09-16)

This section supersedes any conflicting detail in the task text below. It is based on the
current source and both reference game assemblies.

- `TopBarButton` does **not** retain one tooltip. `ShowTip()` calls `GameTip()` on every
  hover, and the fallback also builds a fresh label. Keep `Title` computed, but remove
  `HotkeyBinding.Changed` and Task 4's subscription entirely.
- `HotkeyBinding` exposes `bool IsDown()`. It returns false for `Key.None`; after `Set(key)`
  it returns false while that same key remains physically down, then resumes normal polling
  after release. `RecapUi.OnFrame` uses it and renames `_f8WasDown` to `_hotkeyWasDown`.
  This is required to stop the press used to bind F8 (or any current hotkey) from closing the
  recap in the same frame.

  ```csharp
  private static Key _ignoredUntilReleased = Key.None;

  public static bool IsDown()
  {
      if (_bound == Key.None) return false;
      if (_ignoredUntilReleased == _bound)
      {
          if (Input.IsKeyPressed(_bound)) return false;
          _ignoredUntilReleased = Key.None;
      }
      return Input.IsKeyPressed(_bound);
  }

  public static void Set(Key key)
  {
      _bound = key;
      _ignoredUntilReleased = key;
      // Keep the existing guarded Settings.Save call, but do not raise Changed.
  }
  ```
- `HotkeyRebind.Attach` always subscribes its own root `GuiInput` handler, because
  `PadInput.Attach` can fail before it subscribes. It keeps `_swallowedKey`: once a capture
  has acted on a press, it accepts that press, every echo, and its release. Its public
  `Consumes(InputEvent input)` returns true while listening or for that swallowed key.
  `PadInput.OnInput` starts with `if (HotkeyRebind.Consumes(input)) return;`. This works in
  either signal order: before capture it stands aside because listening is true; after capture
  it stands aside because the same key is swallowed.

  ```csharp
  private static Key _swallowedKey = Key.None;

  public static bool Consumes(InputEvent input) => _listening ||
      input is InputEventKey key && key.Keycode == _swallowedKey;

  private static void OnKey(InputEvent input)
  {
      if (input is InputEventKey swallowed && swallowed.Keycode == _swallowedKey)
      {
          _panel?.Root.AcceptEvent();
          if (!swallowed.Pressed) _swallowedKey = Key.None;
          return;
      }
      if (!_listening || input is not InputEventKey { Pressed: true, Echo: false } key) return;

      _swallowedKey = key.Keycode;
      _panel?.Root.AcceptEvent();
      switch (key.Keycode)
      {
          case Key.Escape: Stop(); break;
          case Key.Delete or Key.Backspace: HotkeyBinding.Set(Key.None); Stop(); break;
          case Key.Shift or Key.Ctrl or Key.Alt or Key.Meta: break;
          default: HotkeyBinding.Set(key.Keycode); Stop(); break;
      }
  }
  ```

  `Forget()` resets `_swallowedKey`; `Stop()` does not, because a stopped prompt still has to
  consume the release and any echo of the key that ended it.
- Cancellation is Esc, actual `FocusExited`, or `TreeExiting`. Per the owner decision, a
  click elsewhere inside the recap does not have to cancel the prompt. `FocusExited` must
  call `Stop`, not re-grab focus; `PadInput` may subsequently take focus back for its own
  controller behaviour.
- Extend the manual check with: bind the currently configured key and confirm the recap stays
  open until that key is released; hold a captured Esc briefly and confirm it still does not
  close the recap.

**Branch:** `feature/rebindable-hotkey` (already created, spec committed as `219d934`).

**Commands** (run from the repo root):

| What | Command |
|---|---|
| Build | `& 'C:\Program Files\dotnet\dotnet.exe' build src/WhoCarried -c Release` |
| All tests | `& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests` |
| One test class | `& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests Settings` |

The runner takes an optional name filter as its first argument ([`tests/WhoCarried.Tests/Program.cs`](../../../tests/WhoCarried.Tests/Program.cs)). Tests are `public static` methods marked `[Test]`, asserted with `Check.Equal(expected, actual, label)` and `Check.True(condition, label)`. Copy the shape of [`DataFolderMoveTests.cs`](../../../tests/WhoCarried.Tests/DataFolderMoveTests.cs).

## File structure

| File | Responsibility |
|---|---|
| `src/WhoCarried/Core/Settings.cs` | **New.** The settings record and its file. Plain C#, unit-tested. Knows nothing about keys beyond "it's a string". |
| `src/WhoCarried/Core/WhoCarriedJson.cs` | Register `Settings` with the serializer context. |
| `src/WhoCarried/UI/HotkeyBinding.cs` | **New.** The current binding as a Godot `Key`. Loads once, saves on change, and suppresses the captured press until release. The single place the rest of the UI asks "what's the key?". |
| `src/WhoCarried/UI/HotkeyRebind.cs` | **New.** The press-a-key prompt: focus, capture, the Esc/Delete/modifier rules. |
| `src/WhoCarried/UI/RecapUi.cs` | Poll the bound key instead of `Key.F8`; `HotkeyName` computed; load the binding at start-up. |
| `src/WhoCarried/UI/RecapPanel.cs` | Status label becomes clickable; idle hint computed; transient messages revert. |
| `src/WhoCarried/UI/PadInput.cs` | Ignore key events while the rebind prompt is listening. |
| `src/WhoCarried/UI/TopBarButton.cs` | `Title` computed whenever the hover tooltip is built. |
| `tests/WhoCarried.Tests/SettingsTests.cs` | **New.** |
| `README.md` | Say the key can be changed. |

Four tasks. Each ends with something that works on its own.

---

### Task 1: The settings file

`Core/Settings` and its tests. Nothing reads it yet — this task is the file format and its failure modes, proved by unit tests.

**Files:**
- Create: `src/WhoCarried/Core/Settings.cs`
- Modify: `src/WhoCarried/Core/WhoCarriedJson.cs`
- Test: `tests/WhoCarried.Tests/SettingsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `WhoCarried.Core.Settings`, a class with `string Hotkey { get; set; }` (JSON name `hotkey`), defaulting to `Settings.DefaultHotkey` = `"F8"`. `""` means unbound.
  - `static (Settings Settings, string? Error) Settings.Load(string dir)` — never throws.
  - `static string? Settings.Save(Settings settings, string dir)` — returns an error message, or null on success.
  - `static string Settings.PathIn(string dir)` — `<dir>/settings.json`.

- [ ] **Step 1: Write the failing tests**

Create `tests/WhoCarried.Tests/SettingsTests.cs`:

```csharp
using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class SettingsTests
{
    private static string NewDir() => Path.Combine(Path.GetTempPath(), "whocarried-tests", Guid.NewGuid().ToString("N"));

    [Test]
    public static void DefaultsToF8WithoutAFile()
    {
        (Settings settings, string? error) = Settings.Load(NewDir());

        Check.Equal("F8", settings.Hotkey, "hotkey");
        Check.Equal<string?>(null, error, "error");
    }

    [Test]
    public static void SavesAndLoadsAKey()
    {
        string dir = NewDir();

        string? saveError = Settings.Save(new Settings { Hotkey = "F9" }, dir);
        (Settings settings, string? loadError) = Settings.Load(dir);

        Check.Equal<string?>(null, saveError, "save error");
        Check.Equal<string?>(null, loadError, "load error");
        Check.Equal("F9", settings.Hotkey, "hotkey");
    }

    [Test]
    public static void KeepsAnUnboundHotkey()
    {
        string dir = NewDir();

        Settings.Save(new Settings { Hotkey = "" }, dir);
        (Settings settings, _) = Settings.Load(dir);

        Check.Equal("", settings.Hotkey, "hotkey");
    }

    [Test]
    public static void FallsBackAndReportsWhenTheFileIsMalformed()
    {
        string dir = NewDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Settings.PathIn(dir), "{ not json");

        (Settings settings, string? error) = Settings.Load(dir);

        Check.Equal("F8", settings.Hotkey, "hotkey");
        Check.True(error != null, "reported the problem");
    }

    [Test]
    public static void KeepsAKeyNameItDoesNotRecognise()
    {
        // Core stores the name verbatim; UI/HotkeyBinding is what falls back to F8 when it can't be parsed.
        string dir = NewDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Settings.PathIn(dir), "{ \"hotkey\": \"Banana\" }");

        (Settings settings, string? error) = Settings.Load(dir);

        Check.Equal("Banana", settings.Hotkey, "hotkey");
        Check.Equal<string?>(null, error, "error");
    }

    [Test]
    public static void WritesTheFileWhereItSaysItDoes()
    {
        string dir = NewDir();

        Settings.Save(new Settings { Hotkey = "F7" }, dir);

        Check.True(File.Exists(Path.Combine(dir, "settings.json")), "settings.json exists");
    }
}
```

- [ ] **Step 2: Run the tests and watch them fail**

```
& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests Settings
```

Expected: a build error — `Settings` does not exist in `WhoCarried.Core`.

- [ ] **Step 3: Write `Core/Settings.cs`**

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhoCarried.Core;

/// <summary>
/// The mod's settings: one entry today, which key opens the recap. The key is the name of a Godot Key enum member
/// ("F8"), or "" for no hotkey at all — held as a string so this file stays free of engine types and the tests can
/// cover it. UI/HotkeyBinding turns the name into a key and decides what to do with one it doesn't recognise.
/// </summary>
public sealed class Settings
{
    public const string DefaultHotkey = "F8";

    [JsonPropertyName("hotkey")]
    public string Hotkey { get; set; } = DefaultHotkey;

    /// <summary>
    /// A ".json" name is safe here: the mod loader reads every .json under mods/ as a manifest, but the data folder
    /// is in the game's save folder, which isn't scanned. Readable matters — hand-editing this is the way back if
    /// someone binds a key they can't press.
    /// </summary>
    public static string PathIn(string dir) => Path.Combine(dir, "settings.json");

    /// <summary>The saved settings, plus what went wrong if anything did. Defaults on a missing or unreadable file.</summary>
    public static (Settings Settings, string? Error) Load(string dir)
    {
        try
        {
            string path = PathIn(dir);
            if (!File.Exists(path)) return (new Settings(), null);
            Settings? saved = JsonSerializer.Deserialize(File.ReadAllText(path), WhoCarriedJson.Default.Settings);
            return saved == null ? (new Settings(), "settings.json was empty") : (saved, null);
        }
        catch (Exception e)
        {
            return (new Settings(), e.Message);
        }
    }

    /// <summary>Writes the settings through a temp file. Returns what went wrong, or null.</summary>
    public static string? Save(Settings settings, string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            string path = PathIn(dir), tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, WhoCarriedJson.Default.Settings));
            File.Move(tmp, path, overwrite: true);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }
}
```

- [ ] **Step 4: Register the type with the serializer**

In `src/WhoCarried/Core/WhoCarriedJson.cs`, add a second `JsonSerializable` attribute beside the existing one:

```csharp
[JsonSerializable(typeof(RunStats))]
[JsonSerializable(typeof(Settings))]
internal partial class WhoCarriedJson : JsonSerializerContext
```

Without this, `WhoCarriedJson.Default.Settings` won't exist and the build fails — which is the point of doing it here rather than discovering it in game.

- [ ] **Step 5: Run the tests and watch them pass**

```
& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests Settings
```

Expected: 6 passed. Then run the whole suite to be sure nothing else moved:

```
& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests
```

Expected: 161 passed (155 before this change, plus 6).

- [ ] **Step 6: Commit**

```
git add src/WhoCarried/Core/Settings.cs src/WhoCarried/Core/WhoCarriedJson.cs tests/WhoCarried.Tests/SettingsTests.cs
git commit -m "Add a settings file for the recap hotkey"
```

---

### Task 2: Read the bound key

The mod starts obeying the file. After this task, editing `settings.json` by hand and restarting the game changes the key — the whole feature works, just without a way to set it in game.

**Files:**
- Create: `src/WhoCarried/UI/HotkeyBinding.cs`
- Modify: `src/WhoCarried/UI/RecapUi.cs` (the `HotkeyName` const at :21, `Install` at :42, `OnFrame` at :229)
- Modify: `src/WhoCarried/UI/TopBarButton.cs` (the `Title` const at :21)
- Modify: `src/WhoCarried/UI/RecapPanel.cs` (the `IdleHint` const at :22)

**Interfaces:**
- Consumes: `Settings.Load`, `Settings.Save`, `Settings.DefaultHotkey` from Task 1.
- Produces:
  - `HotkeyBinding.Load(string dataDir)` — call once at start-up.
  - `HotkeyBinding.Bound` → `Godot.Key`, `Key.None` when unbound.
  - `HotkeyBinding.Name` → `string?`, e.g. `"F8"`, null when unbound.
  - `HotkeyBinding.Set(Key key)` — saves and suppresses that captured press until its release.
  - `HotkeyBinding.IsDown()` → `bool`, false when unbound or while the captured key remains down.

- [ ] **Step 1: Write `UI/HotkeyBinding.cs`**

```csharp
using Godot;
using WhoCarried.Core;
using WhoCarried.Game;

namespace WhoCarried.UI;

/// <summary>
/// Which key opens the recap. Read every frame by RecapUi, changed by HotkeyRebind, and named by anything that
/// tells the player what to press. Godot's Key stops here: Core/Settings only ever sees the name.
/// </summary>
internal static class HotkeyBinding
{
    private static Key _bound = Key.F8;
    private static Key _ignoredUntilReleased = Key.None;
    private static string _dataDir = "";
    private static bool _loaded;

    /// <summary>The key that opens the recap, or Key.None when there isn't one.</summary>
    public static Key Bound => _bound;

    /// <summary>The bound key's name ("F8"), or null when unbound.</summary>
    public static string? Name => _bound == Key.None ? null : _bound.ToString();

    /// <summary>Whether the hotkey is down, except for the press that just captured it.</summary>
    public static bool IsDown()
    {
        if (_bound == Key.None) return false;
        if (_ignoredUntilReleased == _bound)
        {
            if (Input.IsKeyPressed(_bound)) return false;
            _ignoredUntilReleased = Key.None;
        }
        return Input.IsKeyPressed(_bound);
    }

    /// <summary>Reads the saved key. Falls back to F8 on anything unexpected, with one log line.</summary>
    public static void Load(string dataDir)
    {
        _dataDir = dataDir;
        _loaded = true;
        _ignoredUntilReleased = Key.None;
        try
        {
            (Settings settings, string? error) = Settings.Load(dataDir);
            if (error != null) Tracker.Note($"settings: {error}; using {Settings.DefaultHotkey}");
            _bound = Parse(settings.Hotkey, out bool unknown);
            if (unknown) Tracker.Note($"settings: don't know the key '{settings.Hotkey}'; using {Settings.DefaultHotkey}");
        }
        catch (Exception e)
        {
            Tracker.LogError("reading settings (using F8)", e);
            _bound = Key.F8;
        }
    }

    /// <summary>Binds a key (Key.None to unbind), saves it, and ignores this physical press until it ends.</summary>
    public static void Set(Key key)
    {
        try
        {
            _bound = key;
            _ignoredUntilReleased = key;
            if (_loaded)
            {
                string? error = Settings.Save(new Settings { Hotkey = key == Key.None ? "" : key.ToString() }, _dataDir);
                Tracker.Note(error == null ? $"hotkey set to {Name ?? "none"}" : $"hotkey set to {Name ?? "none"} but not saved: {error}");
            }
        }
        catch (Exception e)
        {
            Tracker.LogError("saving the hotkey", e);
        }
    }

    /// <summary>An empty name means unbound; a name we don't recognise falls back to F8 and says so through `unknown`.</summary>
    private static Key Parse(string? name, out bool unknown)
    {
        unknown = false;
        if (string.IsNullOrEmpty(name)) return Key.None;
        if (Enum.TryParse(name, ignoreCase: true, out Key key) && Enum.IsDefined(typeof(Key), key)) return key;
        unknown = true;
        return Key.F8;
    }
}
```

`Enum.IsDefined` matters: `Enum.TryParse` happily accepts `"12345"` and returns a `Key` that is not a real member.

- [ ] **Step 2: Load it at start-up and poll it**

In `src/WhoCarried/UI/RecapUi.cs`, replace the const at line 21:

```csharp
    /// <summary>The key that toggles the recap, as the top-bar tooltip names it.</summary>
    public static string HotkeyName => HotkeyBinding.Name ?? "unbound";
```

In `Install()`, load the binding before anything reads it — add this as the first line of the method, before the `if (Engine.GetMainLoop() is SceneTree tree)`:

```csharp
        HotkeyBinding.Load(Tracker.DataDir);
```

In `OnFrame()`, replace the hard-coded key:

```csharp
    private static void OnFrame()
    {
        try
        {
            bool down = HotkeyBinding.IsDown();
            if (down && !_hotkeyWasDown) Toggle();
            _hotkeyWasDown = down;
        }
        catch (Exception e)
        {
            Tracker.LogError("hotkey", e);
        }
    }
```

Rename the field `_f8WasDown` to `_hotkeyWasDown` (declared at `RecapUi.cs:26`) so it doesn't lie about which key it tracks.

- [ ] **Step 3: Make the two other consts computed**

`src/WhoCarried/UI/TopBarButton.cs` line 21 — a `const` can't call a property, so it becomes one:

```csharp
    public static string Title => HotkeyBinding.Name is string key
        ? $"{RecapTexts.ModName} ({key})"
        : RecapTexts.ModName;
```

`src/WhoCarried/UI/RecapPanel.cs` line 22:

```csharp
    public static string IdleHint => HotkeyBinding.Name is string key ? $"{key} toggles" : "click to set a key";
```

- [ ] **Step 4: Build**

```
& 'C:\Program Files\dotnet\dotnet.exe' build src/WhoCarried -c Release
```

Expected: build succeeded, 0 errors. If `Title` is rejected as a constant expression somewhere, that's a caller expecting a compile-time value — make it read the property.

- [ ] **Step 5: Run the whole test suite**

```
& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests
```

Expected: 161 passed. `Core/` didn't change in this task, so a failure here means something unrelated broke.

- [ ] **Step 6: Commit**

```
git add src/WhoCarried/UI/HotkeyBinding.cs src/WhoCarried/UI/RecapUi.cs src/WhoCarried/UI/TopBarButton.cs src/WhoCarried/UI/RecapPanel.cs
git commit -m "Open the recap with the key from the settings file"
```

---

### Task 3: Change the key in game

Clicking the status label starts listening; the next key press binds. This is the task the feature exists for.

**Files:**
- Create: `src/WhoCarried/UI/HotkeyRebind.cs`
- Modify: `src/WhoCarried/UI/RecapPanel.cs` (the status label at :45, `TopBar` at :106)
- Modify: `src/WhoCarried/UI/RecapUi.cs` (`ShowView` at :76, the status writes at :199)
- Modify: `src/WhoCarried/UI/PadInput.cs` (`OnInput` at :154)

**Interfaces:**
- Consumes: `HotkeyBinding.Set`, `HotkeyBinding.Bound`, `HotkeyBinding.Name` from Task 2; `PanelHandle` (`Root`, `Status`) from `RecapPanel`.
- Produces:
  - `HotkeyRebind.Attach(PanelHandle panel)` — call when a panel opens.
  - `HotkeyRebind.Start()` — begin listening.
  - `HotkeyRebind.Consumes(InputEvent input)` → `bool`, read by `PadInput`.

- [ ] **Step 1: Write `UI/HotkeyRebind.cs`**

```csharp
using Godot;
using WhoCarried.Game;

namespace WhoCarried.UI;

/// <summary>
/// The "press a key" prompt behind the recap's status label. The mod can't override Godot virtuals — the game loads
/// mod DLLs without registering script classes — so keys arrive through the panel root's GuiInput signal, the same
/// route controller support uses. This listener remains independent of PadInput, whose attachment can fail.
/// </summary>
internal static class HotkeyRebind
{
    private static PanelHandle? _panel;
    private static bool _listening;
    private static Key _swallowedKey = Key.None;

    /// <summary>True while the next key press will be taken as the new binding.</summary>
    public static bool Listening => _listening;

    /// <summary>True while capture owns an event, including the echoes and release of a captured key.</summary>
    public static bool Consumes(InputEvent input) => _listening ||
        input is InputEventKey key && key.Keycode == _swallowedKey;

    /// <summary>Points the prompt at a newly opened recap. Call once per panel.</summary>
    public static void Attach(PanelHandle panel)
    {
        Forget();   // not Stop(): the old panel may already be freed, and there's no hint left to restore on it
        _panel = panel;
        try
        {
            panel.Status.MouseFilter = Control.MouseFilterEnum.Stop;
            panel.Status.TooltipText = "Click to change the key that opens this";
            panel.Status.GuiInput += OnStatusClicked;
            panel.Root.GuiInput += OnKey;
            panel.Root.FocusExited += Stop;   // actual focus loss abandons the prompt
            panel.Root.TreeExiting += Forget; // the recap closed, or a resize is rebuilding it
        }
        catch (Exception e)
        {
            Tracker.LogError("hotkey rebind (the key still works)", e);
        }
    }

    /// <summary>Starts listening. The label says so until a key arrives or the prompt is cancelled.</summary>
    public static void Start()
    {
        if (_panel == null || !GodotObject.IsInstanceValid(_panel.Root)) return;
        _listening = true;
        SetStatus("press a key   (Esc cancels, Delete clears)");
        _panel.Root.FocusMode = Control.FocusModeEnum.All;
        if (_panel.Root.IsInsideTree()) _panel.Root.GrabFocus();
    }

    private static void OnStatusClicked(InputEvent input)
    {
        try
        {
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Start();
        }
        catch (Exception e)
        {
            Tracker.LogError("hotkey rebind click", e);
        }
    }

    private static void OnKey(InputEvent input)
    {
        if (input is InputEventKey swallowed && swallowed.Keycode == _swallowedKey)
        {
            _panel?.Root.AcceptEvent();
            if (!swallowed.Pressed) _swallowedKey = Key.None;
            return;
        }
        if (!_listening || input is not InputEventKey { Pressed: true, Echo: false } key) return;
        try
        {
            _swallowedKey = key.Keycode;
            _panel?.Root.AcceptEvent(); // the key being bound mustn't also switch a tab or close the recap
            switch (key.Keycode)
            {
                case Key.Escape:
                    Stop();
                    return;
                case Key.Delete or Key.Backspace:
                    HotkeyBinding.Set(Key.None);
                    Stop();
                    return;
                case Key.Shift or Key.Ctrl or Key.Alt or Key.Meta:
                    return; // a modifier on its own polls as held forever; keep listening
                default:
                    HotkeyBinding.Set(key.Keycode);
                    Stop();
                    return;
            }
        }
        catch (Exception e)
        {
            Tracker.LogError("hotkey rebind", e);
            Stop();
        }
    }

    /// <summary>Stops listening and puts the idle hint back, whatever the binding ended up as.</summary>
    private static void Stop()
    {
        _listening = false;
        SetStatus(RecapPanel.IdleHint);
    }

    /// <summary>The panel is going away: stop listening and let go of it, so a stale handle can't be written to.</summary>
    private static void Forget()
    {
        _listening = false;
        _swallowedKey = Key.None;
        _panel = null;
    }

    private static void SetStatus(string text)
    {
        if (_panel != null && GodotObject.IsInstanceValid(_panel.Status)) _panel.Status.Text = text;
    }
}
```

- [ ] **Step 2: Attach it when a panel opens**

In `src/WhoCarried/UI/RecapUi.cs`, in `ShowView`, add the attach **before** `PadInput.Attach(handle)`:

```csharp
        EnsureLayer().AddChild(handle.Root);
        HotkeyRebind.Attach(handle);
        PadInput.Attach(handle);
        return handle;
```

Order matters for readability only — `PadInput` stands aside via the `Listening` flag, not via subscription order, because `PadInput.Attach` can fail and never subscribe at all.

- [ ] **Step 3: Let the rebind have the key**

In `src/WhoCarried/UI/PadInput.cs`, at the very top of `OnInput` (line 154), before the mouse filter:

```csharp
    private void OnInput(InputEvent input)
    {
        if (HotkeyRebind.Consumes(input)) return; // capture owns its press, echoes, and release
        if (input is InputEventMouse or InputEventScreenTouch or InputEventScreenDrag or InputEventGesture) return;
```

This handles either signal order: before capture, `Listening` keeps PadInput out; after capture,
the swallowed key keeps it out until release. Esc therefore cannot close the recap while it cancels.

- [ ] **Step 4: Make transient status messages go away**

`RecapUi.Export` writes into the status label and leaves the text there, so after one export the label reads `Saved to your Steam screenshots` forever. Now that the label is also the control showing the current key, it has to come back.

In `src/WhoCarried/UI/RecapUi.cs`, replace the local `Show` helper inside `Export` (around line 200):

```csharp
        void Show(string text)
        {
            if (!GodotObject.IsInstanceValid(handle.Status)) return;
            handle.Status.Text = text;
            if (Engine.GetMainLoop() is not SceneTree tree) return;
            SceneTreeTimer timer = tree.CreateTimer(4.0);
            timer.Timeout += () =>
            {
                if (GodotObject.IsInstanceValid(handle.Status) && !HotkeyRebind.Listening) handle.Status.Text = RecapPanel.IdleHint;
            };
        }
```

The `Listening` check stops a timer left over from an export overwriting the "press a key" prompt.

- [ ] **Step 5: Build**

```
& 'C:\Program Files\dotnet\dotnet.exe' build src/WhoCarried -c Release
```

Expected: build succeeded, 0 errors.

- [ ] **Step 6: Run the whole test suite**

```
& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests
```

Expected: 161 passed.

- [ ] **Step 7: Commit**

```
git add src/WhoCarried/UI/HotkeyRebind.cs src/WhoCarried/UI/RecapUi.cs src/WhoCarried/UI/PadInput.cs
git commit -m "Change the recap's key by clicking the hint"
```

---

### Task 4: Document it, then see it running

The podium tooltip is built on hover and reads the computed title then, so it already stays current. Document the feature and verify it in game on both branches.

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: `TopBarButton.Title` from Task 2.
- Produces: nothing further.

- [ ] **Step 1: Build**

```
& 'C:\Program Files\dotnet\dotnet.exe' build src/WhoCarried -c Release
```

Expected: build succeeded, 0 errors.

- [ ] **Step 2: Say so in the README**

In `README.md`, in the "Good to know" list, after the **Controller** bullet:

```markdown
- **The hotkey** can be changed in game: open the recap and click the `F8 toggles` hint in its top bar, then press the key you want. Delete clears it, leaving the podium button. It's kept in `settings.json` in the mod's data folder.
```

And update the opening paragraph, which promises F8 specifically:

```markdown
It can be opened mid-run too, with **F8** (rebindable) or the podium button on the game's top bar.
```

- [ ] **Step 3: Commit**

```
git add README.md
git commit -m "Document the rebindable recap hotkey"
```

- [ ] **Step 4: Check it in game — ask first**

**Do not deploy or launch without asking the owner.** The game locks the DLL and they may be playing. When they say go:

```
powershell -File tools/deploy.ps1
```

Then create `preview.flag` in the data folder (`%APPDATA%\SlayTheSpire2\WhoCarried`) and launch `steam://rungameid/2868840`. About 10 s after load the recap opens with sample data.

What to confirm by hand, with the recap open:

1. The status label reads `F8 toggles` and shows the tooltip on hover.
2. Clicking it changes the text to `press a key`.
3. Pressing `F9` binds it — the label reads `F9 toggles`, and `F9` now toggles the recap while `F8` does nothing.
4. `settings.json` in the data folder contains `"hotkey": "F9"`.
5. The podium tooltip reads `Who Carried? (F9)` without restarting.
6. Clicking the label and pressing `Esc` leaves the binding alone and does **not** close the recap.
7. Clicking the label and pressing `Delete` unbinds: the label reads `click to set a key`, no key opens the recap, and the podium button still does.
8. Clicking elsewhere in the recap leaves the prompt active; pressing Esc afterwards cancels it and leaves the binding alone.
9. Bind the currently configured key and keep it held: the recap stays open until that key is released.
10. Hold Esc briefly while cancelling: the recap stays open.
11. Export an image; the status reverts to the hint after about 4 seconds.
12. Restart the game: the binding survives.

Then set it back to F8 and close the game fully — the owner asked that any game started here is fully closed.

- [ ] **Step 5: Check the other game branch**

The mod ships one DLL for both branches. This feature adds no `GameCompat` entries and touches no game input APIs, so it should behave identically — confirm rather than assume. Repeat steps 1–3 and 5 of the check above on whichever branch wasn't used the first time. Reference DLLs are at `E:\Claude\sts2-refs\v0.107.1` and `E:\Claude\sts2-refs\v0.111.0`; build against either with `-p:GameData=<folder>`.

- [ ] **Step 6: Squash into main**

Only when the owner is happy with the in-game check:

```
git checkout main
git merge --squash feature/rebindable-hotkey
git commit
git branch -D feature/rebindable-hotkey
```

Do not push. Pushing is the owner's call.

---

## Notes for whoever implements this

- **`PadInput.Attach` can fail.** It sets `_failed` and never subscribes to `GuiInput` when the game's controller bindings can't be read ([`PadInput.cs:78`](../../../src/WhoCarried/UI/PadInput.cs)). That's why `HotkeyRebind` subscribes to `panel.Root.GuiInput` itself instead of borrowing PadInput's handler.
- **A `Label` ignores the mouse by default.** `MouseFilter = Stop` in `HotkeyRebind.Attach` is what makes the click land; without it `GuiInput` never fires.
- **Binding a key the game uses is allowed.** The poll is independent of the game's input handling, so the key does both jobs. That's deliberate — see the spec. Don't add conflict detection; it would mean reading the game's remappable-input list, whose name differs per branch.
- **Don't reach for `InputMap` or `NInputManager`.** The whole point of this design is that it doesn't.
