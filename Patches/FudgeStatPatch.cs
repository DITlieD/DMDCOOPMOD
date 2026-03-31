using HarmonyLib;
using Death.Run.Behaviours.Players;
using Death.Run.Behaviours.Entities;
using Death.Run.Behaviours.Abilities;
using Death.Run.Core;
using System.Collections.Generic;
using System.Reflection;
namespace DeathMustDieCoop.Patches
{
    public static class CoopFudgeStats
    {
        public static FudgeStats P1FudgeStats;
        public static FudgeStats P2FudgeStats;
        private static bool _initialized;
        public static Dictionary<int, int> LastMirroredBonus = new Dictionary<int, int>();
        public static FieldInfo CurBonusField;
        public static PropertyInfo ParamsProperty;
        public static FieldInfo StatField;
        public static PropertyInfo EntityProperty;
        static CoopFudgeStats()
        {
            try
            {
                var abilityType = typeof(Ability_FudgeStat);
                CurBonusField = abilityType.GetField("_curBonus",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                ParamsProperty = abilityType.GetProperty("Params",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var dataType = abilityType.GetNestedType("Data");
                if (dataType != null)
                    StatField = dataType.GetField("Stat",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                EntityProperty = abilityType.GetProperty("Entity",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                CoopPlugin.FileLog($"CoopFudgeStats: Reflection init — CurBonus={CurBonusField != null}, Params={ParamsProperty != null}, Stat={StatField != null}, Entity={EntityProperty != null}");
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"CoopFudgeStats: Reflection init ERROR: {ex.Message}");
            }
        }
        public static void Init()
        {
            P1FudgeStats = new FudgeStats();
            P2FudgeStats = new FudgeStats();
            LastMirroredBonus.Clear();
            _initialized = true;
            CoopPlugin.FileLog("CoopFudgeStats: Initialized per-player FudgeStats.");
        }
        public static void Reset()
        {
            P1FudgeStats = null;
            P2FudgeStats = null;
            LastMirroredBonus.Clear();
            _initialized = false;
            CoopPlugin.FileLog("CoopFudgeStats: Reset.");
        }
        public static bool IsActive => _initialized && PlayerRegistry.Count >= 2;
        public static FudgeStats GetForPlayer(Behaviour_Player player)
        {
            if (!IsActive || player == null)
                return null;
            if (player == PlayerRegistry.GetPlayer(0))
                return P1FudgeStats;
            if (player == PlayerRegistry.GetPlayer(1))
                return P2FudgeStats;
            return null; 
        }
        public static FudgeStats GetForActiveRewardPlayer()
        {
            if (!IsActive) return null;
            var active = CoopRewardState.ActiveRewardPlayer;
            if (active == null) active = PlayerRegistry.GetPlayer(0);
            return GetForPlayer(active);
        }
        public static bool IsTeamStats(FudgeStats stats)
        {
            if (stats == null) return false;
            try { return stats == Teams.Get(TeamId.Player).FudgeStats; }
            catch { return false; }
        }
        public static bool ShouldRedirect(FudgeStats instance, FudgeStatId stat)
        {
            if (!IsActive) return false;
            if (stat == FudgeStatId.Revivals) return false;
            if (CoopRewardState.ActiveRewardPlayer == null) return false;
            return IsTeamStats(instance);
        }
        public static FudgeStatId GetAbilityStat(object abilityInstance)
        {
            var data = ParamsProperty.GetValue(abilityInstance, null);
            return (FudgeStatId)StatField.GetValue(data);
        }
        public static Behaviour_Player GetAbilityOwnerPlayer(object abilityInstance)
        {
            var entity = (Entity)EntityProperty.GetValue(abilityInstance, null);
            if (entity != null)
                return entity.GetComponent<Behaviour_Player>();
            return null;
        }
        public static void LogState(string context)
        {
            if (!IsActive) return;
            try
            {
                var teamStats = Teams.Get(TeamId.Player).FudgeStats;
                string team = $"B={teamStats.GetMax(FudgeStatId.Banish)} R={teamStats.GetMax(FudgeStatId.Reroll)} A={teamStats.GetMax(FudgeStatId.Alteration)}";
                string p1 = P1FudgeStats != null ? $"B={P1FudgeStats.GetMax(FudgeStatId.Banish)} R={P1FudgeStats.GetMax(FudgeStatId.Reroll)} A={P1FudgeStats.GetMax(FudgeStatId.Alteration)}" : "null";
                string p2 = P2FudgeStats != null ? $"B={P2FudgeStats.GetMax(FudgeStatId.Banish)} R={P2FudgeStats.GetMax(FudgeStatId.Reroll)} A={P2FudgeStats.GetMax(FudgeStatId.Alteration)}" : "null";
                CoopPlugin.FileLog($"FudgePatch STATE [{context}]: TeamMax=[{team}] P1Max=[{p1}] P2Max=[{p2}] Active={CoopRewardState.ActiveRewardPlayer?.name ?? "null"}");
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"FudgePatch STATE ERROR: {ex.Message}");
            }
        }
    }
    [HarmonyPatch]
    public static class FudgeStat_UpdateBonus_Patch
    {
        static MethodBase TargetMethod()
        {
            return typeof(Ability_FudgeStat).GetMethod("UpdateBonus",
                BindingFlags.NonPublic | BindingFlags.Instance);
        }
        static void Postfix(object __instance)
        {
            if (!CoopFudgeStats.IsActive) return;
            if (CoopFudgeStats.CurBonusField == null) return;
            try
            {
                int curBonus = (int)CoopFudgeStats.CurBonusField.GetValue(__instance);
                var stat = CoopFudgeStats.GetAbilityStat(__instance);
                if (stat == FudgeStatId.Revivals) return;
                var player = CoopFudgeStats.GetAbilityOwnerPlayer(__instance);
                var perPlayer = CoopFudgeStats.GetForPlayer(player);
                if (perPlayer == null) return;
                int key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(__instance);
                int lastBonus = CoopFudgeStats.LastMirroredBonus.ContainsKey(key)
                    ? CoopFudgeStats.LastMirroredBonus[key] : 0;
                int delta = curBonus - lastBonus;
                CoopFudgeStats.LastMirroredBonus[key] = curBonus;
                if (delta != 0)
                {
                    perPlayer.AddMax(stat, delta);
                    CoopPlugin.FileLog($"FudgePatch: UpdateBonus mirror — {stat} delta={delta} (was={lastBonus} now={curBonus}) -> {player?.name ?? "?"}");
                    CoopFudgeStats.LogState("after UpdateBonus");
                }
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"FudgePatch: UpdateBonus postfix ERROR: {ex.Message}");
            }
        }
    }
    [HarmonyPatch(typeof(Ability_FudgeStat), "Lose")]
    public static class FudgeStat_Lose_Patch
    {
        static void Prefix(object __instance)
        {
            if (!CoopFudgeStats.IsActive) return;
            if (CoopFudgeStats.CurBonusField == null) return;
            try
            {
                var stat = CoopFudgeStats.GetAbilityStat(__instance);
                if (stat == FudgeStatId.Revivals) return;
                var player = CoopFudgeStats.GetAbilityOwnerPlayer(__instance);
                var perPlayer = CoopFudgeStats.GetForPlayer(player);
                if (perPlayer == null) return;
                int key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(__instance);
                int lastBonus = CoopFudgeStats.LastMirroredBonus.ContainsKey(key)
                    ? CoopFudgeStats.LastMirroredBonus[key] : 0;
                if (lastBonus != 0)
                {
                    perPlayer.AddMax(stat, -lastBonus);
                    CoopPlugin.FileLog($"FudgePatch: Lose mirror — {stat} removing {lastBonus} from {player?.name ?? "?"}");
                }
                CoopFudgeStats.LastMirroredBonus.Remove(key);
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"FudgePatch: Lose prefix ERROR: {ex.Message}");
            }
        }
    }
    [HarmonyPatch(typeof(FudgeStats), "Get")]
    public static class FudgeStats_Get_Redirect
    {
        static bool Prefix(FudgeStats __instance, FudgeStatId stat, ref int __result)
        {
            if (!CoopFudgeStats.ShouldRedirect(__instance, stat)) return true;
            var perPlayer = CoopFudgeStats.GetForActiveRewardPlayer();
            if (perPlayer == null) return true;
            __result = perPlayer.Get(stat);
            return false;
        }
    }
    [HarmonyPatch(typeof(FudgeStats), "HadStat")]
    public static class FudgeStats_HadStat_Redirect
    {
        static bool Prefix(FudgeStats __instance, FudgeStatId stat, ref bool __result)
        {
            if (!CoopFudgeStats.ShouldRedirect(__instance, stat)) return true;
            var perPlayer = CoopFudgeStats.GetForActiveRewardPlayer();
            if (perPlayer == null) return true;
            __result = perPlayer.HadStat(stat);
            return false;
        }
    }
    [HarmonyPatch(typeof(FudgeStats), "Use")]
    public static class FudgeStats_Use_Mirror
    {
        static void Postfix(FudgeStats __instance, FudgeStatId stat)
        {
            if (!CoopFudgeStats.IsActive) return;
            if (stat == FudgeStatId.Revivals) return;
            if (!CoopFudgeStats.IsTeamStats(__instance)) return;
            if (CoopRewardState.ActiveRewardPlayer == null) return;
            var perPlayer = CoopFudgeStats.GetForActiveRewardPlayer();
            if (perPlayer != null && perPlayer != __instance)
            {
                perPlayer.Use(stat);
                CoopPlugin.FileLog($"FudgePatch: Use({stat}) mirrored to {CoopRewardState.ActiveRewardPlayer?.name}");
            }
        }
    }
}