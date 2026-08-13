using System;
using UnityEngine.Serialization;

namespace WorldBuild.Mod.Saving
{
    [Serializable]
    public struct AstronautSaveData
    {
        public bool evaActive;
        [FormerlySerializedAs("inEva")] public bool isCurrentPlayer;
        public Double2 position;
        public Double2 speed;
        public string planetName;
        public float rotation;
        public float rotationSpeed;
        public double oxygen;
        public float materialLeft;
        public double fuelPercent;
        public float temperature;
    }
}