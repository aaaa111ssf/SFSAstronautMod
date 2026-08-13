using System;
using SFS.UI.ModGUI;
using SFS.World;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SFS.Parts.Modules;
using UnityEngine;
using WorldBuild.Mod.UI;
using WorldBuild.Mod.Managers;
using WorldBuild.Mod.Saving;
using SFS.UI;

namespace WorldBuild.Mod.Modules
{
    public class Astronaut : InjectEverywhereWith<Astronaut_EVA>
    {
        public double maxOxygen = 300;

        public double materialLeft = 0;
        public const float maxMaterial = 20f;

        private double oxygenSeconds = double.NegativeInfinity;

        private double lastTime;
        public bool BreathingAir;

        public double GetOxygenSecondsLeft()
        {
            if (oxygenSeconds == double.NegativeInfinity) oxygenSeconds = maxOxygen;
            return oxygenSeconds;
        }

        private void Start()
        {
            lastTime = WorldTime.main.worldTime;
        }

        private void Update()
        {
            if (TargetComponent.CanPickItselfUp && TargetComponent.ragdollTime > 2)
            {
                TargetComponent.SetRagdoll(false);
            }

            if (GetOxygenSecondsLeft() <= 0 || !TargetComponent.astronaut.alive)
            {
                MsgDrawer.main.Log("WorldBuild astronaut is dead");
                AstronautSpawner.main.EndEVAAndReturnToRocket(true);
                TargetComponent.astronaut.alive = true;
            }

            var loc = TargetComponent.location.Value;

            var planet = loc.planet;

            var atmoDensity = planet.GetAtmosphericDensity(TargetComponent.location.Value.Height);

            BreathingAir = true;

            // I assume that earth's 0.005 atmo density = 1 atm, the atmo breathing limits are 0.5-2 atm
            if (!(atmoDensity > 0.0025 && atmoDensity < 0.01 && planet.data.atmosphereVisuals.GRADIENT.texture == "Atmo_Earth"))
            {
                oxygenSeconds -= WorldTime.main.worldTime - lastTime;
                BreathingAir = false;
            }

            AstronautDataHelper.main.SaveData.position = loc.position;
            AstronautDataHelper.main.SaveData.speed = loc.velocity;
            AstronautDataHelper.main.SaveData.planetName = loc.planet.codeName;
            AstronautDataHelper.main.SaveData.isCurrentPlayer = true;
            AstronautDataHelper.main.SaveData.fuelPercent = TargetComponent.resources.fuelPercent.Value;
            AstronautDataHelper.main.SaveData.oxygen = GetOxygenSecondsLeft();
            AstronautDataHelper.main.SaveData.temperature = TargetComponent.resources.temperature.Value;
            AstronautDataHelper.main.SaveData.rotationSpeed = TargetComponent.rb2d.angularVelocity;
            AstronautDataHelper.main.SaveData.materialLeft = 0f; // TODO

            lastTime = WorldTime.main.worldTime;

            //(AstronautManagementGUI.main.Elements["oxygenBarSlider"] as Slider).Value = (float) (GetOxygenSecondsLeft() / oxygenSeconds) * 100;
        }

        //private IEnumerator TimeEstimateTextCoro()
        //{
        //    while (true)
        //    {
        //        int secondsLeft = (int) (GetOxygenSecondsLeft() * Random.Range(0.95f, 1.05f));
        //        int minutesLeft = secondsLeft / 60;
        //        int hoursLeft = secondsLeft / 3600;

        //        try
        //        {
        //        } catch (KeyNotFoundException)
        //        {
        //            Debugger.Log("this is fucked up");
        //        }
        //        yield return new WaitForSecondsRealtime(1f);
        //    }
        //}
    }
}
