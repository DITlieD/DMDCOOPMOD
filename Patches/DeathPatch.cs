using HarmonyLib;
using Death.Run.Systems;
using Death.Run.Systems.Spawning;
using Death.Run.Behaviours;
using Death.Run.Behaviours.Entities;
using Death.Run.Behaviours.Players;
using Death.Run.Core;
using Death.Run.UserInterface.HUD;
using UnityEngine;
using System.Collections;
namespace DeathMustDieCoop.Patches
{
    [HarmonyPatch(typeof(System_EndGameTracker), "OnPlayerDied")]
    public static class EndGameTracker_OnPlayerDied_Patch
    {
        static bool Prefix(System_EndGameTracker __instance)
        {
            if (PlayerRegistry.Count < 2) return true; 
            bool anyAlive = PlayerRegistry.AnyAlive;
            CoopPlugin.FileLog($"DeathPatch: OnPlayerDied — AnyAlive={anyAlive}");
            if (anyAlive)
            {
                var runtime = CoopRuntime.Instance;
                if (runtime != null)
                    runtime.StartCoroutine(CleanupDeadPlayers());
                return false;
            }
            return true;
        }
        private static IEnumerator CleanupDeadPlayers()
        {
            var allOverhead = Object.FindObjectsOfType<GUI_PlayerOverheadBars>();
            CoopPlugin.FileLog($"DeathPatch: Found {allOverhead.Length} GUI_PlayerOverheadBars instances.");
            foreach (var oh in allOverhead)
            {
                CoopPlugin.FileLog($"DeathPatch: Overhead bar: name={oh.name}");
                if (oh.name.Contains("P2"))
                {
                    Object.Destroy(oh.gameObject);
                    CoopPlugin.FileLog($"DeathPatch: Destroyed {oh.name}");
                }
            }
            var allXpBars = Object.FindObjectsOfType<CoopXpBar>();
            foreach (var xpBar in allXpBars)
            {
                var player = Traverse.Create(xpBar).Field("_player").GetValue<Behaviour_Player>();
                if (player != null && player.Entity != null && !player.Entity.IsAlive)
                {
                    Object.Destroy(xpBar.gameObject);
                    CoopPlugin.FileLog($"DeathPatch: Destroyed CoopXpBar for dead player.");
                }
            }
            var allHealthBars = Object.FindObjectsOfType<GUI_HealthBar>();
            CoopPlugin.FileLog($"DeathPatch: Found {allHealthBars.Length} GUI_HealthBar instances total.");
            foreach (var hb in allHealthBars)
            {
                var target = Traverse.Create(hb).Field("_target").GetValue<Entity>();
                if (target != null && target.IsDead)
                {
                    bool isPlayerEntity = false;
                    for (int i = 0; i < PlayerRegistry.Players.Count; i++)
                    {
                        var p = PlayerRegistry.Players[i];
                        if (p != null && p.Entity == target)
                        {
                            isPlayerEntity = true;
                            break;
                        }
                    }
                    if (isPlayerEntity)
                    {
                        Object.Destroy(hb.gameObject);
                        CoopPlugin.FileLog($"DeathPatch: Destroyed GUI_HealthBar targeting dead player entity.");
                    }
                }
                else
                {
                    string targetName = target != null ? target.name : "null";
                    bool alive = target != null && !target.IsDead;
                    CoopPlugin.FileLog($"DeathPatch: GUI_HealthBar target={targetName} alive={alive} active={hb.gameObject.activeSelf}");
                }
            }
            var allChargeBars = Object.FindObjectsOfType<GUI_ChargeBar>();
            CoopPlugin.FileLog($"DeathPatch: Found {allChargeBars.Length} GUI_ChargeBar instances total.");
            foreach (var cb in allChargeBars)
            {
                var unit = Traverse.Create(cb).Field("_unit").GetValue<Behaviour_Unit>();
                if (unit != null && unit.Entity != null && unit.Entity.IsDead)
                {
                    bool isPlayerUnit = false;
                    for (int i = 0; i < PlayerRegistry.Players.Count; i++)
                    {
                        var p = PlayerRegistry.Players[i];
                        if (p != null && p.Unit == unit)
                        {
                            isPlayerUnit = true;
                            break;
                        }
                    }
                    if (isPlayerUnit)
                    {
                        Object.Destroy(cb.gameObject);
                        CoopPlugin.FileLog($"DeathPatch: Destroyed GUI_ChargeBar targeting dead player unit.");
                    }
                }
            }
            yield return new WaitForSeconds(2f);
            for (int i = 0; i < PlayerRegistry.Players.Count; i++)
            {
                var p = PlayerRegistry.Players[i];
                if (p != null && p.Entity != null && !p.Entity.IsAlive)
                {
                    CoopPlugin.FileLog($"DeathPatch: Hiding dead player {i} body.");
                    var renderers = p.GetComponentsInChildren<SpriteRenderer>();
                    float fadeDuration = 1f;
                    float elapsed = 0f;
                    while (elapsed < fadeDuration)
                    {
                        elapsed += Time.deltaTime;
                        float alpha = 1f - (elapsed / fadeDuration);
                        foreach (var sr in renderers)
                        {
                            if (sr != null)
                            {
                                var c = sr.color;
                                c.a = alpha;
                                sr.color = c;
                            }
                        }
                        yield return null;
                    }
                    if (p != null && p.gameObject != null)
                        p.gameObject.SetActive(false);
                    CoopPlugin.FileLog($"DeathPatch: Dead player {i} body hidden.");
                }
            }
        }
    }
    [HarmonyPatch(typeof(System_Revivals), "OnBegin")]
    public static class SystemRevivals_OnBegin_Patch
    {
        static void Postfix(System_Revivals __instance)
        {
            if (PlayerRegistry.Count < 2) return;
            var p2 = PlayerRegistry.GetPlayer(1);
            if (p2 == null || p2.Unit == null) return;
            try
            {
                var trav = Traverse.Create(__instance);
                int remaining = trav.Property("RemainingRevivals").GetValue<int>();
                CoopPlugin.FileLog($"DeathPatch: Hooking P2 death handler override. RemainingRevivals={remaining}");
                var overrideDeathMethod = trav.Method("OverrideDeath",
                    new System.Type[] { typeof(Behaviour_Unit), typeof(Behaviour_Unit.DeathData) });
                var methodInfo = typeof(System_Revivals).GetMethod("OverrideDeath",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (methodInfo != null)
                {
                    var deathAction = (System.Action<Behaviour_Unit, Behaviour_Unit.DeathData>)
                        System.Delegate.CreateDelegate(
                            typeof(System.Action<Behaviour_Unit, Behaviour_Unit.DeathData>),
                            __instance, methodInfo);
                    p2.Unit.DeathHandler.Override(deathAction, 3, () =>
                        trav.Property("RemainingRevivals").GetValue<int>() > 0);
                    CoopPlugin.FileLog("DeathPatch: P2 death handler override hooked successfully.");
                }
                else
                {
                    CoopPlugin.FileLog("DeathPatch: ERROR — Could not find OverrideDeath method.");
                }
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"DeathPatch: ERROR hooking P2 revival: {ex}");
            }
        }
    }
    [HarmonyPatch(typeof(System_MonsterManager), "OnPlayerDied")]
    public static class MonsterManager_OnPlayerDied_Patch
    {
        static bool Prefix()
        {
            if (PlayerRegistry.Count < 2) return true; 
            bool anyAlive = PlayerRegistry.AnyAlive;
            CoopPlugin.FileLog($"DeathPatch: MonsterManager.OnPlayerDied — AnyAlive={anyAlive}, skipping={anyAlive}");
            return !anyAlive;
        }
    }
    [HarmonyPatch(typeof(System_EndGameTracker), "CompleteObjective")]
    public static class EndGameTracker_CompleteObjective_Patch
    {
        static void Postfix()
        {
            if (PlayerRegistry.Count < 2) return;
            for (int i = 1; i < PlayerRegistry.Players.Count; i++)
            {
                var p = PlayerRegistry.Players[i];
                if (p != null && p.Entity != null && p.Entity.IsAlive)
                {
                    p.Entity.Invulnerable.AddStack();
                    CoopPlugin.FileLog($"DeathPatch: Made player {i} invulnerable on objective complete.");
                }
            }
        }
    }
}