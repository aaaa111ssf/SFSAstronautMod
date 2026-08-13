using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
using WorldBuild.Mod.Managers;
using HarmonyLib;
using UITools;

namespace WorldBuild.Mod.Modules
{
    public class IEWInjector : BaseManager<IEWInjector>
    {
        private static List<(Type, Type)> _iewTypes = new List<(Type, Type)>();
        private static int _typeCount = 0;
        
        public static HashSet<MonoBehaviour> IEWs = new HashSet<MonoBehaviour>(); 
        
        private void Start()
        {
            foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
            {
                var baseType = type.BaseType;
                if (baseType == null) continue;
                if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(InjectEverywhereWith<>))
                {
                    _iewTypes.Add((type, baseType.GetGenericArguments()[0]));
                    _typeCount++;
                }
            }
        }
        
        public static void ForceRefresh()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded) continue;

                var roots = s.GetRootGameObjects();

                for (var ri = 0; ri < roots.Length; ri++)
                {
                    AddRecursive(roots[ri].transform, _iewTypes);
                }
            }
        }
        
        static void AddRecursive(Transform t, List<(Type, Type)> types)
        {
            for (var i = 0; i < _typeCount; i++)
            {
                var pair = types[i];
                if (t.GetComponent(pair.Item2) != null)
                    t.GetOrAddComponent(pair.Item1);
            }

            var cc = t.childCount;

            if (cc == 0) return;
            
            for (var i = 0; i < cc; i++)
                AddRecursive(t.GetChild(i), types);
        }

        private void Update()
        {
            ForceRefresh();
        }
    }
}