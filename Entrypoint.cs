using UnityEngine;
using HarmonyLib;
using WorldBuild.Mod.Managers;
using ModLoader.Helpers;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using WorldBuild.Mod.Build;
using SFS.UI;
using SFS.Variables;
using SFS.Parts.Modules;
using SFS.Parts;
using AstronautUnlocker;

namespace WorldBuild.Mod
{
    public class Entrypoint : ModLoader.Mod
    {
        public override string ModNameID => "worldbuild_astronaut_merged";
        public override string DisplayName => "WorldBuild + AstronautMod";
        public override string Author => "Fusion Space Industries & A Future star";
        public override string Description => "Build rockets during missions + native astronaut/crew system with EVA, flags, and rock collection.";
        public override string ModVersion => "1.0.0-merged";
        public override string MinimumGameVersionNecessary => "1.6";

        public static GameObject BaseGO;
        public static Entrypoint main;
        public static Harmony patcher;

        public Entrypoint()
        {
            main = this;
        }

        public override Dictionary<string, string> Dependencies => new Dictionary<string, string> { { "UITools", "1.1.5" } };

        public override void Early_Load()
        {
            patcher = new Harmony("com.sfs.worldbuild.astronaut.merged");
            patcher.PatchAll();
            ManagerInjector.Inject();
            AstronautUnlockerMod.Initialize(patcher);
        }

        public override void Load()
        {
            Keybindings.SetupKeybindings();
            AstronautUnlockerMod.OnLoad();
        }
    }
}
