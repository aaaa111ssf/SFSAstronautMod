using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace WorldBuild.Mod.Modules
{
    public class InjectEverywhereWith<T> : MonoBehaviour where T : MonoBehaviour
    {
        protected T TargetComponent;
        
        public T GetTargetComponent() => TargetComponent;
        
        void Awake()
        {
            TargetComponent = GetComponent<T>();
            IEWInjector.IEWs.Add(this);
        }
    }
}
