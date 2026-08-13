using SFS.Career;
using SFS.Translations;
using SFS.UI;
using SFS.World;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using WorldBuild.Mod.Modules;
using WorldBuild.Mod.Saving;
using WorldBuild.Mod.UI;
using WorldBuild.Mod.Build;

namespace WorldBuild.Mod.Managers
{
    public class AstronautSpawner : WorldManager<AstronautSpawner>
    {
        public Rocket lastRocket;

        public Astronaut_EVA eva;

        public bool isRocket
        {
            get
            {
                return PlayerController.main.player.Value is Rocket;
            }
        }

        public bool CanSpawnEVA()
        {
            if (!(PlayerController.main.player.Value is Rocket rocket))
                return false;

            return rocket.partHolder.parts.Any(part => part.Name == "Capsule" || part.GetComponent<CrewModule>() != null);
        }

        public void EndEVAAndReturnToRocket(bool death = false)
        {
            AstronautManager.DestroyEVA(eva, death);

            PlayerController.main.player.Value = lastRocket;
            
            AstronautDataHelper.main.SaveData.evaActive = false;
        }

        public void EndEVA(Rocket rocket)
        {
            if (!(PlayerController.main.player.Value is Astronaut_EVA curEva)) return;

            rocket.GetComponent<RocketResources>().ReturnResource(eva.GetComponent<Astronaut>().GetOxygenSecondsLeft());
            rocket.GetComponent<RocketResources>().ReturnResource(eva.GetComponent<Astronaut>().materialLeft, true, RocketResources.ResourceType.BuildResource);

            AstronautManager.DestroyEVA(curEva, false);

            PlayerController.main.SmoothChangePlayer(rocket);
            
            AstronautDataHelper.main.SaveData.evaActive = false;
        }

        private void Start()
        {
            PlayerController.main.player.OnChange += (o, n) =>
            {
                if (n is Rocket rocket)
                    lastRocket = rocket;

                if (!(n is Astronaut_EVA))
                    WorldBuildManager.main.ExitBuild();
            };
            
            AstronautSavingManager.main.OnAstronautSpawnerInitialized();
        }
        
        public void StartEVA()
        {
            if (CapsuleScanner.main.selectedCapsule.Value.cm == null)
            {
                MsgDrawer.main.Log("No capsule selected!");
                return;
            }
            
            if (!(PlayerController.main.player.Value is Rocket rocket)) return;
            
            var ox = rocket.GetComponent<RocketResources>();

            if (!ox) return;

            if (ox.CalculateResourceAvailable() < 30)
            {
                MsgDrawer.main.Log("Not enough oxygen for at least 30 seconds of EVA");
                return;
            }
            
            var player = PlayerController.main.player.Value;
            var loc = player.location.Value;

            eva = StartAndGetEVA(new Location(loc.planet, WorldView.ToGlobalPosition(CapsuleScanner.main.selectedCapsule.Value.GetGlobalPosition()), loc.velocity), player.transform.rotation.z);
            
            IEWInjector.ForceRefresh();
            
            var astronaut = eva.GetComponent<Astronaut>();
            AstronautManagementGUI.main.OnFrame(); // refresh gui so the elements can be added to dict
            
            astronaut.maxOxygen = rocket.GetComponent<RocketResources>().RequestResource(astronaut.maxOxygen);
            astronaut.materialLeft = rocket.GetComponent<RocketResources>().RequestResource(Astronaut.maxMaterial, RocketResources.ResourceType.BuildResource);

            if (astronaut.maxOxygen.AboutEqual(-1))
            {
                AstronautManager.DestroyEVA(eva, false);
                return;
            }
            
            AstronautDataHelper.main.SaveData.evaActive = true;
            PlayerController.main.SmoothChangePlayer(eva);
            
            PlayerPrefs.SetInt("WORLDBUILD_STATS_EVA_COUNT", PlayerPrefs.GetInt("WORLDBUILD_STATS_EVA_COUNT", 0) + 1);
            PlayerPrefs.Save();
        }

        public Astronaut_EVA StartAndGetEVA(Location loc, float rotation, float angVel = 0, bool ragdoll = false, double fuelPercent = 1, float temperature = 0f)
        {
            // wtf was I thinking when writing this exact line? like wtf
            //AstronautManager manager = GameObject.Find("Astronaut Manager").GetComponent<AstronautManager>();

            if (AstronautState.main.GetAstronautByName("WorldBuild EVA") == null)
                AstronautState.main.CreateAstronaut("WorldBuild EVA");

            var spawned = AstronautManager.main.SpawnEVA("WorldBuild EVA",
                loc,
                rotation, 0, false, 1, 0);

            spawned.gameObject.name = "WorldBuild Astronaut";

            AstronautDataHelper.main.SaveData.evaActive = true;
            
            return spawned;
        }

        //private bool CheckWorld() => Utility.CheckSceneLoaded("World_PC");
    }
}