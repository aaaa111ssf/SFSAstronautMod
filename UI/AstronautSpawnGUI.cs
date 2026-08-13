using SFS.UI.ModGUI;
using SFS.World;
using System;
using UITools;
using WorldBuild.Mod.Managers;
using WorldBuild.Mod.Modules;

namespace WorldBuild.Mod.UI
{
    public class AstronautSpawnGUI : GUIBase
    {
        public override string SceneToAttach => "World_PC";

        public override Func<bool> GOActiveCondition => () => 
        { 
            return AstronautSpawner.main.CanSpawnEVA(); 
        };

        public override void Begin()
        {
            PlayerController.main.player.OnChange += OnPlayerChange;
            CapsuleScanner.main.selectedCapsule.ValueChanged += (CapsuleScanner.BestCapsuleData o, CapsuleScanner.BestCapsuleData n) =>
            {
                NewGUI();
            };
        }

        private bool windowState;

        public override void Update()
        {
            if (!elements.ContainsKey("oxygenAvail")) return;

            var plr = PlayerController.main.player.Value;
            if (!plr) return;

            var rox = plr.GetComponent<RocketResources>();
            if (!rox) return;
            
            if (window != null)
            {
                windowState = window.As<ClosableWindow>().Minimized;
            }

            var timeLeft = rox.CalculateResourceAvailable();

            if (!(elements["oxygenAvail"] is Label label))
            {
                Debugger.LogError("oxygenAvail was not of the correct type");
                return;
            }
            
            label.Text = $"Available oxygen: {Utility.StringifyTime(timeLeft)}";
            Elements["resAvail"].As<Label>().Text = $"Available resources: {rox.CalculateResourceAvailable(RocketResources.ResourceType.BuildResource)}";
        }

        private void OnPlayerChange(Player oldP, Player newP)
        {
            if (newP == null) return;
            NewGUI();
        }

        public override void GenerateGUI()
        {
            var width = 384;
            var height = 200;
            var coords = WindowPositionHelper.GenerateWindowCoords(0, -80, width, height, Anchor.TopCenter, Origin.TopCenter);
            window = UIToolsBuilder.CreateClosableWindow(holder.transform, WindowID, width, height, coords.x, coords.y, false, false, 0.95f, "Astronaut Manager");
            window.As<ClosableWindow>().Minimized = windowState;
            VerticalDefGroup();

            if (AstronautManager.main.eva.Count == 0)
            {
                if (CapsuleScanner.main.selectedCapsule.Value.cm == null)
                {
                    elements.Add("selectNote", Builder.CreateLabel(window, 352, 32, text: "Select a capsule first! (click one with RMB)"));
                    window.Size = new UnityEngine.Vector2(window.Size.x, 108);
                    return;
                }

                elements.Add("oxygenAvail", Builder.CreateLabel(
                    window, 352, 32, text: "Available oxygen: [not calculated yet]"
                ));
                
                elements.Add("resAvail", Builder.CreateLabel(
                    window, 352, 32, text: "Available resources: [not calculated yet]"
                ));

                elements.Add("spawnBtn", Builder.CreateButton(window, 352, 45, onClick: () =>
                {
                    AstronautSpawner.main.StartEVA();
                    (elements["spawnBtn"] as Button).SetSelected(false);
                }, text: "Start EVA"));
            } else
            {
                width = 384;
                height = 120;
                coords = WindowPositionHelper.GenerateWindowCoords(0, -80, width, height, Anchor.TopCenter, Origin.TopCenter);
                window.Size = new UnityEngine.Vector2(width, height);
                window.Position = coords;
                elements.Add("switchToRkt", Builder.CreateButton(window, 352, 45, onClick: () =>
                {
                    PlayerController.main.SmoothChangePlayer(AstronautManager.main.eva[0]);
                }, text: "Switch to astronaut"));
            }
        }
    }
}
