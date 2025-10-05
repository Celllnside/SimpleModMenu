using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;

namespace SimpleModMenu
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Core : BasePlugin
    {
        public const string PluginGuid = "com.cellinside.SimpleModMenu";
        public const string PluginName = "SimpleModMenu";
        public const string PluginVersion = "1.6.7";

        internal static Core Instance { get; private set; }

        internal Harmony _harmony;
        private bool _patched;

        private ConfigEntry<bool> _aggressiveMode;
        private ConfigEntry<bool> _debugLogging;
        private ConfigEntry<bool> _enableInfinite;
        private ConfigEntry<int> _forcedRefreshConfig;
        private ConfigEntry<bool> _enableInfiniteBanishes;
        private ConfigEntry<bool> _enableInfiniteSkips;

        internal bool AggressiveMode { get => _aggressiveMode.Value; set { if (_aggressiveMode.Value != value) { _aggressiveMode.Value = value; Config.Save(); } } }
        internal bool DebugLogging { get => _debugLogging.Value; set { if (_debugLogging.Value != value) { _debugLogging.Value = value; Config.Save(); } } }
        internal bool EnableInfinite { get => _enableInfinite.Value; set { if (_enableInfinite.Value != value) { _enableInfinite.Value = value; Config.Save(); } } }
        internal bool EnableInfiniteBanishes { get => _enableInfiniteBanishes?.Value ?? false; set { if (_enableInfiniteBanishes != null && _enableInfiniteBanishes.Value != value) { _enableInfiniteBanishes.Value = value; Config.Save(); } } }
        internal bool EnableInfiniteSkips { get => _enableInfiniteSkips?.Value ?? false; set { if (_enableInfiniteSkips != null && _enableInfiniteSkips.Value != value) { _enableInfiniteSkips.Value = value; Config.Save(); } } }

        internal int GenericPatchCount { get; private set; }
        internal int EShopItemPatchCount { get; private set; }
        internal int PlayerInventoryPatchCount { get; private set; }

        internal static readonly System.Collections.Generic.List<string> PatchedMethodNames = new();

        private const string EShopItemFullName = "Assets.Scripts._Data.ShopItems.EShopItem";
        private Type _eShopItemType;

        private const string PlayerInventoryTypeName = "PlayerInventory";
        public static int ForcedRefreshValue = 9999;

        internal void SetForcedRefreshValue(int value)
        {
            if (value < 1) value = 1;
            if (value > 1000000) value = 1000000;
            ForcedRefreshValue = value;
            if (_forcedRefreshConfig.Value != value)
            {
                _forcedRefreshConfig.Value = value;
                Config.Save();
            }
        }

        public override void Load()
        {
            Instance = this;
            _harmony = new Harmony(PluginGuid);
            BindConfig();
            ForcedRefreshValue = _forcedRefreshConfig.Value;
            TryApplyPatches();
            if (!_patched)
            {
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
                Log.LogInfo("SimpleModMenu waiting for Assembly-Csharp to load...");
            }
            Log.LogInfo("SimpleModMenu initialized.");

            try
            {
                if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(GuiController)))
                {
                    ClassInjector.RegisterTypeInIl2Cpp<GuiController>();
                    Log.LogInfo("Registered GuiController in Il2Cpp domain.");
                }
                var go = new GameObject("SimpleModMenuGUI");
                go.hideFlags = HideFlags.HideAndDontSave;
                go.AddComponent<GuiController>();
                UnityEngine.Object.DontDestroyOnLoad(go);
                Log.LogInfo("GuiController GameObject created. Press F6 (or F7) to toggle panel.");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Failed to create GUI GameObject: {ex.Message}");
            }
        }

        public override bool Unload()
        {
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            _harmony?.UnpatchSelf();
            return base.Unload();
        }

        private void BindConfig()
        {
            _enableInfinite = Config.Bind("General", "EnableInfinite", true, "Master toggle for forcing infinite refreshes.");
            _enableInfiniteBanishes = Config.Bind("General", "EnableInfiniteBanishes", true, "Toggle for infinite banishes.");
            _enableInfiniteSkips = Config.Bind("General", "EnableInfiniteSkips", true, "Toggle for infinite skips.");
            _forcedRefreshConfig = Config.Bind("General", "ForcedRefreshValue", 9999, "Forced refresh/banish/skip count value when infinite enabled.");
            _aggressiveMode = Config.Bind("General", "AggressiveMode", true, "If true, patch ALL methods with EShopItem param to force infinite Refresh (may be overkill). If false, only name-matched consumption methods are skipped.");
            _debugLogging = Config.Bind("General", "DebugLogging", false, "Enable verbose logging of patch decisions and runtime interceptions.");
        }

        private void OnAssemblyLoad(object sender, AssemblyLoadEventArgs e)
        {
            if (_patched) return;
            if (e.LoadedAssembly?.GetName().Name == "Assembly-CSharp") TryApplyPatches();
        }

        private Assembly GetGameAssembly() => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");

        internal void ReapplyPatches()
        {
            try
            {
                _harmony?.UnpatchSelf();
                _patched = false;
                GenericPatchCount = EShopItemPatchCount = PlayerInventoryPatchCount = 0;
                TryApplyPatches();
            }
            catch (Exception ex) { Log.LogError($"ReapplyPatches failed: {ex}"); }
        }

        private void TryApplyPatches()
        {
            try
            {
                var gameAsm = GetGameAssembly();
                if (gameAsm == null) { if (_debugLogging.Value) Log.LogDebug("Assembly-Csharp not loaded yet."); return; }

                var playerInventoryType = gameAsm.GetTypes().FirstOrDefault(t => t.Name == PlayerInventoryTypeName || t.FullName == PlayerInventoryTypeName);

                _eShopItemType = gameAsm.GetType(EShopItemFullName);
                if (_eShopItemType == null) Log.LogWarning($"Could not locate enum type {EShopItemFullName}. Will still apply generic patches.");
                else if (!_eShopItemType.IsEnum) { Log.LogWarning($"Type {EShopItemFullName} found but is not an enum."); _eShopItemType = null; }

                int genericPatchCount = 0;
                int eShopItemPatchCount = 0;
                int playerInventoryPatchCount = 0;
                PatchedMethodNames.Clear();

                foreach (var type in gameAsm.GetTypes())
                {
                    MethodInfo[] methods; try { methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static); } catch { continue; }

                    foreach (var m in methods)
                    {
                        try
                        {
                            if (m.DeclaringType == playerInventoryType) continue;

                            if (m.ReturnType == typeof(bool) && m.GetParameters().Length == 0)
                            {
                                var nameLower = m.Name.ToLowerInvariant();
                                if (nameLower.Contains("canrefresh") || nameLower.Contains("hasrefresh") || nameLower == "canrefresh")
                                {
                                    PatchOnce(m, null, null, typeof(AlwaysTruePatch), nameof(AlwaysTruePatch.Postfix));
                                    genericPatchCount++; PatchedMethodNames.Add($"TRUE  {m.DeclaringType?.Name}.{m.Name}()");
                                }
                            }
                            if (m.ReturnType == typeof(void) && m.GetParameters().Length == 0)
                            {
                                var nameLower = m.Name.ToLowerInvariant();
                                if (nameLower.Contains("userefresh") || nameLower.Contains("consumerefresh") || nameLower.Contains("spendrefresh"))
                                {
                                    PatchOnce(m, typeof(SkipMethodPatch), nameof(SkipMethodPatch.Prefix));
                                    genericPatchCount++; PatchedMethodNames.Add($"SKIP  {m.DeclaringType?.Name}.{m.Name}()");
                                }
                            }
                        }
                        catch (Exception ex) { if (_debugLogging.Value) Log.LogDebug($"Generic patch attempt failed for {m.DeclaringType?.FullName}.{m.Name}: {ex.Message}"); }

                        if (_eShopItemType != null)
                        {
                            var parameters = m.GetParameters();
                            if (parameters.Length > 0 && parameters.Any(p => p.ParameterType == _eShopItemType))
                            {
                                try
                                {
                                    if (m.ReturnType == typeof(int)) { PatchOnce(m, null, null, typeof(EShopItemIntResultPatch), nameof(EShopItemIntResultPatch.Postfix)); eShopItemPatchCount++; PatchedMethodNames.Add($"ESHOP->INT {m.DeclaringType?.Name}.{m.Name}"); }
                                    else if (m.ReturnType == typeof(bool)) { PatchOnce(m, null, null, typeof(EShopItemBoolResultPatch), nameof(EShopItemBoolResultPatch.Postfix)); eShopItemPatchCount++; PatchedMethodNames.Add($"ESHOP->BOOL {m.DeclaringType?.Name}.{m.Name}"); }
                                    else if (m.ReturnType == typeof(void))
                                    {
                                        bool shouldPatch = _aggressiveMode.Value;
                                        if (!shouldPatch)
                                        {
                                            var lname = m.Name.ToLowerInvariant();
                                            if (lname.Contains("use") || lname.Contains("consume") || lname.Contains("spend") || lname.Contains("take") || lname.Contains("apply") || lname.Contains("add")) shouldPatch = true;
                                        }
                                        if (shouldPatch) { PatchOnce(m, typeof(EShopItemUsePatch), nameof(EShopItemUsePatch.Prefix)); eShopItemPatchCount++; PatchedMethodNames.Add($"ESHOP->SKIP {m.DeclaringType?.Name}.{m.Name}"); }
                                    }
                                }
                                catch (Exception ex) { if (_debugLogging.Value) Log.LogDebug($"EShopItem patch attempt failed for {m.DeclaringType?.FullName}.{m.Name}: {ex.Message}"); }
                            }
                        }
                    }
                }

                if (playerInventoryType != null)
                {
                    try
                    {
                        PlayerInventoryForcePatches.Init(playerInventoryType, Log);
                        bool any = false;
                        var getR = AccessTools.Method(playerInventoryType, "get_refreshes");
                        if (getR != null)
                        {
                            _harmony.Patch(getR, postfix: new HarmonyMethod(typeof(PlayerInventoryForcePatches), nameof(PlayerInventoryForcePatches.GetRefreshesPostfix)));
                            playerInventoryPatchCount++; PatchedMethodNames.Add("PINV get_refreshes"); any = true;
                        }
                        var getRU = AccessTools.Method(playerInventoryType, "get_refreshesUsed");
                        if (getRU != null)
                        {
                            _harmony.Patch(getRU, postfix: new HarmonyMethod(typeof(PlayerInventoryForcePatches), nameof(PlayerInventoryForcePatches.GetRefreshesUsedPostfix)));
                            playerInventoryPatchCount++; PatchedMethodNames.Add("PINV get_refreshesUsed"); any = true;
                        }
                        var upd = AccessTools.Method(playerInventoryType, "Update");
                        if (upd != null)
                        {
                            _harmony.Patch(upd, postfix: new HarmonyMethod(typeof(PlayerInventoryForcePatches), nameof(PlayerInventoryForcePatches.UpdatePostfix)));
                            playerInventoryPatchCount++; PatchedMethodNames.Add("PINV Update"); any = true;
                        }
                        if (!any) Log.LogWarning("PlayerInventory found but expected methods (get_refreshes / get_refreshesUsed / Update) missing.");
                    }
                    catch (Exception ex) { Log.LogWarning($"Failed PlayerInventory force patches: {ex.Message}"); }
                }
                else Log.LogWarning("PlayerInventory type not found. Provide fully qualified name if namespaced.");

                Log.LogInfo($"SimpleModMenu: Applied {genericPatchCount} generic, {eShopItemPatchCount} EShopItem-targeted, {playerInventoryPatchCount} PlayerInventory-specific patches. AggressiveMode={_aggressiveMode.Value}");
                _patched = true;

                GenericPatchCount = genericPatchCount;
                EShopItemPatchCount = eShopItemPatchCount;
                PlayerInventoryPatchCount = playerInventoryPatchCount;

                if (genericPatchCount + eShopItemPatchCount + playerInventoryPatchCount == 0) Log.LogWarning("SimpleModMenu: No candidate methods found. Need concrete method names.");
            }
            catch (Exception ex) { Log.LogError($"SimpleModMenu patching failed: {ex}"); }
        }

        private void PatchOnce(MethodInfo target, Type prefix = null, string prefixName = null, Type postfix = null, string postfixName = null)
        {
            try
            {
                HarmonyMethod pre = prefix != null ? new HarmonyMethod(prefix.GetMethod(prefixName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)) : null;
                HarmonyMethod post = postfix != null ? new HarmonyMethod(postfix.GetMethod(postfixName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)) : null;
                _harmony.Patch(target, pre, post);
                if (_debugLogging.Value) Log.LogDebug($"Patched {target.DeclaringType?.FullName}.{target.Name} (pre={(pre != null)}, post={(post != null)})");
            }
            catch (Exception ex) { if (_debugLogging.Value) Log.LogDebug($"Failed to patch {target.DeclaringType?.FullName}.{target.Name}: {ex.Message}"); }
        }

        internal static bool ArgIsRefresh(object arg)
        { if (arg == null) return false; var t = arg.GetType(); if (!t.IsEnum) return false; if (t.FullName != EShopItemFullName) return false; try { return Convert.ToInt32(arg) == 0; } catch { return false; } }

        internal static void DebugLogInvoke(string where, object[] args)
        { var self = BepInEx.Logging.Logger.CreateLogSource(PluginName); try { if (args != null && args.Any(ArgIsRefresh)) self.LogDebug($"{where}: invoked with Refresh"); } catch { } }

        // Helper reflection caches for resource operations
        private MethodInfo _miChangeGold;
        private MethodInfo _miAddXp;
        private FieldInfo _fiPlayerHealth;
        private object _cachedPlayerHealth;
        private MemberInfo _miHealthValue;
        private MemberInfo _miMaxHealthValue;
        private MethodInfo _miHealLike;

        private object GetPlayerInventoryInstance() => PlayerInventoryForcePatches.LastInstance; // re-enabled instance capture

        internal bool TryAddGold(int amount)
        {
            try
            {
                var inv = GetPlayerInventoryInstance(); if (inv == null) return false;
                _miChangeGold ??= inv.GetType().GetMethod("ChangeGold", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
                if (_miChangeGold == null) return false;
                _miChangeGold.Invoke(inv, new object[] { amount });
                return true;
            }
            catch (Exception ex) { if (_debugLogging?.Value == true) Log.LogDebug($"AddGold failed: {ex.Message}"); return false; }
        }

        internal bool TryAddXp(int amount)
        {
            try
            {
                var inv = GetPlayerInventoryInstance(); if (inv == null) return false;
                _miAddXp ??= inv.GetType().GetMethod("AddXp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
                if (_miAddXp == null) return false;
                _miAddXp.Invoke(inv, new object[] { amount });
                return true;
            }
            catch (Exception ex) { if (_debugLogging?.Value == true) Log.LogDebug($"AddXp failed: {ex.Message}"); return false; }
        }

        internal bool TryAddHealth(int amount)
        {
            try
            {
                var inv = GetPlayerInventoryInstance(); if (inv == null) return false;
                _fiPlayerHealth ??= inv.GetType().GetField("playerHealth", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (_fiPlayerHealth == null) return false;
                _cachedPlayerHealth = _fiPlayerHealth.GetValue(inv);
                if (_cachedPlayerHealth == null) return false;

                if (_miHealthValue == null)
                {
                    var hType = _cachedPlayerHealth.GetType();
                    string[] names = { "health", "currentHealth", "_health", "hp", "_hp" };
                    foreach (var n in names)
                    {
                        var f = hType.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (f != null && (f.FieldType == typeof(int) || f.FieldType == typeof(float))) { _miHealthValue = f; break; }
                        var p = hType.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (p != null && p.CanRead && p.CanWrite && (p.PropertyType == typeof(int) || p.PropertyType == typeof(float))) { _miHealthValue = p; break; }
                    }
                    string[] maxNames = { "maxHealth", "_maxHealth", "healthMax", "maxHP" };
                    foreach (var n in maxNames)
                    {
                        var f = hType.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (f != null && (f.FieldType == typeof(int) || f.FieldType == typeof(float))) { _miMaxHealthValue = f; break; }
                        var p = hType.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (p != null && p.CanRead && (p.PropertyType == typeof(int) || p.PropertyType == typeof(float))) { _miMaxHealthValue = p; break; }
                    }
                    _miHealLike = hType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(int) &&
                            (m.Name.IndexOf("heal", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("addhealth", StringComparison.OrdinalIgnoreCase) >= 0));
                }

                if (_miHealLike != null)
                {
                    _miHealLike.Invoke(_cachedPlayerHealth, new object[] { amount });
                    return true;
                }
                if (_miHealthValue != null)
                {
                    float cur = GetNumeric(_miHealthValue, _cachedPlayerHealth);
                    float max = (_miMaxHealthValue != null) ? GetNumeric(_miMaxHealthValue, _cachedPlayerHealth) : float.MaxValue;
                    float next = cur + amount;
                    if (next > max) next = max;
                    SetNumeric(_miHealthValue, _cachedPlayerHealth, next);
                    return true;
                }
                return false;
            }
            catch (Exception ex) { if (_debugLogging?.Value == true) Log.LogDebug($"AddHealth failed: {ex.Message}"); return false; }
        }

        private static float GetNumeric(MemberInfo mi, object obj)
        {
            return mi switch
            {
                FieldInfo f when f.FieldType == typeof(int) => (int)f.GetValue(obj),
                FieldInfo f when f.FieldType == typeof(float) => (float)f.GetValue(obj),
                PropertyInfo p when p.PropertyType == typeof(int) => (int)p.GetValue(obj),
                PropertyInfo p when p.PropertyType == typeof(float) => (float)p.GetValue(obj),
                _ => 0f
            };
        }
        private static void SetNumeric(MemberInfo mi, object obj, float value)
        {
            switch (mi)
            {
                case FieldInfo f when f.FieldType == typeof(int): f.SetValue(obj, (int)value); break;
                case FieldInfo f when f.FieldType == typeof(float): f.SetValue(obj, value); break;
                case PropertyInfo p when p.PropertyType == typeof(int): p.SetValue(obj, (int)value); break;
                case PropertyInfo p when p.PropertyType == typeof(float): p.SetValue(obj, value); break;
            }
        }
    }

    internal static class AlwaysTruePatch { public static void Postfix(ref bool __result) { var core = Core.Instance; if (core != null && core.EnableInfinite) __result = true; } }
    internal static class SkipMethodPatch { public static bool Prefix() { var core = Core.Instance; return !(core != null && core.EnableInfinite); } }
    internal static class EShopItemIntResultPatch { public static void Postfix(object[] __args, ref int __result) { var core = Core.Instance; if (core == null || !core.EnableInfinite) return; if (__args == null) return; if (__args.Any(Core.ArgIsRefresh)) __result = Core.ForcedRefreshValue; } }
    internal static class EShopItemBoolResultPatch { public static void Postfix(object[] __args, ref bool __result) { var core = Core.Instance; if (core == null || !core.EnableInfinite) return; if (__args == null) return; if (__args.Any(Core.ArgIsRefresh)) __result = true; } }
    internal static class EShopItemUsePatch { public static bool Prefix(object[] __args) { var core = Core.Instance; if (core == null || !core.EnableInfinite) return true; if (__args == null) return true; if (__args.Any(Core.ArgIsRefresh)) return false; return true; } }

    internal static class PlayerInventoryForcePatches
    {
        internal static void Init(Type playerInventoryType, BepInEx.Logging.ManualLogSource log)
        {
            if (_initialized) return;
            _log = log;
            _setRefreshes = AccessTools.Method(playerInventoryType, "set_refreshes");
            _setRefreshesUsed = AccessTools.Method(playerInventoryType, "set_refreshesUsed");
            _setBanishes = AccessTools.Method(playerInventoryType, "set_banishes");
            _setBanishesUsed = AccessTools.Method(playerInventoryType, "set_banishesUsed");
            _setSkips = AccessTools.Method(playerInventoryType, "set_skips");
            _setSkipsUsed = AccessTools.Method(playerInventoryType, "set_skipsUsed");
            _getRefreshes = AccessTools.Method(playerInventoryType, "get_refreshes");
            _getRefreshesUsed = AccessTools.Method(playerInventoryType, "get_refreshesUsed");
            _getBanishes = AccessTools.Method(playerInventoryType, "get_banishes");
            _getBanishesUsed = AccessTools.Method(playerInventoryType, "get_banishesUsed");
            _getSkips = AccessTools.Method(playerInventoryType, "get_skips");
            _getSkipsUsed = AccessTools.Method(playerInventoryType, "get_skipsUsed");
            _fieldRefreshes = playerInventoryType.GetField("refreshes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _fieldRefreshesUsed = playerInventoryType.GetField("refreshesUsed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _fieldBanishes = playerInventoryType.GetField("banishes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _fieldBanishesUsed = playerInventoryType.GetField("banishesUsed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _fieldSkips = playerInventoryType.GetField("skips", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _fieldSkipsUsed = playerInventoryType.GetField("skipsUsed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _initialized = true;
        }

        private static MethodInfo _setRefreshes;
        private static MethodInfo _setRefreshesUsed;
        private static MethodInfo _setBanishes; private static MethodInfo _setBanishesUsed; private static MethodInfo _setSkips; private static MethodInfo _setSkipsUsed;
        private static MethodInfo _getRefreshes; private static MethodInfo _getRefreshesUsed; private static MethodInfo _getBanishes; private static MethodInfo _getBanishesUsed; private static MethodInfo _getSkips; private static MethodInfo _getSkipsUsed;
        private static FieldInfo _fieldRefreshes, _fieldRefreshesUsed, _fieldBanishes, _fieldBanishesUsed, _fieldSkips, _fieldSkipsUsed;
        private static bool _initialized;
        private static BepInEx.Logging.ManualLogSource _log;

        private class SavedCounts { public int refreshes; public int refreshesUsed; public int banishes; public int banishesUsed; public int skips; public int skipsUsed; public bool captured; }
        private static readonly System.Collections.Generic.Dictionary<IntPtr, SavedCounts> _original = new();

        private static bool EnabledRefresh => Core.Instance != null && Core.Instance.EnableInfinite;
        private static bool EnabledBanish => Core.Instance != null && Core.Instance.EnableInfiniteBanishes;
        private static bool EnabledSkip => Core.Instance != null && Core.Instance.EnableInfiniteSkips;

        private static bool _lastRefresh, _lastBanish, _lastSkip;
        internal static object LastInstance { get; private set; }

        public static void GetRefreshesPostfix(object __instance, ref int __result)
        { if (!EnabledRefresh) return; if (__result < Core.ForcedRefreshValue) { ForceSet(__instance, Core.ForcedRefreshValue, 0); __result = Core.ForcedRefreshValue; } }

        public static void GetRefreshesUsedPostfix(object __instance, ref int __result)
        { if (!EnabledRefresh) return; if (__result != 0) { ForceSet(__instance, Core.ForcedRefreshValue, 0); __result = 0; } }

        public static void UpdatePostfix(object __instance)
        {
            if (__instance == null) return;
            LastInstance = __instance; // capture latest inventory instance for resource buttons
            IntPtr key;
            try { dynamic d = __instance; key = (IntPtr)d.Pointer; if (key == IntPtr.Zero) key = (IntPtr)__instance.GetHashCode(); }
            catch { key = (IntPtr)__instance.GetHashCode(); }

            if (!_original.TryGetValue(key, out var saved) || !saved.captured)
            {
                saved = new SavedCounts
                {
                    refreshes = SafeGet(_getRefreshes, __instance),
                    refreshesUsed = SafeGet(_getRefreshesUsed, __instance),
                    banishes = SafeGet(_getBanishes, __instance),
                    banishesUsed = SafeGet(_getBanishesUsed, __instance),
                    skips = SafeGet(_getSkips, __instance),
                    skipsUsed = SafeGet(_getSkipsUsed, __instance),
                    captured = true
                };
                _original[key] = saved;
            }

            if (_lastRefresh && !EnabledRefresh) ForceSet(__instance, saved.refreshes, saved.refreshesUsed);
            if (_lastBanish && !EnabledBanish) ForceSetBanishes(__instance, saved.banishes, saved.banishesUsed);
            if (_lastSkip && !EnabledSkip) ForceSetSkips(__instance, saved.skips, saved.skipsUsed);

            if (EnabledRefresh) ForceSet(__instance, Core.ForcedRefreshValue, 0);
            if (EnabledBanish) ForceSetBanishes(__instance, Core.ForcedRefreshValue, 0);
            if (EnabledSkip) ForceSetSkips(__instance, Core.ForcedRefreshValue, 0);

            _lastRefresh = EnabledRefresh; _lastBanish = EnabledBanish; _lastSkip = EnabledSkip;
        }

        private static int SafeGet(MethodInfo mi, object inst)
        { if (mi == null) return 0; try { var v = mi.Invoke(inst, null); if (v is int i) return i; } catch { } return 0; }

        private static void ForceSet(object instance, int refreshes, int refreshesUsed)
        {
            try
            {
                bool usedSetter = false;
                if (_setRefreshes != null) { _setRefreshes.Invoke(instance, new object[] { refreshes }); usedSetter = true; }
                if (_setRefreshesUsed != null) { _setRefreshesUsed.Invoke(instance, new object[] { refreshesUsed }); usedSetter = true; }
                if (!usedSetter)
                {
                    _fieldRefreshes?.SetValue(instance, refreshes);
                    _fieldRefreshesUsed?.SetValue(instance, refreshesUsed);
                }
            }
            catch { }
        }
        private static void ForceSetBanishes(object instance, int value, int used)
        {
            try
            {
                bool usedSetter = false;
                if (_setBanishes != null) { _setBanishes.Invoke(instance, new object[] { value }); usedSetter = true; }
                if (_setBanishesUsed != null) { _setBanishesUsed.Invoke(instance, new object[] { used }); usedSetter = true; }
                if (!usedSetter)
                {
                    _fieldBanishes?.SetValue(instance, value);
                    _fieldBanishesUsed?.SetValue(instance, used);
                }
            }
            catch { }
        }
        private static void ForceSetSkips(object instance, int value, int used)
        {
            try
            {
                bool usedSetter = false;
                if (_setSkips != null) { _setSkips.Invoke(instance, new object[] { value }); usedSetter = true; }
                if (_setSkipsUsed != null) { _setSkipsUsed.Invoke(instance, new object[] { used }); usedSetter = true; }
                if (!usedSetter)
                {
                    _fieldSkips?.SetValue(instance, value);
                    _fieldSkipsUsed?.SetValue(instance, used);
                }
            }
            catch { }
        }
    }
}
