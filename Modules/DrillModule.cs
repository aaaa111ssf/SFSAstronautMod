using SFS.Parts.Modules;

namespace WorldBuild.Mod.Modules
{
    public class DrillModule : Module
    {
        private FlowModule flowMod;

        private void Start()
        {
            flowMod = variables[0] as FlowModule;
        }
    }
}
