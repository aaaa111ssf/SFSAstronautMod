using SFS.Parts;
using SFS.UI;
using SFS.UI.ModGUI;
using SFS.World;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WorldBuild.Mod.Build;
using WorldBuild.Mod.Managers;
using WorldBuild.Mod.Modules;

namespace WorldBuild.Mod.UI
{
    public class AstronautManagementGUI : GUIBase
    {
        public override string SceneToAttach => "World_PC";

        public override Func<bool> GOActiveCondition => 
            () => {
                if (PlayerController.main != null)
                    return PlayerController.main.player.Value is Astronaut_EVA;
                return false;
            };

        public static AstronautManagementGUI main;

        public AstronautManagementGUI()
        {
            main = this;
        }

        public override void Update()
        {
            if (!(PlayerController.main.player.Value is Astronaut_EVA eva) || Elements["oxygenLeftApprox"] == null) return;

            if (eva.GetComponent<Astronaut>() == null) return;

            Elements["oxygenLeftApprox"].As<Label>().Text = $"Oxygen left: {Utility.StringifyTime(eva.GetComponent<Astronaut>().GetOxygenSecondsLeft())} {(eva.GetComponent<Astronaut>().BreathingAir ? "(air)" : "")}";
            Elements["resLeft"].As<Label>().Text = $"Resources left: {eva.GetComponent<Astronaut>().materialLeft}";
        }

        public override void GenerateGUI() 
        {
            var coords = WindowPositionHelper.GenerateWindowCoords(0, 16, 384, 268, Anchor.BottomCenter, Origin.BottomCenter);

            window = Builder.CreateWindow(holder.transform, WindowID, 384, 268, coords.x, coords.y, false, false, 0.95f, "Astronaut");
            VerticalDefGroup();

            elements.Add("main", Builder.CreateContainer(window));
            (elements["main"] as Container).CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, childAlignment: TextAnchor.UpperCenter, spacing: 8, padding: new RectOffset(0, 0, 8, 0));
            #region Main
            var main = elements["main"] as Container;

            elements.Add(
                "plantFlag",
                Builder.CreateButton(main, 352, 48, text: "Plant Flag", onClick: () =>
                {
                    AstronautManager.main.PlantFlag();
                }
            ));

            elements.Add(
                "endEVA",
                Builder.CreateButton(main, 352, 48, text: "End EVA", onClick: () =>
                {
                    var best = new CapsuleScanner.BestCapsuleData();

                    var pos = WorldView.ToLocalPosition(PlayerController.main.player.Value.location.position);

                    foreach (var rocket in GameManager.main.rockets)
                    {
                        var data = CapsuleScanner.main.FindBest(rocket, pos, 3f);

                        if (data.GetDistanceTo(pos) < best.GetDistanceTo(pos)) best.cm = data.cm;
                    }
                    
                    if (best.cm == null)
                    {
                        MsgDrawer.main.Log("No capsule nearby!");
                        return;
                    }

                    CapsuleScanner.main.selectedCapsule.Value = best;

                    AstronautSpawner.main.EndEVA(best.cm.Rocket);
                }
            ));

            elements.Add("oxygenLeftApprox", Builder.CreateLabel(main, 352, 32, text: "Oxygen left: 0m 0s"));
            elements.Add("resLeft", Builder.CreateLabel(main, 352, 32, text: "Resources left: 0"));
            #endregion
        }
    }
}
