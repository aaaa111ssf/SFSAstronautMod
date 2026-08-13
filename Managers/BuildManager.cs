using UnityEngine;

namespace WorldBuild.Mod.Managers
{
    public class BuildManager<T> : Manager<T>
        where T : BuildManager<T>
    {
        public new static string[] ScenesToAttach => new string[] { "Build_PC" };
    }
}
