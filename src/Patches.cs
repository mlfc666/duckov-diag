using System;
using HarmonyLib;
using ItemStatsSystem;

namespace DuckovDiag
{
    [HarmonyPatch(typeof(Item), "Awake")]
    internal static class Patch_Item_Awake
    {
        private static void Prefix(Item __instance)
        {
            try { Diag.LogItemPhase("Awake", __instance); }
            catch (Exception e) { Diag.Write("[ITEM] Awake prefix 异常: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Item), "Initialize")]
    internal static class Patch_Item_Initialize
    {
        private static void Prefix(Item __instance)
        {
            try { Diag.LogItemPhase("Initialize.BEGIN", __instance); }
            catch (Exception e) { Diag.Write("[ITEM] Initialize prefix 异常: " + e.Message); }
        }

        private static void Postfix(Item __instance)
        {
            try { Diag.LogItemPhase("Initialize.END", __instance); }
            catch (Exception e) { Diag.Write("[ITEM] Initialize postfix 异常: " + e.Message); }
        }
    }

    /// <summary>Item.Initialize 内部依次调用各 ItemComponent.Initialize，这里逐步打点。</summary>
    [HarmonyPatch(typeof(ItemComponent), "Initialize")]
    internal static class Patch_ItemComponent_Initialize
    {
        private static void Prefix(ItemComponent __instance)
        {
            try
            {
                if (__instance == null) { Diag.Write("[STEP] component=null"); return; }
                var master = __instance.Master;
                Diag.Step("component.Initialize:" + __instance.GetType().Name, master);
            }
            catch (Exception e) { Diag.Write("[STEP] component.Initialize 异常: " + e.Message); }
        }
    }

    /// <summary>
    /// 崩溃点：Item.Initialize -> ModifierDescriptionCollection.ReapplyModifiers
    /// Prefix 只读地枚举内部 list；发现 null / 跨物品共享 / key 为空会明确打出来。
    /// 若游戏根目录存在 DuckovDiag.skipModifiers，则跳过原方法。
    /// </summary>
    [HarmonyPatch(typeof(ModifierDescriptionCollection), "ReapplyModifiers")]
    internal static class Patch_ReapplyModifiers
    {
        private static bool Prefix(ModifierDescriptionCollection __instance)
        {
            try { Diag.LogReapplyModifiers(__instance); }
            catch (Exception e) { Diag.Write("[MODS] ReapplyModifiers prefix 异常: " + e.Message); }

            if (Diag.SkipModifiers)
            {
                Diag.Write("[MODS] skipModifiers 已启用，跳过原方法");
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ModifierDescription), "ReapplyModifier")]
    internal static class Patch_ReapplyModifier
    {
        private static void Prefix(ModifierDescription __instance, ModifierDescriptionCollection collection)
        {
            try { Diag.LogReapplyModifier(__instance, collection); }
            catch (Exception e) { Diag.Write("[MODS] ReapplyModifier prefix 异常: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(ItemComponent), "Initialize")]
    internal static class Patch_ItemComponent_Initialize_End
    {
        private static void Postfix(ItemComponent __instance)
        {
            try { Diag.Write("[STEP] component.Initialize.END:" + (__instance?.GetType().Name ?? "null")); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(ItemStatsSystem.Items.SlotCollection), "OnInitialize")]
    internal static class Patch_SlotCollection_OnInitialize
    {
        private static void Prefix(ItemStatsSystem.Items.SlotCollection __instance)
        {
            try
            {
                Diag.Write("[SLOTS] SlotCollection.OnInitialize BEGIN");
                Diag.LogSlotCollection(__instance);
            }
            catch (Exception e) { Diag.Write("[SLOTS] prefix 异常: " + e.Message); }
        }

        private static void Postfix(ItemStatsSystem.Items.SlotCollection __instance)
        {
            try { Diag.Write("[SLOTS] SlotCollection.OnInitialize END"); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(ItemStatsSystem.Items.Slot), "Initialize")]
    internal static class Patch_Slot_Initialize
    {
        private static void Postfix(ItemStatsSystem.Items.Slot __instance)
        {
            try
            {
                string key = "?";
                try { key = __instance?.Key ?? "<null>"; } catch { }
                Diag.Write("[SLOTS] slot.Initialize done key=" + key);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Item), "HandleEffectsActive")]
    internal static class Patch_Item_HandleEffectsActive
    {
        private static void Prefix(Item __instance)
        {
            try { Diag.Step("HandleEffectsActive", __instance); }
            catch { }
        }
    }

    // ---------------- 战斗相关（崩溃高发点） ----------------

    [HarmonyPatch(typeof(CharacterMainControl), "OnDead")]
    internal static class Patch_CharacterMainControl_OnDead
    {
        private static void Prefix(CharacterMainControl __instance)
        {
            try
            {
                string name = "?";
                try { name = __instance != null ? __instance.name : "null"; } catch { }
                Diag.Write("[COMBAT] OnDead " + name + "  " + Diag.Stats());
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(ItemAgent_Gun), "TransToFire")]
    internal static class Patch_ItemAgent_Gun_TransToFire
    {
        private static void Prefix(ItemAgent_Gun __instance)
        {
            try
            {
                string name = "?";
                try { name = __instance != null ? __instance.name : "null"; } catch { }
                Diag.Write("[COMBAT] TransToFire " + name + "  " + Diag.Stats());
            }
            catch { }
        }
    }
}
