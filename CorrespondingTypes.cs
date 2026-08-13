using System;
using WorldBuild.Mod.Modules;
using WorldBuild.Toolkit;

namespace WorldBuild.Mod
{
    public static class CorrespondingTypes
    {
        public static Type GetCorrespondingType(ModuleType type)
        {
            switch (type)
            {
                case ModuleType.Drill:
                    return typeof(DrillModule);
            }

            return null;
        }
    }
}
