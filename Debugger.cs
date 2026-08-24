using System;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace WorldBuild.Mod
{
    public static class Debugger
    {
        public const bool IsDebugEnabled = true;

        private static object FormatMessage(object msg)
        {
            return msg;
        }
        
        public static void Log(object message, bool overrideDE = false)
        {
            if (!IsDebugEnabled && !overrideDE) return;

            Debug.Log(FormatMessage(message));
        }

        public static void LogException(Exception ex, bool overrideDE = false)
        {
            if (!IsDebugEnabled && !overrideDE) return;

            Debug.LogException(ex);
        }

        public static void LogError(object message, bool overrideDE = false)
        {
            if (!IsDebugEnabled && !overrideDE) return;

            Debug.LogError(FormatMessage(message));
        }

        public static void LogWarning(object message, bool overrideDE = false)
        {
            if(!IsDebugEnabled && !overrideDE) return;

            Debug.LogWarning(FormatMessage(message));
        }
    }
}
