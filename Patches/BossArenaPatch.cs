using HarmonyLib;
using Death.Run.Behaviours;
using Death.Run.Behaviours.Players;
using UnityEngine;
namespace DeathMustDieCoop.Patches
{
    [HarmonyPatch(typeof(BossArena), "Start")]
    public static class BossArena_Start_Patch
    {
        private const float MinPadding = 2f;
        private static Vector2? _pendingMidpoint;
        public static void Prefix(BossArena __instance)
        {
            if (PlayerRegistry.Count < 2) return;
            var players = PlayerRegistry.Players;
            Vector2 p1Pos = Vector2.zero;
            Vector2 p2Pos = Vector2.zero;
            int aliveCount = 0;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p != null && p.Entity != null && p.Entity.IsAlive)
                {
                    if (aliveCount == 0) p1Pos = (Vector2)p.transform.position;
                    else if (aliveCount == 1) p2Pos = (Vector2)p.transform.position;
                    aliveCount++;
                }
            }
            if (aliveCount < 2) return;
            Vector2 midpoint = (p1Pos + p2Pos) * 0.5f;
            float halfDist = Vector2.Distance(p1Pos, p2Pos) * 0.5f;
            float originalRadius = __instance.Radius;
            float neededRadius = halfDist + MinPadding;
            if (neededRadius > originalRadius)
            {
                float scale = neededRadius / originalRadius;
                __instance.Radius = neededRadius;
                if (__instance.InsideRadius > 0f)
                    __instance.InsideRadius *= scale;
                __instance.ActionRadius *= scale;
                if (__instance.BarrierRoot != null)
                    __instance.BarrierRoot.transform.localScale *= scale;
                CoopPlugin.FileLog($"BossArenaPatch: expanded radius {originalRadius:F1} -> {neededRadius:F1} " +
                    $"(scale {scale:F2}), halfDist={halfDist:F1}");
            }
            else
            {
                CoopPlugin.FileLog($"BossArenaPatch: players fit in default radius {originalRadius:F1}, recentering only");
            }
            _pendingMidpoint = midpoint;
        }
        public static void Postfix(BossArena __instance)
        {
            if (!_pendingMidpoint.HasValue) return;
            Vector2 mid = _pendingMidpoint.Value;
            __instance.transform.position = new Vector3(mid.x, mid.y, __instance.transform.position.z);
            CoopPlugin.FileLog($"BossArenaPatch: recentered arena to ({mid.x:F1}, {mid.y:F1})");
            _pendingMidpoint = null;
        }
    }
}