using System.Linq;
using SFS.World;
using UnityEngine;

namespace WorldBuild.Mod.Modules
{
    public class Astronaut : InjectEverywhereWith<Astronaut_EVA>
    {
        public double maxOxygen = 300d;

        private double oxygenSeconds = double.NaN;
        private double lastTime;

        public double GetOxygenSecondsLeft()
        {
            if (double.IsNaN(oxygenSeconds)) oxygenSeconds = maxOxygen;
            return oxygenSeconds;
        }

        private void Start()
        {
            lastTime = WorldTime.main != null ? WorldTime.main.worldTime : 0d;
            ProvisionFromNearestRocket();
        }

        private void ProvisionFromNearestRocket()
        {
            // EVA 默认带满氧气，可从最近火箭补充。
            oxygenSeconds = maxOxygen;
            RefillOxygenFromNearestRocket();
        }

        public void RefillOxygenFromNearestRocket()
        {
            if (TargetComponent == null || GameManager.main == null) return;

            Rocket rocket = GameManager.main.rockets
                .Where(r => r != null)
                .OrderBy(r => ((Vector2)r.transform.position - (Vector2)TargetComponent.transform.position).sqrMagnitude)
                .FirstOrDefault();
            if (rocket == null) return;

            var resources = rocket.GetComponent<RocketResources>();
            if (resources == null)
            {
                Debugger.Log("[WB-OXYGEN] Nearest rocket oxygen system is still initializing.");
                return;
            }

            double current = GetOxygenSecondsLeft();
            double needed = System.Math.Max(0d, maxOxygen - current);
            if (needed < 0.5d) return;

            double suppliedOxygen = resources.RequestResource(needed);
            if (suppliedOxygen > 0d)
                oxygenSeconds = System.Math.Min(maxOxygen, current + suppliedOxygen);
        }

        private void Update()
        {
            if (TargetComponent == null || WorldTime.main == null) return;

            var location = TargetComponent.location?.Value;
            if (location == null || location.planet == null) return;

            double now = WorldTime.main.worldTime;
            double elapsed = System.Math.Max(0d, now - lastTime);
            lastTime = now;

            double density = location.planet.GetAtmosphericDensity(location.Height);
            bool breathable = density > 0.0025d && density < 0.01d &&
                location.planet.data.atmosphereVisuals.GRADIENT.texture == "Atmo_Earth";
            if (!breathable)
                oxygenSeconds = System.Math.Max(0d, GetOxygenSecondsLeft() - elapsed);
        }
    }
}
