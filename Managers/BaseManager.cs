namespace WorldBuild.Mod.Managers
{
    public class BaseManager<T> : Manager<T>
        where T : BaseManager<T>
    {
        public new static string[] ScenesToAttach => new string[] { "Base_PC" };
    }
}
