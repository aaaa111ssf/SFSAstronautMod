using SFS.Variables;
using SFS.World;
using WorldBuild.Mod.Build;

namespace WorldBuild.Mod.Modules
{
    public class CapsuleResources : InjectEverywhereWith<CrewModule>
    {
        private VariablesModule varMod;

        public static double MaxOxygen => 1000;
        public static double MaxEVARes => 50;

        public double Oxygen
        {
            get => varMod.doubleVariables.GetValue("oxygen");
            set => varMod.doubleVariables.SetValue("oxygen", value, (true, true));
        }
        
        public double EVARes
        {
            get => varMod.doubleVariables.GetValue("evares");
            set => varMod.doubleVariables.SetValue("evares", value, (true, true));
        }

        private void Start()
        {
            varMod = GetComponent<VariablesModule>();

            if (!varMod.boolVariables.GetValue("oxygenInitialized"))
            {
                Oxygen = WorldBuildManager.PlacedFrames <= 2 ? 0 : MaxOxygen;
                varMod.boolVariables.SetValue("oxygenInitialized", true);
            }
            if (!varMod.boolVariables.GetValue("evaresInitialized"))
            {
                EVARes = WorldBuildManager.PlacedFrames <= 2 ? 0 : MaxEVARes;
                varMod.boolVariables.SetValue("evaresInitialized", true);
            }
        }
    }
}
