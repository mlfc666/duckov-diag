using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace DuckovDiag
{
    /// <summary>
    /// 极简、崩溃安全的日志器 + 反射工具。
    /// 每写一行都 flush，游戏原生崩溃时日志不会丢。
    /// </summary>
    internal static class Diag
    {
        private const BindingFlags All =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private static StreamWriter _writer;
        private static readonly object Lock = new object();
        private static long _seq;

        public static string LogPath { get; private set; }
        public static bool SkipModifiers { get; private set; }
        public static bool Verbose { get; private set; }

        private static FieldInfo _fTypeId;
        private static FieldInfo _fDisplayName;
        private static FieldInfo _fModifiers;
        private static FieldInfo _fList;
        private static FieldInfo _fCollection;
        private static FieldInfo _fKey;
        private static FieldInfo _fEnableInInventory;
        private static PropertyInfo _pMaster;
        private static PropertyInfo _pModifiers;
        private static PropertyInfo _pTypeId;
        private static PropertyInfo _pDisplayName;

        private static int _itemCount;
        private static int _modifierApplyCount;
        private static readonly Dictionary<int, string> SeenModifiers = new Dictionary<int, string>();

        public static void Init()
        {
            string root = ".";
            try
            {
                var dataPath = Application.dataPath;
                var parent = Path.GetDirectoryName(dataPath);
                if (!string.IsNullOrEmpty(parent)) root = parent;
            }
            catch (Exception e)
            {
                Debug.LogError("[DuckovDiag] Application.dataPath 失败: " + e);
            }

            LogPath = Path.Combine(root, "DuckovDiag.log");
            try
            {
                var fs = new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true };
            }
            catch (Exception e)
            {
                Debug.LogError("[DuckovDiag] 无法创建日志文件: " + e);
            }

            SkipModifiers = SafeExists(Path.Combine(root, "DuckovDiag.skipModifiers"));
            Verbose = SafeExists(Path.Combine(root, "DuckovDiag.verbose"));

            Write("=== DuckovDiag start " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            Write("log=" + LogPath + " | skipModifiers=" + SkipModifiers + " | verbose=" + Verbose);

            try
            {
                var itemType = typeof(ItemStatsSystem.Item);
                _fTypeId = itemType.GetField("typeID", All);
                _fDisplayName = itemType.GetField("displayName", All);
                _fModifiers = itemType.GetField("modifiers", All);
                _pModifiers = itemType.GetProperty("Modifiers", All);
                _pTypeId = itemType.GetProperty("TypeID", All);
                _pDisplayName = itemType.GetProperty("DisplayName", All);

                var collType = typeof(ItemStatsSystem.ModifierDescriptionCollection);
                _fList = collType.GetField("list", All);
                _pMaster = collType.GetProperty("Master", All);

                var mdType = typeof(ItemStatsSystem.ModifierDescription);
                _fCollection = mdType.GetField("collection", All);
                _fKey = mdType.GetField("key", All);
                _fEnableInInventory = mdType.GetField("enableInInventory", All);
            }
            catch (Exception e)
            {
                Write("反射初始化失败: " + e);
            }
        }

        private static bool SafeExists(string p)
        {
            try { return File.Exists(p); } catch { return false; }
        }

        public static void Write(string message)
        {
            lock (Lock)
            {
                _seq++;
                var line = "[" + _seq.ToString("D6") + "] " + message;
                try { _writer?.WriteLine(line); } catch { }
            }
        }

        // ---------- item helpers ----------

        public static int ItemId(ItemStatsSystem.Item item)
        {
            try
            {
                if (_pTypeId != null) return (int)_pTypeId.GetValue(item, null);
                if (_fTypeId != null) return (int)_fTypeId.GetValue(item);
            }
            catch { }
            return -1;
        }

        public static string ItemName(ItemStatsSystem.Item item)
        {
            try
            {
                object v = _pDisplayName != null ? _pDisplayName.GetValue(item, null) : _fDisplayName?.GetValue(item);
                var s = v as string;
                if (!string.IsNullOrEmpty(s)) return s;
                return item.name;
            }
            catch { return "?"; }
        }

        public static string Describe(ItemStatsSystem.Item item)
        {
            if (item == null) return "<null item>";
            try { return "#" + ItemId(item) + " '" + ItemName(item) + "'"; }
            catch { return "<broken item>"; }
        }

        public static ItemStatsSystem.ModifierDescriptionCollection GetModifiers(ItemStatsSystem.Item item)
        {
            try
            {
                if (_pModifiers != null) return (ItemStatsSystem.ModifierDescriptionCollection)_pModifiers.GetValue(item, null);
                return (ItemStatsSystem.ModifierDescriptionCollection)_fModifiers?.GetValue(item);
            }
            catch { return null; }
        }

        public static ItemStatsSystem.Item GetMaster(ItemStatsSystem.ModifierDescriptionCollection coll)
        {
            try { return (ItemStatsSystem.Item)_pMaster?.GetValue(coll, null); }
            catch { return null; }
        }

        public static IList GetModifierList(ItemStatsSystem.ModifierDescriptionCollection coll)
        {
            try { return _fList?.GetValue(coll) as IList; }
            catch { return null; }
        }

        public static string KeyOf(ItemStatsSystem.ModifierDescription md)
        {
            try
            {
                var k = _fKey?.GetValue(md) as string;
                return k ?? "<null-key>";
            }
            catch { return "<key-error>"; }
        }

        // ---------- event logging ----------

        public static void LogItemPhase(string phase, ItemStatsSystem.Item item)
        {
            if (item == null) return;
            var id = ItemId(item);
            _itemCount++;
            var mods = GetModifiers(item);
            var list = mods != null ? GetModifierList(mods) : null;
            var modCount = list?.Count ?? 0;

            Write("[ITEM] " + phase + " " + Describe(item) + " mods=" + modCount + " items=" + _itemCount);
        }

        public static void LogReapplyModifiers(ItemStatsSystem.ModifierDescriptionCollection coll)
        {
            if (coll == null) { Write("[MODS] ReapplyModifiers: collection=null"); return; }

            var master = GetMaster(coll);
            var list = GetModifierList(coll);
            if (list == null || list.Count == 0) return;   // 空词条表不记录，避免日志爆炸
            var name = Describe(master);
            Write("[MODS] ReapplyModifiers BEGIN " + name + " count=" + list.Count);

            if (list == null) { Write("[MODS] !! internal list 为 null，无法枚举"); return; }

            for (int i = 0; i < list.Count; i++)
            {
                var md = list[i] as ItemStatsSystem.ModifierDescription;
                if (md == null)
                {
                    Write("[MODS] !! [" + i + "] entry 为 null（列表被写坏）");
                    continue;
                }

                object ownerObj = null;
                try { ownerObj = _fCollection?.GetValue(md); } catch { }
                var ownerIsThis = ReferenceEquals(ownerObj, coll);
                var key = KeyOf(md);
                string sharedInfo = "";
                int h = 0;
                try { h = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(md); } catch { }

                if (h != 0)
                {
                    if (SeenModifiers.TryGetValue(h, out var where) && where != name)
                        sharedInfo = "  <== SHARED with " + where;
                    else if (!SeenModifiers.ContainsKey(h))
                        SeenModifiers[h] = name;
                }

                if (Verbose || !ownerIsThis || key == "<null-key>" || sharedInfo.Length > 0 || name.Contains("#8"))
                {
                    Write("[MODS]   [" + i + "] key=" + key +
                          " ownerCollectionIsThis=" + ownerIsThis +
                          " enableInInventory=" + SafeField(_fEnableInInventory, md) +
                          sharedInfo);
                }
            }

            Write("[MODS] ReapplyModifiers END " + name);
        }

        public static void LogReapplyModifier(ItemStatsSystem.ModifierDescription md, ItemStatsSystem.ModifierDescriptionCollection coll)
        {
            if (md == null) { Write("[MODS] ReapplyModifier: instance=null"); return; }
            _modifierApplyCount++;

            var key = KeyOf(md);
            var master = coll != null ? GetMaster(coll) : null;
            var itemId = master != null ? ItemId(master) : -1;

            if (Verbose || itemId >= 80000 || key == "<null-key>")
            {
                Write("[MODS] apply " + key + " -> " + Describe(master) + " (total applies=" + _modifierApplyCount + ")");
            }
        }

        public static void LogSlotCollection(ItemStatsSystem.Items.SlotCollection coll)
        {
            try
            {
                var f = typeof(ItemStatsSystem.Items.SlotCollection).GetField("list", All);
                var list = f?.GetValue(coll) as IList;
                if (list == null) { Write("[SLOTS] list = null"); return; }
                Write("[SLOTS] SlotCollection count=" + list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    var slot = list[i] as ItemStatsSystem.Items.Slot;
                    if (slot == null) { Write("[SLOTS]   [" + i + "] slot=null"); continue; }
                    string key = "?", content = "-";
                    try { key = slot.Key ?? "<null-key>"; } catch (Exception e) { key = "<err:" + e.GetType().Name + ">"; }
                    try
                    {
                        var c = slot.Content;
                        content = c == null ? "<null>" : Describe(c);
                    }
                    catch (Exception e) { content = "<err:" + e.GetType().Name + ">"; }
                    Write("[SLOTS]   [" + i + "] key=" + key + " content=" + content);
                }
            }
            catch (Exception e) { Write("[SLOTS] 枚举失败: " + e.GetType().Name + ": " + e.Message); }
        }

        public static void Step(string tag, ItemStatsSystem.Item item, string extra = "")
        {
            try
            {
                Write("[STEP] " + tag + " <- " + Describe(item) + (string.IsNullOrEmpty(extra) ? "" : " " + extra));
            }
            catch { }
        }

        private static string SafeField(FieldInfo f, object obj)
        {
            try { return f == null ? "?" : Convert.ToString(f.GetValue(obj)); }
            catch { return "?"; }
        }

        public static string Stats()
        {
            return "items=" + _itemCount + " modifierApplies=" + _modifierApplyCount + " distinctModifiers=" + SeenModifiers.Count;
        }
    }
}
