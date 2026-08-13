using System;
using System.Linq;
using SFS.UI;
using SFS.World;
using SFS.World.Maps;
using UnityEngine;
using WorldBuild.Mod.Managers;
using UnityEngine.UI;
using WorldBuild.Mod.Build;
using WorldBuild.Mod.Modules;

namespace WorldBuild.Mod.UI
{
    public class VanillaAstronautGUI : WorldManager<VanillaAstronautGUI>
    {
        private GameObject helpBtn;
        private ButtonPC bpc;
        private Sprite buildIcon;

        private GameObject topBar;

        private GameObject originalRecover;
        private GameObject astronautRecover;

        private ButtonPC selectResourceSourceBtn;

        private void Start()
        {
            WorldBuildManager.main.ExitBuild();
            
            var panel = GameObject.Find("Top Left Panel");
            foreach (var text in panel.GetComponentsInChildren<TextAdapter>())
            {
                if (text.Text == "Help")
                {
                    helpBtn = text.transform.parent.gameObject;
                }
            }
            
            var texture = new Texture2D(1, 1);
            texture.LoadImage(ResourceFile.Hammer);
            
            buildIcon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            
            helpBtn.GetComponentInChildren<TextAdapter>().Text = "Build";
            helpBtn.GetComponentsInChildren<Image>().First(i => i.gameObject.name == "Icon").sprite = buildIcon;
            
            var buttonPC = helpBtn.GetComponent<ButtonPC>();
            buttonPC.onClick.Clear();
            buttonPC.onClick += WorldBuildManager.main.ToggleBuild;
            
            bpc = buttonPC;

            topBar = GameObject.Find("Top Center Stats");

            //originalRecover = GameObject.Find("Recover Button");
            astronautRecover = Instantiate(originalRecover);
        }
        
        private void Update()
        {
            helpBtn.SetActive(PlayerController.main.player.Value is Astronaut_EVA);
            bpc.SetSelected(WorldBuildManager.main.worldBuildActive);

            topBar.SetActive(!(PlayerController.main.player.Value is Astronaut_EVA));

            try
            {
                selectResourceSourceBtn.gameObject.SetActive(false);
                selectResourceSourceBtn.gameObject.SetActive(PlayerController.main.player.Value is Astronaut_EVA
                                                             && GameSelector.main.selected_World.Value is MapRocket
                                                             && CapsuleScanner.main
                                                                 .FindBest(
                                                                     GameSelector.main.selected_World.Value
                                                                         .As<MapRocket>().rocket, Vector2.zero,
                                                                     float.PositiveInfinity).cm != null);
            }
            catch (NullReferenceException)
            {
            }
            
            if (selectResourceSourceBtn != null) return;

            var go = GameObject.Find("Swtch To Button");
            if (go == null) return;

            selectResourceSourceBtn = Instantiate(go).GetComponent<ButtonPC>();
            selectResourceSourceBtn.transform.parent = go.transform.parent;
            selectResourceSourceBtn.transform.localScale = Vector3.one;
            selectResourceSourceBtn.ButtonText.text = "Set as resource source";
            selectResourceSourceBtn.onClick.Clear();
            selectResourceSourceBtn.onClick += () => { };
        }
    }
}