using SFS.UI.ModGUI;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using GUIElement = SFS.UI.ModGUI.GUIElement;

namespace WorldBuild.Mod.UI
{
    public abstract class GUIBase
    {
        public bool SceneReqMet;

        protected GameObject holder;
        protected Window window;

        protected Dictionary<string, GUIElement> elements = new Dictionary<string, GUIElement>();

        public Dictionary<string, GUIElement> Elements => elements;

        public abstract string SceneToAttach { get; }
        public abstract Func<bool> GOActiveCondition { get; }

        protected int WindowID;

        public virtual void Begin() { }
        public virtual void GenerateGUI() { }
        public virtual void Update() { }
        public virtual void LateUpdate() { }

        protected void VerticalDefGroup()
        {
            window.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, spacing: 8, padding: new RectOffset(0, 0, 8, 0));
        }

        protected void HorizontalDefGroup()
        {
            window.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, spacing: 8, padding: new RectOffset(0, 0, 8, 0));
        }

        public GUIBase()
        {
            WindowID = Builder.GetRandomID();
            SceneManager.sceneLoaded += (Scene s, LoadSceneMode lsm) =>
            {
                if (s.name == SceneToAttach)
                {
                    holder = Builder.CreateHolder(
                        Builder.SceneToAttach.CurrentScene, 
                        string.Concat("WorldBuild UI ", UnityEngine.Random.Range(0, int.MaxValue).ToString()));
                    holder.SetActive(false);

                    shouldCallBegin = true;
                }
            };
        }

        bool shouldCallBegin;

        int d;

        public void NewGUI()
        {
            if (holder == null)
            {
                Debugger.Log("Holder was null!");
                return;
            }

            for (var i = 0; i < holder.transform.childCount; i++)
            {
                GameObject.Destroy(holder.transform.GetChild(i).gameObject);
            }

            elements.Clear();

            GC.Collect();

            GenerateGUI();
        }

        public void OnFrame()
        {
            if (!SceneReqMet || holder == null) return;

            if (shouldCallBegin)
            {
                Begin();
                shouldCallBegin = false;
            }

            var newActive = GOActiveCondition();

            if (newActive && !holder.activeSelf)
            {
                NewGUI();

                d++;

                Debugger.Log($"Generated the UI for the {d}th time");
            }

            holder.SetActive(newActive);

            Update();
        }
    }
}
