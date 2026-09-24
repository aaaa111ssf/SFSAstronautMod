using System;
using HarmonyLib;
using UnityEngine;

namespace AstronautMod
{
    internal static class BareNumericLogFilter
    {
        internal static bool ShouldWrite(object message)
        {
            return !(message is byte ||
                     message is sbyte ||
                     message is short ||
                     message is ushort ||
                     message is int ||
                     message is uint ||
                     message is long ||
                     message is ulong ||
                     message is float ||
                     message is double ||
                     message is decimal);
        }
    }

    [HarmonyPatch(typeof(Debug), nameof(Debug.Log), new Type[] { typeof(object) })]
    internal static class Patch_Debug_Log_BareNumber
    {
        private static bool Prefix(object message)
        {
            return BareNumericLogFilter.ShouldWrite(message);
        }
    }

    [HarmonyPatch(typeof(Debug), nameof(Debug.Log), new Type[] { typeof(object), typeof(UnityEngine.Object) })]
    internal static class Patch_Debug_Log_BareNumber_Context
    {
        private static bool Prefix(object message)
        {
            return BareNumericLogFilter.ShouldWrite(message);
        }
    }
}
