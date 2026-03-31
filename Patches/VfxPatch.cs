using HarmonyLib;
using Death.Run.Core;
using Death.Run.Core.Entities;
using UnityEngine;
namespace DeathMustDieCoop.Patches
{
    [HarmonyPatch(typeof(Entity), nameof(Entity.GetVfxPosition))]
    public static class VfxPatch
    {
        private const float PowerupIconYOffset = 0.5f;
        static void Postfix(ref Vector2 __result, VfxPlacement placement)
        {
            if (PlayerRegistry.Count > 1 && placement == VfxPlacement.Overhead)
            {
                __result.y += PowerupIconYOffset;
            }
        }
    }
}