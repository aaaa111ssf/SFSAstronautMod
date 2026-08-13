using ModLoader;
using ModLoader.Helpers;
using SFS.Input;
using SFS.World;
using UnityEngine;
using WorldBuild.Mod.Build;

namespace WorldBuild.Mod
{
    public class Keybindings : ModKeybindings
    {
        public static Keybindings main;
        
        public override void CreateUI()
        {
            main = this;
            SceneHelper.OnWorldSceneLoaded += SetupWorldKeybindings;
            
            CreateUI_Text("WorldBuild Keybindings");
            
            CreateUI_Keybinding(Run, Run.key, "Sprint");
            
            CreateUI_Keybinding(Place, Place.key, "Place part");
            CreateUI_Keybinding(Delete, Delete.key, "Delete part");
            CreateUI_Keybinding(RotateLeft, RotateLeft.key, "Rotate left");
            CreateUI_Keybinding(RotateRight, RotateRight.key, "Rotate right");
            
            CreateUI_Keybinding(FlipHorizontally, FlipHorizontally.key, "Flip horizontally");
            CreateUI_Keybinding(FlipHorizontally2, FlipHorizontally2.key, "");
            CreateUI_Keybinding(FlipVertically, FlipVertically.key, "Flip vertically");
            CreateUI_Keybinding(FlipVertically2, FlipVertically2.key, "");
        }

        public static void SetupKeybindings()
        {
            SetupKeybindings<Keybindings>(Entrypoint.main);
        }

        private void SetupWorldKeybindings()
        {
            AddOnKeyDown_World(Place, WorldBuildManager.main.TryBuildPart);
            
            AddOnKeyDown_World(Delete, WorldBuildManager.main.DestroyHeldPart);
            
            AddOnKeyDown_World(RotateLeft, () =>
            {
                if (!(PlayerController.main.player.Value is Astronaut_EVA)) return;
                
                Utility.RotatePart(WorldBuildManager.main.heldPart, 90f);
            });
            
            AddOnKeyDown_World(RotateRight, () =>
            {
                if (!(PlayerController.main.player.Value is Astronaut_EVA)) return;
                
                Utility.RotatePart(WorldBuildManager.main.heldPart, -90f);
            });
            
            AddOnKeyDown_World(FlipHorizontally, () =>
            {
                if (!(PlayerController.main.player.Value is Astronaut_EVA)) return;
                
                Utility.ScalePart(WorldBuildManager.main.heldPart, new Vector2(-1f, 1f));
            });
            
            AddOnKeyDown_World(FlipHorizontally2, () =>
            {
                if (!(PlayerController.main.player.Value is Astronaut_EVA)) return;
                
                Utility.ScalePart(WorldBuildManager.main.heldPart, new Vector2(-1f, 1f));
            });
            
            AddOnKeyDown_World(FlipVertically, () =>
            {
                if (!(PlayerController.main.player.Value is Astronaut_EVA)) return;
                
                Utility.ScalePart(WorldBuildManager.main.heldPart, new Vector2(1f, -1f));
            });
            
            AddOnKeyDown_World(FlipVertically2, () =>
            {
                if (!(PlayerController.main.player.Value is Astronaut_EVA)) return;
                
                Utility.ScalePart(WorldBuildManager.main.heldPart, new Vector2(1f, -1f));
            });
        }
        
        public KeybindingsPC.Key Place = KeyCode.Return;
        public KeybindingsPC.Key Delete = KeyCode.Delete;
        public KeybindingsPC.Key RotateLeft = KeyCode.F;
        public KeybindingsPC.Key RotateRight = KeyCode.H;
        public KeybindingsPC.Key FlipHorizontally = KeyCode.V;
        public KeybindingsPC.Key FlipHorizontally2 = KeyCode.N;
        public KeybindingsPC.Key FlipVertically = KeyCode.B;
        public KeybindingsPC.Key FlipVertically2 = KeyCode.G;
        public KeybindingsPC.Key Run = KeyCode.LeftShift;
    }
}