using JetBrains.Annotations;
using SFS.Variables;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuild.Mod.Variables
{
    public struct Observable<T>
    {
        private T m_Value;

        public T Value
        {
            get => m_Value;
            set
            {
                if (value.Equals(m_Value)) return;

                var oldValue = m_Value;

                m_Value = value;

                if (ValueChanged != null)
                    ValueChanged(oldValue, value);
            }
        }

        public event Action<T, T> ValueChanged;

        public static implicit operator T(Observable<T> obs)
        {
            return obs.Value;
        }
    }
}
