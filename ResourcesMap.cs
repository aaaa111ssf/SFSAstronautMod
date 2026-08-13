using System.Collections.Generic;

namespace WorldBuild.Mod
{
    public struct PlanetResourceData
    {
        public float oilMultiplier;
        public float oreMultiplier;
    }

    public static class ResourcesMap
    {
        private static Dictionary<string, PlanetResourceData> planetResources =
            new Dictionary<string, PlanetResourceData>()
            {
                {
                    "Mercury", new PlanetResourceData()
                    {
                        oilMultiplier = 1.2f,
                        oreMultiplier = 1.2f
                    }
                }
            };


        public static float GetOreAt(string planetName, float angle)
        {
            if (!planetResources.TryGetValue(planetName, out var resource)) return 0;
            
            return resource.oreMultiplier;
        }

        public static float GetOilAt(string planetName, float angle)
        {
            if (!planetResources.TryGetValue(planetName, out var resource)) return 0;
            
            return resource.oilMultiplier;
        }
    }
}