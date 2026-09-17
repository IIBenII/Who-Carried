# Rebindable recap hotkey

The recap opens on **F8**, hard-coded. F8 is a common key and other mods claim it, so a
player who already uses it has no way out. This adds one setting — which key opens the
recap — and a way to change it without leaving the game.

Scope is the hotkey and nothing else. There is no general options screen, no second
setting, and no new dependency.

## Why not the game's settings screen

The game's Settings → Input list is reachable from a mod: `NInputSettingsPanel` builds its
rows from `NInputManager`'s remappable-input list, which is an `IReadOnlyList` over a
mutable `List`, and the mapping loaded from `settings.save` accepts any action name whose
value parses as a `Key`. So a native-looking row is possible.

It was rejected for three reasons.

Every name involved changed between the two game versions the mod supports:

| | v0.107.1 | v0.111.0 |
|---|---|---|
| remappable list | `remappableKeyboardInputs` | `remappableMKbInputs` (+ a keyboard-only list) |
| key map field | `_keyboardInputMap` | `_mKbInputMap` (+ `_fKbInputMap`) |
| read a binding | `GetShortcutKey` | `GetMKbHotkey` |
| rebind | `ModifyShortcutKey` | `ModifyMKbKey` |
| label lookup | `_commandToLocTitle` (private) | `commandToLocTitle` (public) |

That is five more entries in `Game/GameCompat.cs`, on UI internals that were already
reshuffled once in four versions.

The rebind would live in the game's own `settings.save`, where it would stay after someone
uninstalls the mod.

The game resolves a rebind conflict by swapping: binding the recap to `M` moves View Map
onto F8. A stats mod silently moving a core game key is worse than no rebinding at all.

The third option, a config file with no UI, does not meet the requirement that the key can
be changed in game.

## How it works

The recap's top bar already shows `F8 toggles` as a faint status label. That label becomes
the control. Clicking it starts listening; the next key press becomes the binding.

```
   idle          F8 toggles              click
   listening     press a key…            next key binds, Esc cancels, Delete clears
   bound         F9 toggles              saved, reverts to idle
   unbound       podium button opens     no key opens the recap
```

Nothing new appears on screen. The text that tells you the key is the thing you click to
change it, so it needs no label, no icon and no room in a bar that is already full.

### Storing it

`Core/Settings.cs`, a plain C# record beside the rest of `Core/`, with no Godot or game
types so the existing test runner can cover it:

```json
{ "hotkey": "F8" }
```

Saved as `settings.json` in the mod's data folder (`%APPDATA%\SlayTheSpire2\WhoCarried`),
next to `current_run.dat`. JSON goes through the source-generated `Core/WhoCarriedJson.cs`
context, as the mod has no runtime reflection.

The value is the name of a Godot `Key` enum member, held as a **string** in `Core` and
converted to `Key` in `UI`. That is what keeps `Core` free of Godot types. An empty string
means unbound.

`Settings` never throws. A missing file, unreadable file, malformed JSON or unrecognised
key name all fall back to F8, and anything other than a missing file is logged once.

A `.json` name is safe here. The rule that the mod's own files avoid that extension applied
to files under `mods/`, which the loader scans for manifests; the data folder moved out of
`mods/` and is not scanned. A readable name is worth having, as hand-editing the file is the
fallback if someone binds a key they cannot press.

### Reading it

`RecapUi.OnFrame` polls `Input.IsKeyPressed(Key.F8)` today. It polls the bound key instead,
and polls nothing when unbound. This does not touch Godot's `InputMap` or the game's input
manager, so the game's own keys keep working exactly as before.

When a key is captured, polling ignores that key until its release. This prevents the press
that binds the current hotkey from immediately toggling (and therefore closing) the already
open recap in the same frame. It also absorbs the key's echo and release from recap controls.

### Capturing the key

The mod cannot override Godot virtuals — the game loads mod DLLs without registering script
classes — so `_UnhandledKeyInput` is not available. The existing route is the one controller
support already uses: the recap panel root takes focus and raises `GuiInput`.
`UI/PadInput.cs` already reads `InputEventKey` from it.

While listening, the rebind check runs **before** the pad-command mapping and marks the
event handled, so the key that is being bound does not also switch a tab or close the panel.
The root signal must remain the capture route even when controller support has disabled
itself. `PadInput` asks the rebind code whether an event is being consumed; it must not be
the sole route for capture. The captured press, its echoes, and its release are all consumed.

