using UnityEngine;

namespace WorldBuild.Mod.Modules
{
    public class InjectEverywhereWith<T> : MonoBehaviour where T : MonoBehaviour
    {
        protected T TargetComponent;

        public T GetTargetComponent() => TargetComponent;

        private void Awake()
        {
            TargetComponent = GetComponent<T>();
        }
    }
}
