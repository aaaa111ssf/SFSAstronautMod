using System.Linq;
using ModLoader.Helpers;
using SFS;
using SFS.IO;
using SFS.Parsers.Json;
using SFS.UI;
using SFS.World;
using WorldBuild.Mod.Managers;

namespace WorldBuild.Mod.Saving
{
    public class AstronautSavingManager : BaseManager<AstronautSavingManager>
    {
        private const string SaveFileName = "AstronautSaving.txt";

        public bool astronautSwitchBlocked;
        
        public void OnAstronautSpawnerInitialized()
        {
            if (!AstronautDataHelper.main.SaveData.evaActive) return;

            var data = AstronautDataHelper.main.SaveData;
            
            var loc = new Location(Base.planetLoader.planets.First(p => p.Value.codeName == data.planetName).Value, data.position, data.speed);
            
            var eva = AstronautSpawner.main.StartAndGetEVA(loc, data.rotation, data.rotationSpeed, false, data.fuelPercent, data.temperature);

            if (astronautSwitchBlocked)
            {
                astronautSwitchBlocked = false;
                return;
            }

            if (!data.isCurrentPlayer) return;
            
            PlayerController.main.player.Value = eva;
        }
        
        public void OnSave(FolderPath path)
        {
            MsgDrawer.main.Log("Saving called");

            var saveText = JsonWrapper.ToJson(
                AstronautDataHelper.main.SaveData,
                false);
            Debugger.Log(AstronautDataHelper.main.SaveData.position.y);
            Debugger.Log(saveText);
            path.ExtendToFile(SaveFileName).WriteText(saveText);
        }

        public void OnLoad(FolderPath path)
        {
            MsgDrawer.main.Log("OnLoad called");

            var saveFile = path.ExtendToFile(SaveFileName);

            if (!saveFile.FileExists()) return;
            
            AstronautDataHelper.main.SaveData =
                JsonWrapper.FromJson<AstronautSaveData>(
                    saveFile.ReadText());

            Debugger.Log(AstronautDataHelper.main.SaveData.position.y);

            if (Utility.CheckSceneLoaded("World_PC"))
            {
                OnAstronautSpawnerInitialized();
            }
        }
    }
}
