using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UITools;
using UnityEngine;

namespace WorldBuild.Mod.Managers
{
    public class Manager<T> : MonoBehaviour
        where T : Manager<T>
    {
        private static T m_main;

        public static T main => m_main;

        public static string[] ScenesToAttach => new string[] { };

        public void Awake()
        {
            m_main = this as T;
        }
    }
}
