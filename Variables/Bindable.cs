using SFS.Variables;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuild.Mod.Variables
{
    public class Bindable<T>
    {
        private T m_Value;

        private bool valueChangeCausedByBind;
        private Bindable<T> causeBindable;
        private Obs<T> causeObservable;

        public T Value
        {
            get => m_Value;
            set
            {
                OnChange(Value, value);

                m_Value = value;

                bindedBindables.ForEach((bindable) => 
                {
                    // prevent recursive calls
                    if (valueChangeCausedByBind && bindable == causeBindable) return;

                    bindable.Value = value;
                });
                bindedObservables.ForEach(observable => observable.Value = value);

                valueChangeCausedByBind = false;
                causeBindable = null;
                causeObservable = null;
            }
        }

        private HashSet<Bindable<T>> bindedBindables = new HashSet<Bindable<T>>();
        private HashSet<Obs<T>> bindedObservables = new HashSet<Obs<T>>();

        public bool BindTo(Bindable<T> bindable)
        {
            if (bindable.bindedBindables != null)
            {
                if (bindedBindables.Any(element => element == this))
                {
                    return false;
                }

                bindedBindables.Add(bindable);
                bindable.OnChange += (T oldVal, T newVal) =>
                {

                };

            } else
            {
                return false;
            }

            return true;
        }

        public bool BindTo(Obs<T> observable)
        {
            return true;
        }

        public Action<T, T> OnChange { get; set; }
    }
}
