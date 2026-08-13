using System.Linq;
using SFS;
using SFS.Parts;
using SFS.Variables;
using SFS.World;
using WorldBuild.Mod.Modules;

namespace WorldBuild.Mod.Managers
{
    public class CapsuleVariables : BaseManager<CapsuleVariables>
    {
        private void Start()
        {
            var capsules = Base.partsLoader.parts.Values
                .Where(part => part.GetComponent<CrewModule>() != null);

            foreach (var capsule in capsules)
            {
                Debugger.Log("chuj kurwa");
                var varMod = capsule.GetComponent<VariablesModule>();

                varMod.doubleVariables.SetValue("oxygen", CapsuleResources.MaxOxygen, (true, true));

                // the workaround for the variables bug
                varMod.boolVariables.SetValue("oxygenInitialized", false, (true, true));
            }
        }
    }
}
