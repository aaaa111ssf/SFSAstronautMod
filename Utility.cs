using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using SFS.Analytics;
using SFS.UI.ModGUI;
using UnityEngine;
using UnityEngine.SceneManagement;
using WorldBuild.Toolkit;
using Type = System.Type;
using WorldBuild.Mod.UI;
using SFS.Parts;
using SFS.Parts.Modules;
using SFS.Translations;
using System.Security.AccessControl;
using SFS.World;

namespace WorldBuild.Mod
{
    public static class Utility
    {
        public static bool CheckPackLoaded()
        {
            try
            {
                var temp = ModuleType.Drill;

                return temp == ModuleType.Drill;
            } catch
            {
                return false;
            }
        }

        public static bool CheckSceneLoaded(string name)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isLoaded)
                if (SceneManager.GetSceneAt(i).name == name)
                {
                    return true;
                }
            }

            return false;
        }

        public static Component GetOrAddComponent(this GameObject go, Type type)
        {
            if (!go) return null;

            if (go.GetComponent(type) == null) go.AddComponent(type);

            return go.GetComponent(type);
        }

        public static Component GetOrAddComponent(this Component component, Type type)
        {
            return component.gameObject.GetComponent(type) ?? component.gameObject.AddComponent(type);
        }

        public static void RotatePart(Part part, float deltaAngle)
        {
            var val = part.orientation.orientation.Value;
            part.orientation.orientation.Value = new Orientation(val.x, val.y, val.z + deltaAngle);
            part.RegenerateMesh();
        }

        public static void ScalePart(Part part, Vector2 mult)
        {
            var val = part.orientation.orientation.Value;
            part.orientation.orientation.Value = new Orientation(val.x * mult.x, val.y * mult.y, val.z);
            part.RegenerateMesh();
        }

        public static string StringifyTime(double seconds)
        {
            var hoursLeft = (int)(seconds / 3600);
            var minutesLeft = (int)(seconds / 60 - hoursLeft * 60);
            var secondsLeft = (int)(seconds - minutesLeft * 60 - hoursLeft * 3600);

            var hoursLeftString = hoursLeft > 0 ? $"{hoursLeft}h " : "";
            var minutesLeftString = hoursLeft > 0 || minutesLeft > 0 ? $"{minutesLeft}m " : "";
            var secondsLeftString = $"{secondsLeft}s";

            return string.Concat(hoursLeftString, minutesLeftString, secondsLeftString);
        }

        public static List<T> KeySort<T>(this List<T> source, Func<T, double> key, bool desc = false)
        {
            var result = new List<T>();

            var temp = new List<T>(source);

            while (result.Count < source.Count())
            {
                var bestKey = desc ? double.NegativeInfinity : double.PositiveInfinity;
                T bestValue = default;

                foreach (var item in temp)
                {
                    var curKey = key.Invoke(item);
                    if ((desc && curKey >= bestKey) || (!desc && curKey <= bestKey))
                    {
                        bestKey = curKey;
                        bestValue = item;
                    } 
                }

                result.Add(bestValue);
                temp.Remove(bestValue);
            }

            return result;
        }

        public static T As<T>(this object obj) where T : class
        {
            return obj as T;
        }

        public static string GetStats(Part part)
        {
            var result = new StringBuilder();

            foreach (var rm in part.GetModules<ResourceModule>())
            {
                result.AppendLine(rm.resourceType.displayName.Field + ": " + rm.ResourceAmount.ToString(2, false) + " / " + rm.TotalResourceCapacity.ToString(2, false) + rm.resourceType.resourceUnit.Field);
            }
            foreach (var em in part.GetModules<EngineModule>())
            {
                result.AppendLine("Thrust: " + em.thrust.Value + "t");
            }
            if (part.GetModules<DetachModule>().Length != 0)
            {
                var dm = part.GetModules<DetachModule>()[0];
                result.AppendLine("Sep. force: " + dm.separationForce.Value.magnitude * dm.forceMultiplier.Value + "kN");
            }

            return result.ToString();
        }

        public static bool AboutEqual(this float a, float b, float tolerance = 0.0001f)
        {
            return a - b < tolerance;
        }
        
        public static bool AboutEqual(this double a, double b, double tolerance = 0.0000001)
        {
            return a - b < tolerance;
        }
    }
}
