using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using WorldBuild.Mod.Managers;

namespace WorldBuild.Mod.Modules
{
    public class IEWInjector : BaseManager<IEWInjector>
    {
        private static readonly List<(Type moduleType, Type targetType)> Types = new List<(Type, Type)>();
        private bool initialized;

        private void Start()
        {
            foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
            {
                var baseType = type.BaseType;
                if (baseType != null && baseType.IsGenericType &&
                    baseType.GetGenericTypeDefinition() == typeof(InjectEverywhereWith<>))
                    Types.Add((type, baseType.GetGenericArguments()[0]));
            }

            initialized = true;
            ForceRefresh();
        }

        public static void ForceRefresh()
        {
            if (Types.Count == 0) return;

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                    AddRecursive(root.transform);
            }
        }

        private static void AddRecursive(Transform transform)
        {
            foreach (var pair in Types)
            {
                if (transform.GetComponent(pair.targetType) != null && transform.GetComponent(pair.moduleType) == null)
                    transform.gameObject.AddComponent(pair.moduleType);
            }

            for (var i = 0; i < transform.childCount; i++)
                AddRecursive(transform.GetChild(i));
        }

        private void Update()
        {
            if (initialized) ForceRefresh();
        }
    }
}
