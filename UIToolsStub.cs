using SFS.UI.ModGUI;
using UnityEngine;
using SystemType = System.Type;

namespace UITools
{
    public class ClosableWindow : Window
    {
        public bool Minimized { get; set; } = false;
    }

    public static class UIToolsBuilder
    {
        public static ClosableWindow CreateClosableWindow(Transform holder, int ID, int width, int height, float posX, float posY, bool savePosition = true, bool draggable = false, float opacity = 1f, string titleText = "")
        {
            Window window = Builder.CreateWindow(holder, ID, width, height, (int)posX, (int)posY, draggable, savePosition, opacity, titleText);
            ClosableWindow closable = new ClosableWindow();
            closable.Init(window.gameObject, holder);
            closable.ID = window.ID;
            closable.Size = window.Size;
            closable.Position = window.Position;
            closable.Draggable = window.Draggable;
            closable.WindowOpacity = opacity;
            closable.Title = titleText;
            return closable;
        }
    }

    public static class UIToolsExtensions
    {
        public static T As<T>(this object obj) where T : class
        {
            return obj as T;
        }

        public static void SetSelected(this SFS.UI.ModGUI.Button button, bool selected)
        {
        }

        public static Component GetOrAddComponent(this Transform t, SystemType type)
        {
            Component c = t.GetComponent(type);
            if (c == null)
                c = t.gameObject.AddComponent(type);
            return c;
        }

        public static Component GetOrAddComponent(this Component c, SystemType type)
        {
            Component result = c.GetComponent(type);
            if (result == null)
                result = c.gameObject.AddComponent(type);
            return result;
        }
    }
}
