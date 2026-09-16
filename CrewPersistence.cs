using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SFS.Parts;
using SFS.Variables;
using SFS.World;

namespace AstronautUnlocker
{
    /// <summary>
    /// Per-part persistence for the CrewModule that this mod injects into custom capsules.
    ///
    /// Problem this solves:
    /// The injected CrewModule and its seats are created at runtime, so they are not part of the
    /// part prefab and never reach <see cref="SFS.Parts.PartSave"/>. That is why the mod used to keep
    /// its state in a global JSON keyed by <c>part.name</c> plus volatile Unity instance IDs, which
    /// broke on launch, on every revert and after restarting the game.
    ///
    /// Solution:
    /// Every injected part gets its own string variables inside its native <c>VariablesModule</c>
    /// (marked <c>save = true</c>). Those are picked up automatically by the vanilla pipeline:
    /// blueprint -> PartSave.TEXT_VARIABLES -> PartsLoader -> new Part. Nothing has to be re-matched
    /// by part name or instance id any more, so blueprints, rocket saves, world saves, quick saves
    /// and "revert to launch / 30 sec / 3 min" all restore the crew reliably.
    ///
    /// Variable layout (all inside TEXT_VARIABLES):
    ///   AstronautMod_Cap   -> crew capacity of this part instance
    ///   AstronautMod_Seat0 -> astronaut name of seat 0 ("" == empty seat)
    ///   AstronautMod_Seat1 -> ... and so on
    /// </summary>
    public static class CrewPersistence
    {
        public const string CapacityVariable = "AstronautMod_Cap";
        public const string SeatPrefix = "AstronautMod_Seat";
        public const string IdVariable = "AstronautMod_Id";

        /// <summary>True when the given variable name belongs to this mod.</summary>
        public static bool IsOwnVariable(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (name == CapacityVariable || name == IdVariable)
                return true;
            if (name.Length <= SeatPrefix.Length)
                return false;
            if (!name.StartsWith(SeatPrefix, StringComparison.Ordinal))
                return false;
            for (int i = SeatPrefix.Length; i < name.Length; i++)
            {
                if (name[i] < '0' || name[i] > '9')
                    return false;
            }
            return true;
        }

        public static string SeatVariable(int index)
        {
            return SeatPrefix + index;
        }

        static VariableList<string> GetList(Part part)
        {
            if (part == null)
                return null;
            VariablesModule module = part.variablesModule;
            return module != null ? module.stringVariables : null;
        }

        // ---------------------------------------------------------------- installation

        /// <summary>
        /// Patches <c>VariableList&lt;string&gt;.LoadDictionary</c> so that variables belonging to this mod
        /// are re-created before a saved part is restored. PartsLoader loads with
        /// addMissingVariables = (false, false), which silently drops unknown variables, so without
        /// this the mod's values would never make it back into a freshly created part.
        /// </summary>
        public static void Install(Harmony harmony)
        {
            try
            {
                Type generic = null;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type type = assembly.GetType("SFS.Variables.VariableList`1");
                    if (type != null)
                    {
                        generic = type;
                        break;
                    }
                }
                if (generic == null)
                {
                    ModLogger.Warning("CrewPersistence: variable list API not found");
                    return;
                }

                Type concrete = generic.MakeGenericType(typeof(string));
                MethodInfo loadDictionary = AccessTools.Method(concrete, "LoadDictionary");
                if (loadDictionary == null)
                {
                    ModLogger.Warning("CrewPersistence: LoadDictionary not found");
                    return;
                }

                harmony.Patch(loadDictionary,
                    new HarmonyMethod(typeof(CrewPersistence).GetMethod(nameof(LoadDictionary_Prefix))));
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("CrewPersistence install", e);
            }
        }

        /// <summary>Harmony prefix for <c>VariableList&lt;string&gt;.LoadDictionary</c>.</summary>
        public static void LoadDictionary_Prefix(object __instance, Dictionary<string, string> inputs)
        {
            try
            {
                EnsureVariablesFor(__instance as VariableList<string>, inputs);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("CrewPersistence load dictionary", e);
            }
        }

        static void EnsureVariablesFor(VariableList<string> list, Dictionary<string, string> inputs)
        {
            if (list == null || inputs == null)
                return;
            bool added = false;
            foreach (KeyValuePair<string, string> entry in inputs)
            {
                if (!IsOwnVariable(entry.Key) || list.Has(entry.Key))
                    continue;
                AddVariable(list, entry.Key, true);
                added = true;
            }
            if (added)
                InvalidateCache(list);
        }

        // ---------------------------------------------------------------- raw access

        static void AddVariable(VariableList<string> list, string name, bool save)
        {
            list.saves.Add(new VariableSave(name) { save = save });
        }

