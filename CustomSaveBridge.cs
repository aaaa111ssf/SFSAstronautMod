using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SFS.Builds;
using SFS.Parts;
using SFS.World;
using UnityEngine;

namespace AstronautMod
{
    /// Optional bridge to the "Custom Save Data" dependency mod.
    public static class CustomSaveBridge
    {
        // 实例标识复用 CrewPersistence 的命名 保证被一视同仁地持久化
        public const string IdVariable = CrewPersistence.IdVariable;
        public const string BlueprintKey = "astronautmod_crew";
        public const string WorldFile = "AstronautMod.json";

        const string EntrypointTypeName = "CustomSaveData.Entrypoint, CustomSaveData";

        public class CrewEntry
        {
            public int capacity = 1;
            public List<string> crew = new List<string>();
        }

        public class CrewSnapshot
        {
            public int version = 1;
            public Dictionary<string, CrewEntry> parts = new Dictionary<string, CrewEntry>();
        }

        static bool subscribed;
        static bool unavailable;
        static int idCounter;

        // 反射缓存（订阅成功时初始化）
        static MethodInfo bpAdd, bpRemove, bpGetGeneric, wsAdd, wsRemove, wsGetGeneric;

        /// <summary>Whether the Custom Save Data dependency is usable right now.</summary>
        public static bool Available
        {
            get { return subscribed; }
        }

        /// <summary>Called every frame; subscribes once the dependency mod has finished loading.</summary>
        public static void Ensure()
        {
            if (subscribed || unavailable)
                return;

            // 双保险：即使 Subscribe 内部出现意料之外的类型加载失败，
            // 也在这里捕获并置 unavailable，保证绝不每帧重试刷日志。
            try
            {
                Subscribe();
            }
            catch (TypeLoadException)
            {
                unavailable = true;
            }
            catch (FileNotFoundException)
            {
                unavailable = true;
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Custom Save Data subscribe", e);
                unavailable = true;
            }
        }

        static void Subscribe()
        {
            Type entrypointType = Type.GetType(EntrypointTypeName);
            if (entrypointType == null)
            {
                unavailable = true; // 依赖模组未安装 本桥接永久停用
                return;
            }

            const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            object main = GetStaticProperty(entrypointType, "Main");
            if (main == null)
                return; // 依赖模组尚未完成 Early_Load 下一帧再试

            object blueprintHelper = GetStaticProperty(entrypointType, "BlueprintHelper");
            object worldSaveHelper = GetStaticProperty(entrypointType, "WorldSaveHelper");
            if (blueprintHelper == null || worldSaveHelper == null)
            {
                unavailable = true;
                return;
            }

            Type blueprintHelperType = blueprintHelper.GetType();
            Type worldSaveHelperType = worldSaveHelper.GetType();

            // 事件代理：Action<CustomBlueprint> / Action<CustomBlueprint, Rocket[], Part[]> /
            // Action<CustomWorldSave> 全部由 DynamicMethod 生成 与本模组无编译期耦合
            SubscribeEvent(blueprintHelperType, blueprintHelper, "OnSave", nameof(OnBlueprintSaveProxy));
            SubscribeEvent(blueprintHelperType, blueprintHelper, "OnLoad", nameof(OnBlueprintLoadProxy));
            SubscribeEvent(blueprintHelperType, blueprintHelper, "OnLaunch", nameof(OnBlueprintLaunchProxy));
            SubscribeEvent(worldSaveHelperType, worldSaveHelper, "OnSave", nameof(OnWorldSaveProxy));
            SubscribeEvent(worldSaveHelperType, worldSaveHelper, "OnLoad", nameof(OnWorldLoadProxy));

            // 缓存 CustomBlueprint / CustomWorldSave 上的 AddCustomData / GetCustomData / RemoveCustomData
            Type blueprintType = AccessTools.TypeByName("CustomSaveData.CustomBlueprint");
            Type worldSaveType = AccessTools.TypeByName("CustomSaveData.CustomWorldSave");
            if (blueprintType != null)
            {
                bpAdd = AccessTools.Method(blueprintType, "AddCustomData");
                bpRemove = AccessTools.Method(blueprintType, "RemoveCustomData");
                bpGetGeneric = AccessTools.Method(blueprintType, "GetCustomData");
            }
            if (worldSaveType != null)
            {
                wsAdd = AccessTools.Method(worldSaveType, "AddCustomData");
                wsRemove = AccessTools.Method(worldSaveType, "RemoveCustomData");
                wsGetGeneric = AccessTools.Method(worldSaveType, "GetCustomData");
            }

            subscribed = true;
            ModLogger.Info("Custom Save Data bridge enabled");
        }

