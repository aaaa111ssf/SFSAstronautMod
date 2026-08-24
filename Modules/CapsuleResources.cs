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

            if (!varMod.boolVariables.GetValue("oxygenInitialized") || !varMod.boolVariables.GetValue("oxygenV2Initialized"))
            {
                // 氧气用于 EVA 生存；旧存档的零氧舱体会补满。
                Oxygen = MaxOxygen;
                varMod.boolVariables.SetValue("oxygenInitialized", true);
                varMod.boolVariables.SetValue("oxygenV2Initialized", true);
            }
            if (!varMod.boolVariables.GetValue("evaresInitialized"))
            {
                EVARes = MaxEVARes;
                varMod.boolVariables.SetValue("evaresInitialized", true);
            }
        }
    }
}
