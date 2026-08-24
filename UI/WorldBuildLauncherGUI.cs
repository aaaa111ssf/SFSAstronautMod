using System;
using SFS.UI.ModGUI;
using SFS.World;
using WorldBuild.Mod.Build;

namespace WorldBuild.Mod.UI
{
    public class WorldBuildLauncherGUI : GUIBase
    {
        public override string SceneToAttach => "World_PC";

        public override Func<bool> GOActiveCondition => () =>
        {
            return WorldBuildManager.main != null && !WorldBuildManager.main.worldBuildActive &&
                PlayerController.main != null &&
                (PlayerController.main.player.Value is Rocket || PlayerController.main.player.Value is Astronaut_EVA);
        };

        public override void GenerateGUI()
        {
            const int width = 260;
            const int height = 142;
            var coords = WindowPositionHelper.GenerateWindowCoords(16, -120, width, height, Anchor.TopLeft, Origin.TopLeft);
            window = Builder.CreateWindow(holder.transform, WindowID, width, height, coords.x, coords.y, false, true, 0.95f, "World Build");
            VerticalDefGroup();
            Builder.CreateButton(window, width - 16, 52, onClick: () => WorldBuildManager.main?.EnterBuild(), text: "Open Build");
        }
    }
}
