using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SFS.Parts;
using SFS.Variables;
using SFS.World;

namespace AstronautMod
{
    // 本模组注入到自定义乘员舱的 CrewModule 的部件级持久化。
    public static class CrewPersistence
    {
        public const string CapacityVariable = "AstronautMod_Cap";
        public const string SeatPrefix = "AstronautMod_Seat";
        public const string IdVariable = "AstronautMod_Id";

        // 变量名是否属于本模组。
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

    // 给 VariableList<string>.LoadDictionary 打补丁，让本模组的变量
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

        // VariableList<string>.LoadDictionary 的 Harmony 前置补丁。
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

        // 按需创建变量，并返回与之绑定的引用。
        public static String_Reference Bind(Part part, string variableName)
        {
            VariableList<string> list = GetList(part);
            if (list == null)
            {
                // 没有变量模块时退回独立本地值（同原行为）。
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

        // 该部件实例是否带有模组持久化配置。
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

        // 清空某个部件上所有已记录的乘员名（座位变量），保留容量设置。 场景：建造页点「New Rocket」后，新部件实例仍可能带着上一枚火箭的乘员名 （座位名绑定在部件的可保存变量上），InjectCrew
        // Module 的 ReadSeat 会把它们 填回空座位 —— 表现为「新建火箭后宇航员跟着一块回来」。
        public static int ClearSeatAssignments(Part part)
        {
            if (part == null)
                return 0;
            try
            {
                VariableList<string> list = GetList(part);
                if (list == null)
                    return 0;

                int cleared = 0;
                // 座位数量上限与 SavePartState 的清理范围一致
                for (int i = 0; i < AstronautModMain.MaxCrewCapacity + 8; i++)
                {
                    string variable = SeatVariable(i);
                    if (!list.Has(variable))
                    {
                        // 变量还没建出来，后面的也不会有
                        if (i > 0 && !list.Has(SeatVariable(0))) break;
                        continue;
                    }
                    if (!string.IsNullOrEmpty(list.GetValue(variable)))
                    {
                        Write(part, variable, string.Empty);
                        cleared++;
                    }
                }
                return cleared;
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Clear seat assignments", e);
                return 0;
            }
        }

        // 持久化已注入部件的容量与当前座位占用。
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

                // 清掉更早更大布局留下的多余变量。
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

        // 移除本模组在该部件上存过的全部变量。
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

    // 把从外部来源（如 Custom Save Data）恢复的乘员数据写回部件
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