Rules while listening:

- **Esc** cancels and leaves the binding alone. Esc can never be bound; it is how you get out.
- **Delete** or **Backspace** clears the binding. The podium button still opens the recap,
  which is why leaving the mod with no hotkey is safe, and the label stays clickable while
  unbound so a key can be set again.
- A bare modifier (Shift, Ctrl, Alt, Meta) is ignored and listening continues, because
  polling a modifier on its own fires constantly.
- Any other key binds, including keys the game uses. See the trade-off below.
- Listening stops on Esc, when the panel closes, or on actual focus loss. It does not try to
  infer a cancellation from every click elsewhere in the recap.

### Keys the game already uses

Binding the recap to a key the game uses is allowed and not warned about. Our poll is
independent of the game's input handling, so both actions run: the key does its game job
*and* toggles the recap.

Detecting the clash would mean reading the game's whole remappable list, whose name differs
per branch — the exact coupling this design set out to avoid, to prevent a problem the
player creates deliberately at a "press a key" prompt. If it turns out to confuse people,
the cheap fix is a line of help text, not conflict detection.

### The name in the rest of the UI

`RecapUi.HotkeyName` and `RecapPanel.IdleHint` are `const` today, and `TopBarButton.Title`
is a compile-time concatenation of the mod name and `"F8"` used for the podium tooltip and
its hover card. All three become computed from the current binding, and when unbound the
title is the mod name alone with no bracket.

The podium tooltip is created each time the player hovers the button. `TopBarButton.Title`
therefore stays a computed property and `GameTip()`/the fallback tooltip read it when they
are constructed. No long-lived event subscription or stale tooltip object is needed. The
recap panel similarly reads the binding when it restores its idle hint.

### Status label wart

The status label is currently sticky: after an export it reads `Saved to your Steam
screenshots` until the panel is rebuilt. Once it doubles as a control that must show the
current key, it has to return to the idle hint, so transient messages gain a short timeout
and revert. That is a small fix to code this change touches, not a separate piece of work.

## What is not covered

Controller and Steam Deck players are unaffected: they open the recap with the podium button
and move around with the bumpers, and there is no controller binding to change. The rebind
control is mouse-and-keyboard only.

Clearing the binding is included because collisions with other mods are what prompted this,
and it costs one key press in code that has to handle key presses anyway. It can be dropped
without affecting anything else.

## Error handling

Every part of this is optional decoration on a stats mod, so nothing here may take the recap
down with it. Settings load and save are wrapped and fall back to F8. The rebind handler is
wrapped the way `PadInput` already wraps its own input handling, which turns itself off for
the session after a failure and logs one line. A failed save still applies the binding for
the current session.

## Testing

`Core/Settings` is plain C# and gets unit tests in the existing runner, alongside the
`DataFolderMove` tests: defaults when the file is missing, a round trip, malformed JSON, the
unbound value, and that an unrecognised key name is stored verbatim rather than rewritten.

The fallback for an unrecognised name lives in `UI/HotkeyBinding`, not `Core`, because
deciding a name is not a key needs Godot's `Key` enum. It is checked in the preview, not the
unit tests.

The key conversion and the UI need the game, so validate them manually on both supported
branches. The existing dev preview opens panels and injects controller events, but does not
currently emit the status label's `GuiInput` or keyboard events; it is not evidence for this
feature unless it is deliberately extended in a later task.

## Files

| File | Change |
|---|---|
| `src/WhoCarried/Core/Settings.cs` | New. The record, load, save, defaults. |
| `src/WhoCarried/Core/WhoCarriedJson.cs` | Register `Settings` with the serializer context. |
| `src/WhoCarried/UI/HotkeyBinding.cs` | New. String ↔ `Key`, the current binding, save, and release suppression. |
| `src/WhoCarried/UI/HotkeyRebind.cs` | New. The press-a-key prompt: focus, capture, the rules above. |
| `src/WhoCarried/UI/RecapUi.cs` | Poll the bound key; `HotkeyName` becomes computed. |
| `src/WhoCarried/UI/RecapPanel.cs` | Status label becomes clickable; idle hint computed; status reverts. |
| `src/WhoCarried/UI/PadInput.cs` | Give the rebind first look at key events. |
| `src/WhoCarried/UI/TopBarButton.cs` | `Title` computed when a tooltip is created. |
| `tests/WhoCarried.Tests/SettingsTests.cs` | New. |
| `README.md` | Mention that the key can be changed. |