        static void InvalidateCache(VariableList<string> list)
        {
            try
            {
                FieldInfo field = typeof(VariableList<string>)
                    .GetField("_variables", BindingFlags.Instance | BindingFlags.NonPublic);
                field?.SetValue(list, null);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("CrewPersistence cache", e);
            }
        }

        /// <summary>Creates the variable if needed and returns a reference that is bound to it.</summary>
        public static String_Reference Bind(Part part, string variableName)
        {
            VariableList<string> list = GetList(part);
            if (list == null)
            {
                // No variables module: fall back to a detached local value like before.
                return new String_Reference();
            }

            if (!list.Has(variableName))
            {
                AddVariable(list, variableName, true);
                InvalidateCache(list);
            }

            String_Reference reference = new String_Reference();
            SetVariableName(reference, variableName);
            reference.referenceToVariables = part.variablesModule;
            return reference;
        }

        static void SetVariableName(String_Reference reference, string variableName)
        {
            try
            {
                for (Type type = typeof(String_Reference); type != null; type = type.BaseType)
                {
                    FieldInfo field = type.GetField("variableName",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (field != null)
                    {
                        field.SetValue(reference, variableName);
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("CrewPersistence variable name", e);
            }
        }

        public static string Read(Part part, string variableName)
        {
            VariableList<string> list = GetList(part);
            if (list == null || !list.Has(variableName))
                return null;
            return list.GetValue(variableName);
        }

        public static void Write(Part part, string variableName, string value)
        {
            VariableList<string> list = GetList(part);
            if (list == null)
                return;
            bool existed = list.Has(variableName);
            list.SetValue(variableName, value ?? string.Empty, (true, true));
            if (!existed)
                InvalidateCache(list);
        }

        // ---------------------------------------------------------------- capacity / crew

        public static string ReadSeat(Part part, int index)
        {
            return Read(part, SeatVariable(index));
        }

        public static int ReadCapacity(Part part)
        {
            string raw = Read(part, CapacityVariable);
            if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out int capacity) && capacity > 0)
                return capacity;
            return 0;
        }

        /// <summary>True when this part instance carries a persisted mod configuration.</summary>
        public static bool HasStoredConfig(Part part)
        {
            if (part == null)
                return false;
            if (ReadCapacity(part) > 0)
                return true;
            return HasStoredSeat(part);
        }

        static bool HasStoredSeat(Part part)
        {
            VariableList<string> list = GetList(part);
            if (list == null)
                return false;
            foreach (VariableSave save in list.saves)
            {
                if (save != null && IsOwnVariable(save.name) && save.name != CapacityVariable)
                    return true;
            }
            return false;
        }

        /// <summary>Persists capacity and the current seat occupancy of an injected part.</summary>
        public static void SavePartState(Part part, CrewModule crew)
        {
            if (part == null || crew == null)
                return;
            try
            {
                VariableList<string> list = GetList(part);
                if (list == null)
                    return;

                CrewModule.Seat[] seats = crew.seats ?? new CrewModule.Seat[0];
                Write(part, CapacityVariable, seats.Length.ToString());

                for (int i = 0; i < seats.Length; i++)
                {
                    CrewModule.Seat seat = seats[i];
                    string name = seat != null && seat.astronaut != null ? seat.astronaut.Value : null;
                    Write(part, SeatVariable(i), name ?? string.Empty);
                }

                // Drop leftovers from a previously larger layout.
                for (int i = seats.Length; i < seats.Length + 8; i++)
                {
                    string variable = SeatVariable(i);
                    if (!list.Has(variable))
                        break;
                    Write(part, variable, string.Empty);
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("CrewPersistence save part", e);
            }
        }

        /// <summary>Removes every variable this mod stored on the given part.</summary>
        public static void ClearPartState(Part part)
        {
            VariableList<string> list = GetList(part);
            if (list == null)
                return;
            try
            {
                for (int i = list.saves.Count - 1; i >= 0; i--)
                {
                    VariableSave save = list.saves[i];
                    if (save != null && IsOwnVariable(save.name))
                        list.saves.RemoveAt(i);
                }
                InvalidateCache(list);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("CrewPersistence clear part", e);
            }
        }

        /// <summary>
        /// Writes crew data recovered from an external source (e.g. Custom Save Data) into the part's
        /// own variables so the standard restore path picks it up.
        /// </summary>
        public static void Import(Part part, int capacity, IList<string> crew)
        {
            if (part == null)
                return;
            try
            {
                if (capacity > 0)
                    Write(part, CapacityVariable, capacity.ToString());
                if (crew == null)
                    return;

                int effectiveCapacity = capacity > 0 ? capacity : (crew.Count > 0 ? crew.Count : 1);
                Write(part, CapacityVariable, effectiveCapacity.ToString());

                for (int i = 0; i < crew.Count; i++)
                    Write(part, SeatVariable(i), crew[i] ?? string.Empty);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("CrewPersistence import", e);
            }
        }
    }
}
