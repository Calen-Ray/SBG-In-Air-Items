using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace InAirItems
{
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string ModGuid = "sbg.inairitems";
        public const string ModName = "InAirItems";
        public const string ModVersion = "0.2.2";

        internal static ManualLogSource Log;
        internal static Plugin Instance;
        internal ConfigEntry<bool> verboseLoggingConfig;

        private static MethodInfo getEffectiveSlotMethod;
        private static FieldInfo equippedItemIndexBackingField;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            verboseLoggingConfig = Config.Bind(
                "Diagnostics",
                "VerboseLogging",
                false,
                "Emit diagnostic lines when boots fire, what next slot we tried to select, and whether vanilla TrySelectItemSlot accepted. Off by default; flip on if the auto-select still misbehaves.");

            getEffectiveSlotMethod = AccessTools.Method(typeof(PlayerInventory), "GetEffectiveSlot");
            equippedItemIndexBackingField = AccessTools.Field(typeof(PlayerInventory), "<EquippedItemIndex>k__BackingField");

            new Harmony(ModGuid).PatchAll();
            Log.LogInfo($"{ModName} v{ModVersion} loaded.");
        }

        // The vanilla flow: Use button with SpringBoots equipped → PlayerMovement.TryTriggerJump
        // → PlayerInventory.TryUseSpringBoots → coroutine starts (sets IsUsingSpringBoots, calls
        // DecrementUseFromSlotAt synchronously) → returns true.
        //
        // 0.1.0 called TrySelectItemSlot directly in this postfix and 0.2.0 added a deferred
        // retry — neither shipped a working patch because the [HarmonyPatch] attribute was on
        // the postfix METHOD instead of an enclosing TYPE. Harmony.PatchAll scans types for the
        // attribute, so the method-level decoration was invisible and the postfix never ran.
        // 0.2.1 wraps the postfix in the standard nested-class pattern used by the other mods.
        [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.TryUseSpringBoots))]
        internal static class Patch_PlayerInventory_TryUseSpringBoots
        {
            private static void Postfix(PlayerInventory __instance, bool __result)
            {
                if (!__result || __instance == null || !__instance.isLocalPlayer)
                    return;

                int currentIndex = __instance.EquippedItemIndex;
                int nextIndex = FindNextUsableSlot(__instance, currentIndex);

                if (Instance != null && Instance.verboseLoggingConfig.Value)
                    Log?.LogInfo($"InAirItems: boots fired (currentIndex={currentIndex}); next-usable-slot={nextIndex}.");

                if (nextIndex < 0 || nextIndex == currentIndex)
                    return;

                if (Instance != null)
                    Instance.StartCoroutine(DeferredSelect(__instance, currentIndex, nextIndex));
            }
        }

        private static IEnumerator DeferredSelect(PlayerInventory inventory, int bootsIndex, int nextIndex)
        {
            yield return null; // wait one frame so the jump is fully launched

            for (int attempt = 0; attempt < 3 && inventory != null; attempt++)
            {
                if (inventory.EquippedItemIndex != bootsIndex)
                {
                    // Something else already changed the selection; bail.
                    LogVerbose($"InAirItems: equipped index changed to {inventory.EquippedItemIndex} before our select; skipping.");
                    yield break;
                }

                bool ok = inventory.TrySelectItemSlot(nextIndex, fromForcedHotkeySelect: false);
                LogVerbose($"InAirItems: TrySelectItemSlot({nextIndex}) attempt {attempt + 1} -> {ok}.");
                if (ok)
                    yield break;

                yield return null;
            }

            // Final fallback: bypass the gate and write the index directly. We still want the
            // visual switchers to update, so call the same side-effect set vanilla does after
            // assigning the field.
            if (inventory != null && inventory.EquippedItemIndex == bootsIndex && equippedItemIndexBackingField != null)
            {
                LogVerbose($"InAirItems: TrySelectItemSlot kept refusing; writing EquippedItemIndex = {nextIndex} directly.");
                equippedItemIndexBackingField.SetValue(inventory, nextIndex);
                inventory.PlayerInfo?.SetEquippedItemIndex(nextIndex);
            }
        }

        private static int FindNextUsableSlot(PlayerInventory inventory, int startIndex)
        {
            if (GameManager.PlayerInventorySettings == null)
                return -1;
            int max = GameManager.PlayerInventorySettings.MaxItems;
            if (max <= 0)
                return -1;

            for (int offset = 1; offset <= max; offset++)
            {
                int idx = (startIndex + offset) % max;
                if (idx == startIndex)
                    continue;

                InventorySlot slot;
                try
                {
                    slot = (InventorySlot)getEffectiveSlotMethod.Invoke(inventory, new object[] { idx });
                }
                catch
                {
                    continue;
                }
                if (slot.itemType == ItemType.None || slot.itemType == ItemType.SpringBoots)
                    continue;
                if (slot.remainingUses <= 0)
                    continue;
                return idx;
            }
            return -1;
        }

        private static void LogVerbose(string message)
        {
            if (Instance != null && Instance.verboseLoggingConfig.Value)
                Log?.LogInfo(message);
        }
    }
}
