using SFS;
using SFS.Parts;
using System;
using UnityEngine;
using WorldBuild.Toolkit;

namespace WorldBuild.Mod.Managers
{
    public class ModuleInjector : BaseManager<ModuleInjector>
    {
        public bool injected;

#pragma warning disable IDE0051

        private void Update()
        {
            if (injected) return;


            injected = true;

            Inject();
        }

#pragma warning restore IDE0051

        private void Inject()
        {
            foreach (var part in Base.partsLoader.parts.Values)
            {
                var modules = part.GetComponentsInChildren<ExternalModule>();
                
                for (var i = 0; i < modules.Length; i++)
                {
                    if (modules[i])
                    {
                        var module = modules[i].gameObject.AddComponent(CorrespondingTypes.GetCorrespondingType(modules[i].type)) as Module;

                        module.variables = modules[i].args;
                    }
                }
            }
        }
    }
}
