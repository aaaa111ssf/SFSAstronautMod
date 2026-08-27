using System;
using System.Collections.Generic;
using UnityEngine;

namespace AstronautUnlocker
{
    internal static class ModLogger
    {
        private const string Prefix = "[AstronautMod] ";
        private static readonly HashSet<string> reportedErrors = new HashSet<string>();

        internal static void Info(string message)
        {
            Debug.Log(Prefix + message);
        }

        internal static void Warning(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        internal static void ErrorOnce(string area, Exception exception)
        {
            string key = area ?? "Unknown";
            if (!reportedErrors.Add(key)) return;

            string detail = exception == null ? "Unknown error" : exception.Message;
            Debug.LogError(Prefix + key + " failed: " + detail);
        }
    }
}
