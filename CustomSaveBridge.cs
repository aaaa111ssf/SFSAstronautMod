using System;
using System.Collections.Generic;
using HarmonyLib;
using SFS.Builds;
using SFS.Parts;
using SFS.World;
using UnityEngine;

namespace AstronautUnlocker
{
    /// <summary>
    /// Optional bridge to the "Custom Save Data" dependency mod.
    ///
    /// The primary fix relies on the part's own variables (see <see cref="CrewPersistence"/>), which
    /// already travels inside blueprints, rocket saves and world saves. This bridge adds the requested
    /// redundancy: every mod capsule also writes its crew layout into the blueprint's custom data and
    /// into the world save's custom data. If anything upstream fails to restore a part, the data can be
    /// recovered from there.
    ///
    /// Everything here is defensive: when Custom Save Data is not installed the bridge simply does
    /// nothing and the mod keeps working.
    /// </summary>
    public static class CustomSaveBridge
    {
        // 实例标识复用 CrewPersistence 的命名 保证被一视同仁地持久化
        public const string IdVariable = CrewPersistence.IdVariable;
        public const string BlueprintKey = "astronautmod_crew";
        public const string WorldFile = "AstronautMod.json";

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
            Subscribe();
        }

        static bool IsDependencyPresent()
        {
            try
            {
                return Type.GetType("CustomSaveData.Entrypoint, CustomSaveData") != null;
            }
            catch
            {
                return false;
            }
        }

        static void Subscribe()
        {
            try
            {
                if (!IsDependencyPresent())
                    return;

                if (CustomSaveData.Entrypoint.Main == null)
                    return; // 依赖模组尚未完成 Early_Load 下一帧再试

                CustomSaveData.Entrypoint.BlueprintHelper.OnSave += OnBlueprintSave;
                CustomSaveData.Entrypoint.BlueprintHelper.OnLoad += OnBlueprintLoad;
                CustomSaveData.Entrypoint.BlueprintHelper.OnLaunch += OnBlueprintLaunch;
                CustomSaveData.Entrypoint.WorldSaveHelper.OnSave += OnWorldSave;
                CustomSaveData.Entrypoint.WorldSaveHelper.OnLoad += OnWorldLoad;

                subscribed = true;
                ModLogger.Info("Custom Save Data bridge enabled");
            }
            catch (TypeLoadException)
            {
                unavailable = true;
            }
            catch (System.IO.FileNotFoundException)
            {
                unavailable = true;
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Custom Save Data subscribe", e);
                unavailable = true;
            }
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
                if (!AstronautUnlockerMod.injectedPartIds.Contains(part.GetInstanceID())) continue;

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
                    AstronautUnlockerMod.ReimportCrewFromVariables(part);
                else
                {
                    AstronautUnlockerMod.InjectCrewModule(part);
                    AstronautUnlockerMod.ClearModuleCache(part);
                }
            }
        }

        // ---------------------------------------------------------------- blueprint

        static void OnBlueprintSave(CustomSaveData.CustomBlueprint blueprint)
        {
            try
            {
                Part[] parts = null;
                if (BuildManager.main != null && BuildManager.main.buildGrid != null)
                    parts = BuildManager.main.buildGrid.activeGrid.partsHolder.parts.ToArray();

                CrewSnapshot snapshot = SnapshotOf(parts);
                if (snapshot.parts.Count == 0)
                {
                    blueprint.RemoveCustomData(BlueprintKey);
                    return;
                }
                blueprint.AddCustomData(BlueprintKey, snapshot);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Custom blueprint save", e);
            }
        }

        static void OnBlueprintLoad(CustomSaveData.CustomBlueprint blueprint)
        {
            try
            {
                CrewSnapshot snapshot;
                if (!blueprint.GetCustomData(BlueprintKey, out snapshot))
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

        static void OnBlueprintLaunch(CustomSaveData.CustomBlueprint blueprint, Rocket[] rockets, Part[] parts)
        {
            try
            {
                CrewSnapshot snapshot;
                if (!blueprint.GetCustomData(BlueprintKey, out snapshot))
                    return;
                ApplySnapshot(parts, snapshot);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Custom blueprint launch", e);
            }
        }

        // ---------------------------------------------------------------- world

        static CrewSnapshot lastWorldSnapshot;

        static void OnWorldSave(CustomSaveData.CustomWorldSave save)
        {
            try
            {
                List<Part> parts = new List<Part>();
                CrewModule[] crews = UnityEngine.Object.FindObjectsOfType<CrewModule>(true);
                foreach (CrewModule crew in crews)
                {
                    if (crew == null) continue;
                    Part part = Traverse.Create(crew).Field("part").GetValue<Part>();
                    if (part != null && AstronautUnlockerMod.injectedPartIds.Contains(part.GetInstanceID()))
                        parts.Add(part);
                }

                CrewSnapshot snapshot = SnapshotOf(parts);
                if (snapshot.parts.Count > 0)
                    save.AddCustomData(WorldFile, snapshot);

                lastWorldSnapshot = snapshot;
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Custom world save", e);
            }
        }

        static void OnWorldLoad(CustomSaveData.CustomWorldSave save)
        {
            try
            {
                CrewSnapshot snapshot;
                if (save != null && save.GetCustomData(WorldFile, out snapshot))
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

        static IEnumerable<Part> FindSceneParts()
        {
            return UnityEngine.Object.FindObjectsOfType<Part>(true);
        }
    }
}
