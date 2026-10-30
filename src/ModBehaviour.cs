using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DuckovDiag
{
    /// <summary>
    /// 入口类。文件夹、info.ini 里的 name、dll 文件名、类名都必须叫 DuckovDiag。
    /// </summary>
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private static HarmonyLib.Harmony _harmony;

        protected override void OnAfterSetup()
        {
            Diag.Init();

            try { DumpEnvironment(); }
            catch (Exception e) { Diag.Write("环境转储失败: " + e); }

            try
            {
                _harmony = new HarmonyLib.Harmony("duckov.diag");
                int ok = 0, fail = 0;
                foreach (var t in Assembly.GetExecutingAssembly().GetTypes())
                {
                    if (t.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
                    try { _harmony.CreateClassProcessor(t).Patch(); ok++; Diag.Write("patch class OK: " + t.Name); }
                    catch (Exception e) { fail++; Diag.Write("patch class FAILED: " + t.Name + " -> " + e.GetType().Name + ": " + e.Message); }
                }
                Diag.Write("补丁安装完成: ok=" + ok + " failed=" + fail);
            }
            catch (Exception e)
            {
                Diag.Write("补丁安装总异常: " + e);
                Debug.LogError("[DuckovDiag] patch install failed: " + e);
            }
        }

        protected override void OnBeforeDeactivate()
        {
            try { _harmony?.UnpatchAll("duckov.diag"); } catch { }
            Diag.Write("=== DuckovDiag 停用，" + Diag.Stats() + " ===");
        }

        // ------------------------------------------------------------------

        private static void DumpEnvironment()
        {
            Diag.Write("游戏版本=" + Safe(() => Application.version) +
                       " unity=" + Safe(() => Application.unityVersion) +
                       " platform=" + Safe(() => Application.platform.ToString()));

            var asms = AppDomain.CurrentDomain.GetAssemblies();

            // 1) 列出所有 mod / 补丁相关程序集
            Diag.Write("---- 已加载程序集（mod / Harmony / MonoMod）----");
            foreach (var a in asms)
            {
                string name;
                try { name = a.GetName().Name; } catch { continue; }
                if (name == null) continue;
                string loc = "";
                try { loc = a.Location ?? ""; } catch { }

                bool interesting = name.IndexOf("Harmony", StringComparison.OrdinalIgnoreCase) >= 0
                                   || name.IndexOf("MonoMod", StringComparison.OrdinalIgnoreCase) >= 0
                                   || name.StartsWith("DuckovDiag")
                                   || loc.IndexOf("workshop", StringComparison.OrdinalIgnoreCase) >= 0
                                   || loc.IndexOf("\\Mods\\", StringComparison.OrdinalIgnoreCase) >= 0
                                   || loc.IndexOf("/Mods/", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!interesting) continue;

                string ver = "";
                try { ver = a.GetName().Version?.ToString() ?? ""; } catch { }
                Diag.Write("  asm " + name + " v" + ver + "  <- " + Shorten(loc));
            }

            // 2) 统计 Harmony 副本数量（多个副本 = 多套 detour 引擎，是崩溃高危信号）
            var harmonyAsms = asms.Where(a =>
            {
                try { return a.GetName().Name == "0Harmony"; } catch { return false; }
            }).ToList();
            Diag.Write("---- Harmony 副本数量: " + harmonyAsms.Count + " ----");
            foreach (var ha in harmonyAsms)
            {
                string loc = "";
                try { loc = ha.Location ?? ""; } catch { }
                string ver = "";
                try { ver = ha.GetName().Version?.ToString() ?? ""; } catch { }
                Diag.Write("  Harmony v" + ver + "  <- " + Shorten(loc));
            }

            // 3) 每个 Harmony 副本的补丁表
            foreach (var ha in harmonyAsms)
            {
                try { DumpHarmonyPatches(ha); }
                catch (Exception e) { Diag.Write("  补丁表转储失败(" + Safe(() => ha.GetName().Name) + "): " + e.Message); }
            }

            // 4) 当前启用的 mod
            try
            {
                var coreAsm = asms.FirstOrDefault(a => { try { return a.GetName().Name == "TeamSoda.Duckov.Core"; } catch { return false; } });
                var mm = coreAsm?.GetType("Duckov.Modding.ModManager");
                var getList = mm?.GetMethod("GetActiveModList", BindingFlags.Public | BindingFlags.Static);
                if (getList != null)
                {
                    var list = getList.Invoke(null, null) as IEnumerable;
                    if (list != null)
                    {
                        var names = new List<string>();
                        foreach (var o in list) names.Add(Convert.ToString(o));
                        Diag.Write("---- 启用中的 mod(" + names.Count + "): " + string.Join(", ", names) + " ----");
                    }
                }
            }
            catch (Exception e) { Diag.Write("读取启用 mod 列表失败: " + e.Message); }

            Diag.Write("--------------------------------------------------");
        }

        private static void DumpHarmonyPatches(Assembly harmonyAsm)
        {
            var t = harmonyAsm.GetType("HarmonyLib.Harmony");
            if (t == null) { Diag.Write("  无法找到 HarmonyLib.Harmony"); return; }

            var getAll = t.GetMethod("GetAllPatchedMethods", BindingFlags.Public | BindingFlags.Static);
            var getInfo = t.GetMethod("GetPatchInfo", new[] { typeof(MethodBase) });
            if (getAll == null || getInfo == null) { Diag.Write("  Harmony API 不匹配，跳过"); return; }

            var methods = getAll.Invoke(null, null) as IEnumerable;
            if (methods == null) { Diag.Write("  GetAllPatchedMethods 返回 null"); return; }

            int total = 0;
            foreach (var m in methods)
            {
                var mb = m as MethodBase;
                if (mb == null) continue;
                total++;

                string owners = "";
                string counts = "";
                try
                {
                    var pi = getInfo.Invoke(null, new object[] { mb });
                    if (pi != null)
                    {
                        owners = OwnersOf(pi);
                        counts = CountsOf(pi);
                    }
                }
                catch { }

                string typeName = mb.DeclaringType != null ? mb.DeclaringType.FullName : "?";
                Diag.Write("  PATCH " + typeName + "::" + mb.Name + "  owners=[" + owners + "] " + counts);
            }
            Diag.Write("  (该 Harmony 副本共 " + total + " 个被补丁方法)");
        }

        private static string OwnersOf(object patchInfo)
        {
            try
            {
                var pi = patchInfo.GetType();
                var prop = pi.GetProperty("Owners", BindingFlags.Public | BindingFlags.Instance);
                var val = prop?.GetValue(patchInfo, null) ?? pi.GetField("owners", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(patchInfo);
                if (val is IEnumerable en)
                {
                    var names = new List<string>();
                    foreach (var p in en)
                    {
                        if (p == null) continue;
                        var pt = p.GetType();
                        var f = pt.GetField("owner", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        var pr = pt.GetProperty("owner", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        var o = f != null ? f.GetValue(p) : pr?.GetValue(p, null);
                        names.Add(Convert.ToString(o));
                    }
                    return string.Join(", ", names.Distinct());
                }
            }
            catch { }
            return "?";
        }

        private static string CountsOf(object patchInfo)
        {
            try
            {
                var t = patchInfo.GetType();
                string Get(string n)
                {
                    var p = t.GetProperty(n, BindingFlags.Public | BindingFlags.Instance);
                    var v = p?.GetValue(patchInfo, null);
                    return v == null ? "-" : Convert.ToString(v);
                }
                return "prefixes=" + Get("Prefixes") + " postfixes=" + Get("Postfixes") +
                       " transpilers=" + Get("Transpilers") + " finalizers=" + Get("Finalizers");
            }
            catch { return ""; }
        }

        private static string Shorten(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var i = s.IndexOf("3167020", StringComparison.Ordinal);
            if (i >= 0) return "..." + s.Substring(i);
            i = s.IndexOf("Escape from Duckov", StringComparison.Ordinal);
            if (i >= 0) return "..." + s.Substring(i);
            return s;
        }

        private static string Safe(Func<string> f)
        {
            try { return f() ?? ""; } catch { return "?"; }
        }
    }
}
