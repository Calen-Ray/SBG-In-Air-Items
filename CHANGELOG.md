# Changelog

## v0.2.1

- **Actually install the postfix.** v0.1.0 / v0.2.0 placed `[HarmonyPatch]` on the postfix
  method, but `Harmony.PatchAll()` scans **types** — so the method-level attribute was
  invisible and the postfix never ran in either earlier release. Wrapped in the standard
  nested-class pattern used by the other mods. The deferred-select / reflection-fallback
  body from 0.2.0 is unchanged; it just gets to run now.

## v0.2.0

- Defer the auto-select by one frame so it lands after `SetIsInSpringBootsJump(true)`
  instead of mid-`TriggerJumpInternal`, where vanilla state-machine ordering caused the
  select to silently no-op for some users.
- If `TrySelectItemSlot` keeps refusing (up to 3 attempts), fall back to writing the
  `EquippedItemIndex` backing field directly + telling `PlayerInfo` to update its hand
  visuals. Guarantees the slot does switch even if a vanilla gate gets in the way.
- Add `Diagnostics.VerboseLogging` (default false) so future failures can be diagnosed
  remotely without re-instrumenting the source.

## v0.1.0

- Initial release. Auto-select the next available inventory item on Spring Boots activation.
