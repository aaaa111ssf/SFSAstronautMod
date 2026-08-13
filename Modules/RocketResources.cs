using SFS.UI;
using SFS.World;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace WorldBuild.Mod.Modules
{
    public class RocketResources : InjectEverywhereWith<Rocket>
    {
        public enum ResourceType
        {
            Oxygen,
            BuildResource
        }
        // this may bug out when docking new capsules, idk

        /// <summary>
        /// Looks around the capsules and tries to match the requested amount.
        /// </summary>
        /// <param name="amount">The amount of requested oxygen</param>
        /// <returns>The actual amount of oxygen granted</returns>
        public double RequestResource(double amount, ResourceType resourceType = ResourceType.Oxygen)
        {
            double result = 0;

            var requestedLeft = amount;

            foreach (var co in GetComponentsInChildren<CapsuleResources>())
            {
                var avail = Math.Min(requestedLeft, resourceType == ResourceType.Oxygen ? co.Oxygen : co.EVARes);

                requestedLeft -= avail;

                result += avail;
            }

            if (resourceType == ResourceType.Oxygen)
            {
                if (result < 30)
                {
                    return -1;
                }
            
                if (result < amount - 0.001)
                {
                    MsgDrawer.main.Log($"Not enough oxygen for full {(int)amount.Round(0)} seconds of EVA,\nstarting with {(int)result.Round(0)}s instead");
                }
                else
                {
                    MsgDrawer.main.Log($"The rocket has {(int)(CalculateResourceAvailable() - result).Round(0)} seconds of oxygen time left.");
                }
            }

            // run this again, if the check succeeded
            requestedLeft = amount;

            foreach (var co in GetComponentsInChildren<CapsuleResources>())
            {
                var avail = Math.Min(requestedLeft, resourceType == ResourceType.Oxygen ? co.Oxygen : co.EVARes);

                requestedLeft -= avail;

                switch (resourceType)
                {
                    case ResourceType.Oxygen:
                        co.Oxygen -= avail;
                        break;
                    case ResourceType.BuildResource:
                        co.EVARes -= avail;
                        break;
                    default:
                        Debugger.Log("Ty idioto, jak robisz nowy resourcetype to dodaj go do requestresource()");
                        break;
                }
            }

            return result.Round(3);
        }

        public double CalculateResourceAvailable(ResourceType resourceType = ResourceType.Oxygen)
        {
            double result = 0;

            foreach (var co in GetComponentsInChildren<CapsuleResources>())
            {
                result += resourceType == ResourceType.Oxygen ? co.Oxygen : co.EVARes;
            }

            return result;
        }

        /// <summary>
        /// Looks around capsules and returns a given amount of oxygen to them.
        /// </summary>
        /// <param name="amount">The amount of oxygen to return</param>
        /// <returns>The amount of oxygen wasted</returns>
        public double ReturnResource(double amount, bool logWaste = true, ResourceType resourceType = ResourceType.Oxygen)
        {
            var resourceLeft = amount;

            foreach (var co in GetComponentsInChildren<CapsuleResources>())
            {
                if (resourceLeft < 0.001) break;
                var toReturn = Math.Min(resourceLeft, resourceType == ResourceType.Oxygen ? CapsuleResources.MaxOxygen - co.Oxygen : CapsuleResources.MaxEVARes - co.EVARes);

                switch (resourceType)
                {
                    case ResourceType.Oxygen:
                        co.Oxygen += toReturn;
                        break;
                    case ResourceType.BuildResource:
                        co.EVARes += toReturn;
                        break;
                    default:
                        Debugger.Log("Ty idioto, jak robisz nowy resourcetype to dodaj go do returnresource()");
                        break;
                }
                resourceLeft -= toReturn;
            }

            if (logWaste && resourceLeft > 1)
                MsgDrawer.main.Log($"The rocket's {(resourceType == ResourceType.Oxygen ? "oxygen" : "resource")} tanks are full, {resourceLeft.Round(1)}{(resourceType == ResourceType.Oxygen ? "s of oxygen" : " of resources")} was wasted.");


            return resourceLeft.Round(3);
        }
    }
}
