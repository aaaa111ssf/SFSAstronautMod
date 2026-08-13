using SFS.UI.ModGUI;
using System;
using WorldBuild.Mod.Build;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SFS.Parts.Modules;

namespace WorldBuild.Mod.UI
{
    public class VariableEditorGUI : GUIBase
    {
        public override Func<bool> GOActiveCondition => () => EditorActive && WorldBuildManager.main.heldPart != null;
        public override string SceneToAttach => "World_PC";

        public bool EditorActive = false;

        public const int width = 360;
        public const int height = 480;

        public void OpenEditor()
        {
            EditorActive = true;
            base.NewGUI();
        }

        public override void GenerateGUI()
        {
            var coords = WindowPositionHelper.GenerateWindowCoords(-32 - PartControlsGUI.width, 16, width, height, Anchor.BottomRight, Origin.BottomRight);
            window = Builder.CreateWindow(holder.transform, WindowID, width, height, coords.x, coords.y);
            VerticalDefGroup();

            var part = WorldBuildManager.main.heldPart;

            foreach (var variablesDrawer in part.GetModules<VariablesDrawer>())
            {
                
            }
        }
    }
}