        static object GetStaticProperty(Type type, string name)
        {
            try
            {
                PropertyInfo prop = type.GetProperty(name,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                return prop != null ? prop.GetValue(null, null) : null;
            }
            catch
            {
                return null;
            }
        }

        static void SubscribeEvent(Type helperType, object helper, string eventName, string proxyName)
        {
            try
            {
                EventInfo evt = helperType.GetEvent(eventName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (evt == null)
                    return;

                MethodInfo handler = typeof(CustomSaveBridge).GetMethod(proxyName,
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (handler == null)
                    return;

                evt.AddEventHandler(helper, CreateProxy(evt.EventHandlerType, handler));
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Custom Save Data event " + eventName, e);
            }
        }

    /// 生成与依赖模组事件签名完全匹配的静态代理委托：
        static Delegate CreateProxy(Type delegateType, MethodInfo handler)
        {
            MethodInfo invoke = delegateType.GetMethod("Invoke");
            ParameterInfo[] pars = invoke.GetParameters();
            Type[] paramTypes = new Type[pars.Length];
            for (int i = 0; i < pars.Length; i++)
                paramTypes[i] = pars[i].ParameterType;

            DynamicMethod dm = new DynamicMethod(
                "AstronautBridge_" + handler.Name,
                invoke.ReturnType, paramTypes,
                typeof(CustomSaveBridge).Module, skipVisibility: true);

            ILGenerator il = dm.GetILGenerator();
            for (int i = 0; i < paramTypes.Length; i++)
            {
                il.Emit(OpCodes.Ldarg, (short)i);
                if (paramTypes[i].IsValueType)
                    il.Emit(OpCodes.Box, paramTypes[i]);
            }
            il.Emit(OpCodes.Call, handler);
            il.Emit(OpCodes.Ret);

            return dm.CreateDelegate(delegateType);
        }

        // ---------------------------------------------------------------- identity

        /// <summary>Returns the stable per-part-instance id, creating it on first use.</summary>
        static string GetPartId(Part part)
        {
            string existing = CrewPersistence.Read(part, IdVariable);
            if (!string.IsNullOrEmpty(existing))
                return existing;

            idCounter++;
            string id = part.name + "#" + DateTime.UtcNow.Ticks.ToString("x") + "#" + idCounter;
            CrewPersistence.Write(part, IdVariable, id);
            return id;
        }

        static CrewEntry BuildEntry(CrewModule crew)
        {
            CrewEntry entry = new CrewEntry();
            List<string> names = new List<string>();
            CrewModule.Seat[] seats = crew.seats ?? new CrewModule.Seat[0];
            foreach (CrewModule.Seat seat in seats)
            {
                names.Add(seat != null && seat.astronaut != null ? seat.astronaut.Value ?? "" : "");
            }
            entry.capacity = seats.Length;
            entry.crew = names;
            return entry;
        }

        static CrewSnapshot SnapshotOf(IEnumerable<Part> parts)
        {
            CrewSnapshot snapshot = new CrewSnapshot();
            if (parts == null)
                return snapshot;

            foreach (Part part in parts)
            {
                if (part == null) continue;
                if (!AstronautModMain.injectedPartIds.Contains(part.GetInstanceID())) continue;

                CrewModule crew = part.GetComponentInChildren<CrewModule>(true);
                if (crew == null) continue;

                snapshot.parts[GetPartId(part)] = BuildEntry(crew);
            }
            return snapshot;
        }

        /// <summary>Applies recovered data to parts whose own variables came back empty.</summary>
        static void ApplySnapshot(IEnumerable<Part> parts, CrewSnapshot snapshot)
        {
            if (snapshot == null || snapshot.parts == null || parts == null)
                return;

            foreach (Part part in parts)
            {
                if (part == null) continue;
                // 部件自身数据完整时无需回写
                if (CrewPersistence.HasStoredConfig(part)) continue;

                string existingId = CrewPersistence.Read(part, IdVariable);
                if (string.IsNullOrEmpty(existingId)) continue;

                CrewEntry entry;
                if (!snapshot.parts.TryGetValue(existingId, out entry) || entry == null)
                    continue;

                CrewPersistence.Import(part, entry.capacity, entry.crew);

                if (part.GetComponentInChildren<CrewModule>(true) != null)
                    AstronautModMain.ReimportCrewFromVariables(part);
                else
                {
                    AstronautModMain.InjectCrewModule(part);
                    AstronautModMain.ClearModuleCache(part);
                }
            }
        }

        // ---------------------------------------------------------------- custom data helpers

        static void AddCustomData(object container, string key, CrewSnapshot snapshot)
        {
            // blueprint 与 world save 的 AddCustomData(string, object) 签名一致 按容器实际类型分派
            MethodInfo mi = (container.GetType().Name == "CustomWorldSave") ? wsAdd : bpAdd;
            if (mi == null) return;
            mi.Invoke(container, new object[] { key, snapshot });
        }

        static void RemoveCustomData(object container, string key)
        {
            MethodInfo mi = (container.GetType().Name == "CustomWorldSave") ? wsRemove : bpRemove;
            if (mi == null) return;
            mi.Invoke(container, new object[] { key });
        }

        static bool TryGetCustomData(object container, string key, out CrewSnapshot snapshot)
        {
            snapshot = null;
            MethodInfo generic = (container.GetType().Name == "CustomWorldSave") ? wsGetGeneric : bpGetGeneric;
            if (generic == null) return false;
            try
            {
                MethodInfo closed = generic.MakeGenericMethod(typeof(CrewSnapshot));
                object[] args = { key, null };
                bool ok = (bool)closed.Invoke(container, args);
                if (ok) snapshot = args[1] as CrewSnapshot;
                return ok && snapshot != null;
            }
            catch
            {
                return false;
            }
        }

        // ---------------------------------------------------------------- event proxies

        static void OnBlueprintSaveProxy(object blueprint)
        {
            try
            {
                Part[] parts = null;
                if (BuildManager.main != null && BuildManager.main.buildGrid != null)
                    parts = BuildManager.main.buildGrid.activeGrid.partsHolder.parts.ToArray();

                CrewSnapshot snapshot = SnapshotOf(parts);
                if (snapshot.parts.Count == 0)
                {
                    RemoveCustomData(blueprint, BlueprintKey);
                    return;
                }
                AddCustomData(blueprint, BlueprintKey, snapshot);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Custom blueprint save", e);
            }
        }

        static void OnBlueprintLoadProxy(object blueprint)
        {
            try
            {
                CrewSnapshot snapshot;
                if (!TryGetCustomData(blueprint, BlueprintKey, out snapshot))
                    return;

                Part[] parts = null;
                if (BuildManager.main != null && BuildManager.main.buildGrid != null)
                    parts = BuildManager.main.buildGrid.activeGrid.partsHolder.parts.ToArray();

                ApplySnapshot(parts, snapshot);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Custom blueprint load", e);
            }
        }

        static void OnBlueprintLaunchProxy(object blueprint, object rockets, object parts)
        {
            try
            {
                CrewSnapshot snapshot;
                if (!TryGetCustomData(blueprint, BlueprintKey, out snapshot))
                    return;
                ApplySnapshot(parts as Part[], snapshot);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Custom blueprint launch", e);
            }
        }

        // ---------------------------------------------------------------- world

        static CrewSnapshot lastWorldSnapshot;

        static void OnWorldSaveProxy(object worldSave)
        {
            try
            {
                List<Part> parts = new List<Part>();
                CrewModule[] crews = UnityEngine.Object.FindObjectsOfType<CrewModule>(true);
                foreach (CrewModule crew in crews)
                {
                    if (crew == null) continue;
                    Part part = Traverse.Create(crew).Field("part").GetValue<Part>();
                    if (part != null && AstronautModMain.injectedPartIds.Contains(part.GetInstanceID()))
                        parts.Add(part);
                }

                CrewSnapshot snapshot = SnapshotOf(parts);
                if (snapshot.parts.Count > 0)
                    AddCustomData(worldSave, WorldFile, snapshot);

                lastWorldSnapshot = snapshot;
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Custom world save", e);
            }
        }

        static void OnWorldLoadProxy(object worldSave)
        {
            try
            {
                CrewSnapshot snapshot;
                if (worldSave != null && TryGetCustomData(worldSave, WorldFile, out snapshot))
                    lastWorldSnapshot = snapshot;
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Custom world load", e);
            }
        }

        /// <summary>Called after a world has been rebuilt (launch / revert) as a last-resort recovery.</summary>
        public static void RecoverAfterWorldLoad()
        {
            try
            {
                if (!subscribed || lastWorldSnapshot == null)
                    return;

                List<Part> parts = new List<Part>();
                CrewModule[] crews = UnityEngine.Object.FindObjectsOfType<CrewModule>(true);
                foreach (CrewModule crew in crews)
                {
                    if (crew == null) continue;
                    Part part = Traverse.Create(crew).Field("part").GetValue<Part>();
                    if (part != null) parts.Add(part);
                }
                ApplySnapshot(parts, lastWorldSnapshot);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("World recovery", e);
            }
        }
    }
}
