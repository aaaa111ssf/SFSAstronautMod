using System;
using System.Collections.Generic;
using System.Text;

namespace AstronautMod
{
    // 乘员生命周期的集中诊断。
// 目的：把「谁在什么时候、被什么事件、判定成什么结果」串成一条可检索的链路，避免出了问题只能靠猜。
// 典型排查命令：
//   grep "\[AstronautMod\]\[Crew\]" Player.log
// 事件会同时写进环形缓冲（供模组内查询最近 N 条）和 Player.log。
    internal static class CrewDiagnostics
    {
        private const int Capacity = 64;

        private static readonly Queue<string> Recent = new Queue<string>();
        private static int sequence;

        // 最近的乘员事件（供 UI/调试显示）。
        internal static IEnumerable<string> RecentEvents => Recent;

        internal static void Event(string kind, string astronaut, string detail)
        {
            try
            {
                sequence++;
                string name = string.IsNullOrEmpty(astronaut) ? "<none>" : astronaut;
                string line = $"[Crew]{sequence:000} {kind,-18} {name} {detail}";
                lock (Recent)
                {
                    Recent.Enqueue(line);
                    while (Recent.Count > Capacity) Recent.Dequeue();
                }
                ModLogger.Info(line);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("CrewDiagnostics", e);
            }
        }

        // 汇总当前世界里的乘员分布，便于确认"名册与座位是否一致"。
        internal static void DumpRoster(string reason)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("[Crew] roster dump (").Append(reason).Append(")");

                var state = SFS.Career.AstronautState.main;
                if (state == null)
                {
                    sb.Append(" : AstronautState.main is NULL");
                    ModLogger.Info(sb.ToString());
                    return;
                }

                if (state.crew_Build != null && state.crew_Build.Count > 0)
                    sb.Append(" build=[").Append(string.Join(",", state.crew_Build)).Append("]");

                var s = state.state;
                if (s?.crew_World != null && s.crew_World.Count > 0)
                    sb.Append(" world=[").Append(string.Join(",", s.crew_World.ConvertAll(c => c.astronautName))).Append("]");

                if (s?.eva != null && s.eva.Count > 0)
                    sb.Append(" eva=[").Append(string.Join(",", s.eva.ConvertAll(e => e.astronautName))).Append("]");

                int seats = 0, occupied = 0;
                try
                {
                    foreach (var crew in UnityEngine.Object.FindObjectsByType<SFS.World.CrewModule>(
                        UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None))
                    {
                        if (crew?.seats == null) continue;
                        foreach (var seat in crew.seats)
                        {
                            if (seat == null) continue;
                            seats++;
                            if (seat.HasAstronaut) occupied++;
                        }
                    }
                }
                catch { }
                sb.Append(" seats=").Append(occupied).Append("/").Append(seats);

                ModLogger.Info(sb.ToString());
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("CrewDiagnostics roster", e);
            }
        }
    }
}