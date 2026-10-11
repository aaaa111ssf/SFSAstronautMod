using System;
using System.Collections.Generic;
using UnityEngine;

namespace AstronautMod
{
    // 模组日志。全部经由 Unity Debug.Log 输出到 Player.log，行首带 [AstronautMod] 便于检索。
// 排查问题时可打开 VerboseEnabled（ModLogger.SetVerbose(true)），关键路径会输出更详细的日志：
//   grep "\[AstronautMod\]" "C:/Users/<用户名>/AppData/LocalLow/Stef Morojna/Spaceflight Simulator/Player.log"
    internal static class ModLogger
    {
        private const string Prefix = "[AstronautMod] ";
        private static readonly HashSet<string> reportedErrors = new HashSet<string>();
        private static readonly Dictionary<string, int> errorCounts = new Dictionary<string, int>();

        // 详细日志开关。默认关闭，避免每帧刷屏。
        internal static bool VerboseEnabled { get; set; }

        internal static void SetVerbose(bool value)
        {
            VerboseEnabled = value;
            Info("verbose logging " + (value ? "ENABLED" : "disabled"));
        }

        internal static void Info(string message)
        {
            Debug.Log(Prefix + message);
        }

        // 只在 VerboseEnabled 时输出，用于高频路径（每帧/每 0.5s）。
        internal static void Verbose(string message)
        {
            if (VerboseEnabled) Debug.Log(Prefix + "[v] " + message);
        }

        internal static void Warning(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        internal static void Error(string area, Exception exception)
        {
            string detail = exception == null ? "Unknown error" : exception.ToString();
            Debug.LogError(Prefix + area + " failed: " + detail);
        }

        private const int SuppressThreshold = 200;

        // 同一区域只在第一次打完整堆栈，之后每 SuppressThreshold 次打一行节流提示 —— 既不会把 Player.log 撑爆，又能在排查时看出"还在持续出错"并拿到命中次数。
        internal static void ErrorOnce(string area, Exception exception)
        {
            string key = area ?? "Unknown";
            if (reportedErrors.Add(key))
            {
                Error(key, exception);
                return;
            }
            errorCounts[key] = errorCounts.TryGetValue(key, out int c) ? c + 1 : 1;
            if (errorCounts[key] % SuppressThreshold == 0)
                Debug.LogWarning(Prefix + key + " still failing, x" + errorCounts[key] +
                    " total (stack traces suppressed)");
        }

        // 清空去重记录（模组重载时用）。
        internal static void ResetErrorCache()
        {
            reportedErrors.Clear();
            errorCounts.Clear();
        }
    }
}