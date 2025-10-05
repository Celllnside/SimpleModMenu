using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System;
using System.Reflection;

namespace InfRefreshes
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Core : BasePlugin
    {
        public const string PluginGuid = "com.cellinside.infrefreshes";
        public const string PluginName = "InfRefreshes";
        public const string PluginVersion = "1.3.0"; // stronger enforcement

        private Harmony _harmony;

        public override void Load()
        {
            _harmony = new Harmony(PluginGuid);
            try
            {
                InfiniteRefreshesPatches.Apply(_harmony, Log);
                Log.LogInfo("Infinite refreshes patch applied (reinforced).");
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to apply infinite refresh patches: {ex}");
            }
        }
    }

    internal static class InfiniteRefreshesPatches
    {
        private const int InfiniteRefreshValue = 10000;

        private static MethodInfo _setRefreshes;
        private static MethodInfo _setRefreshesUsed;

        public static void Apply(Harmony harmony, BepInEx.Logging.ManualLogSource log)
        {
            var playerInventoryType = AccessTools.TypeByName("PlayerInventory");
            if (playerInventoryType == null)
                throw new InvalidOperationException("Type 'PlayerInventory' not found. Cannot apply infinite refresh patches.");

            // Cache setters (not patched) so we can force values via reflection.
            _setRefreshes = AccessTools.Method(playerInventoryType, "set_refreshes");
            _setRefreshesUsed = AccessTools.Method(playerInventoryType, "set_refreshesUsed");

            if (_setRefreshes == null)
                log.LogWarning("Setter set_refreshes not found.");
            if (_setRefreshesUsed == null)
                log.LogWarning("Setter set_refreshesUsed not found.");

            var getRefreshes = AccessTools.Method(playerInventoryType, "get_refreshes");
            if (getRefreshes != null)
                harmony.Patch(getRefreshes, postfix: new HarmonyMethod(typeof(InfiniteRefreshesPatches), nameof(GetRefreshesPostfix)));
            else
                log.LogWarning("Method get_refreshes not found on PlayerInventory.");

            var getRefreshesUsed = AccessTools.Method(playerInventoryType, "get_refreshesUsed");
            if (getRefreshesUsed != null)
                harmony.Patch(getRefreshesUsed, postfix: new HarmonyMethod(typeof(InfiniteRefreshesPatches), nameof(GetRefreshesUsedPostfix)));
            else
                log.LogWarning("Method get_refreshesUsed not found on PlayerInventory.");

            // Also patch Update method (runs every frame) to hard-reset counts even if game code manipulates fields directly.
            var update = AccessTools.Method(playerInventoryType, "Update");
            if (update != null)
                harmony.Patch(update, postfix: new HarmonyMethod(typeof(InfiniteRefreshesPatches), nameof(UpdatePostfix)));
            else
                log.LogWarning("PlayerInventory.Update not found; relying on getter patches only.");
        }

        // Enforce in getter so any UI read corrects value.
        public static void GetRefreshesPostfix(object __instance, ref int __result)
        {
            if (__result < InfiniteRefreshValue)
            {
                ForceSet(__instance, InfiniteRefreshValue, 0);
                __result = InfiniteRefreshValue;
            }
        }

        public static void GetRefreshesUsedPostfix(object __instance, ref int __result)
        {
            if (__result != 0)
            {
                ForceSet(__instance, InfiniteRefreshValue, 0);
                __result = 0;
            }
        }

        // Every frame ensure values are fixed (covers direct field writes / increments bypassing accessors)
        public static void UpdatePostfix(object __instance)
        {
            ForceSet(__instance, InfiniteRefreshValue, 0);
        }

        private static void ForceSet(object instance, int refreshes, int used)
        {
            try
            {
                if (_setRefreshes != null)
                    _setRefreshes.Invoke(instance, new object[] { refreshes });
                if (_setRefreshesUsed != null)
                    _setRefreshesUsed.Invoke(instance, new object[] { used });
            }
            catch { /* swallow to avoid spam */ }
        }
    }
}