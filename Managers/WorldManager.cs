using UnityEngine;

namespace WorldBuild.Mod.Managers
{
    public class WorldManager<T> : Manager<T>
        where T : WorldManager<T>
    {
        public new static string[] ScenesToAttach => new string[] { "World_PC" };
    }
}
