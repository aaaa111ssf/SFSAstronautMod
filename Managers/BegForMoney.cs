using ModLoader;
using SFS.Input;
using SFS.UI;
using UnityEngine;

namespace WorldBuild.Mod.Managers
{
    public class BegForMoney : BaseManager<BegForMoney>
    {
        private string message =
            "Seems like you're enjoying WorldBuild so far. At least we hope so - we put hundreds of hours over the course of almost a year into designing parts, \ncoding their behavior, astronauts, and so on.\n\nThis mod is free and open-source, and we did it as volunteers, who just want to help the SFS community.\n\nHowever, we're still humans. Humans, who just like you, need money to live (and pay the electricity bills) and motivation to keep working on this project.\n\nMaybe you don't want to or can't donate for whatever reason (maybe you don't like the mod, you have no means to pay with, or maybe you just don't want to pay for something that is free), that's fine.\n\nHowever, if you do want to donate, just click the button below, it'll take you to out Ko-Fi page. We'll be super grateful, no matter how much (or little) you donate.\n\nIf you intend to donate, but not right now, there is also a Donate button in the Mods menu.\n\nAlso a side note, you'll get a mention in the credits, unless you specifically state that you don't want it ;)\n\nThanks for reading this wall of text\n- WorldBuild developers";

        void Start()
        {
            if (PlayerPrefs.GetInt("WORLDBUILD_STATS_EVA_COUNT", 0) > 5 &&
                !PlayerPrefs.HasKey("WORLDBUILD_CLOSED_DONATE_MENU"))
            {
                MenuGenerator.OpenConfirmation(CloseMode.Current, () => message, () => "Donate",
                    () => { Application.OpenURL("https://github.com/VerdiX094/WorldBuild/"); });
                PlayerPrefs.SetInt("WORLDBUILD_CLOSED_DONATE_MENU", 0);
            }
        }
    }
}