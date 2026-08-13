using WorldBuild.Mod.Build;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SFS.UI.ModGUI;
using UnityEngine.UI;
using SFS.Parts.Modules;
using UnityEngine;

namespace WorldBuild.Mod.UI
{
    public class PartControlsGUI : GUIBase
    {
        public override Func<bool> GOActiveCondition => () => WorldBuildManager.main.worldBuildActive && WorldBuildManager.main.heldPart != null;
        public override string SceneToAttach => "World_PC";

        public const int width = 540;
        public const int height = 480;

        public override void GenerateGUI()
        {
            var coords = WindowPositionHelper.GenerateWindowCoords(-16, 16, width, height, Anchor.BottomRight, Origin.BottomRight);
            window = Builder.CreateWindow(holder.transform, WindowID, width, height, coords.x, coords.y, opacity: 0.5f, titleText: "Selected Part");
            VerticalDefGroup();
            window.EnableScrolling(SFS.UI.ModGUI.Type.Vertical);

            elements["actionsHolder"] = Builder.CreateContainer(window);
            elements["actionsHolder"].As<Container>().CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, spacing: 8);
            elements["placeBtn"] = Builder.CreateButton(elements["actionsHolder"], width / 2 - 16, 45, onClick: () => WorldBuildManager.main.TryBuildPart(), text: "Place");
            elements["destroyBtn"] = Builder.CreateButton(elements["actionsHolder"], width / 2 - 16, 45, onClick: () => WorldBuildManager.main.DestroyHeldPart(), text: "Delete");

            elements["transformHld"] = Builder.CreateContainer(window);
            elements["transformHld"].As<Container>().CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, spacing: 8);

            elements["flipHoriz"] = Builder.CreateButton(elements["transformHld"], width / 4 - 12, 45, onClick: () => {
                Utility.ScalePart(WorldBuildManager.main.heldPart, new Vector2(-1, 1));
            }, text: "Horiz");
            elements["flipVert"] = Builder.CreateButton(elements["transformHld"], width / 4 - 12, 45, onClick: () => {
                Utility.ScalePart(WorldBuildManager.main.heldPart, new Vector2(1, -1));
            }, text: "Vert");

            elements["rotLeft"] = Builder.CreateButton(elements["transformHld"], width / 4 - 12, 45, onClick: () => {
                Utility.RotatePart(WorldBuildManager.main.heldPart, 90f);
            }, text: "Left");
            elements["rotRight"] = Builder.CreateButton(elements["transformHld"], width / 4 - 12, 45, onClick: () => {
                Utility.RotatePart(WorldBuildManager.main.heldPart, -90f);
            }, text: "Right");


            //elements["openEditor"] = Builder.CreateButton(window, width - 24, 45, onClick: () => GUIManager.main.GetUI<VariableEditorGUI>().OpenEditor(), text: "Edit values");

            elements["sep"] = Builder.CreateSeparator(window, width - 24);
            Builder.CreateSpace(window, 0, 8);

            var part = WorldBuildManager.main.heldPart;

            elements["info"] = Builder.CreateLabel(window, width - 24, 0, text: $"--- Part Info ---\nName: {part.displayName.Field.subs[0]}\nMass: {part.mass.Value}t\nRequired resources: {PartPriceCalculator.Calculate(part)}\n--- Stats ---\n{Utility.GetStats(part)}");

            elements["info"].As<Label>().AutoFontResize = false;
            elements["info"].As<Label>().FontSize = 32;
            elements["info"].As<Label>().gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        }
    }
}
