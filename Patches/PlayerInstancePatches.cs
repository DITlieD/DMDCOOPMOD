using HarmonyLib;
using Death.Run.Behaviours.Players;
using Death.Run.Behaviours;
using Death.Run.Behaviours.Entities;
using Death.Run.Behaviours.Objects;
using Death.Run.Core;
using Death.Run.Core.Entities;
using Death.Run.Systems;
using Death.Run.Systems.Spawning;
using Death.Run.Core.Systems;
using Death.Darkness;
using UnityEngine;
using System.Reflection;
namespace DeathMustDieCoop.Patches
{
    public static class PlayerContext
    {
        private static FieldInfo _gPlayerField;
        private static FieldInfo _gTransformField;
        private static bool _cached;
        private static Behaviour_Player _savedPlayer;
        private static Transform _savedTransform;
        private static bool _swapped;
        private static void EnsureCache()
        {
            if (_cached) return;
            _cached = true;
            var flags = BindingFlags.NonPublic | BindingFlags.Static;
            _gPlayerField = typeof(Player).GetField("gPlayer", flags);
            _gTransformField = typeof(Player).GetField("gTransform", flags);
        }
        public static void SwapTo(Behaviour_Player player)
        {
            if (player == null || _swapped) return;
            EnsureCache();
            if (_gPlayerField == null) return;
            _savedPlayer = _gPlayerField.GetValue(null) as Behaviour_Player;
            _savedTransform = _gTransformField?.GetValue(null) as Transform;
            _swapped = true;
            _gPlayerField.SetValue(null, player);
            _gTransformField?.SetValue(null, player.transform);
        }
        public static void Restore()
        {
            if (!_swapped) return;
            _swapped = false;
            EnsureCache();
            _gPlayerField?.SetValue(null, _savedPlayer);
            _gTransformField?.SetValue(null, _savedTransform);
            _savedPlayer = null;
            _savedTransform = null;
        }
        public static Behaviour_Player Override => _swapped ? (_gPlayerField?.GetValue(null) as Behaviour_Player) : null;
    }
    [HarmonyPatch]
    public static class AbilityEffect_Trigger_Patch
    {
        static bool Prepare()
        {
            var methods = TargetMethods();
            return methods != null && methods.GetEnumerator().MoveNext();
        }
        static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
        {
            var asm = typeof(Death.Game).Assembly;
            var baseType = asm.GetType("Death.Run.Behaviours.Abilities.AbilityEffect");
            if (baseType == null)
            {
                CoopPlugin.FileLog("PlayerInstancePatch: AbilityEffect type not found!");
                yield break;
            }
            int count = 0;
            foreach (var type in asm.GetTypes())
            {
                if (type.IsAbstract || !baseType.IsAssignableFrom(type)) continue;
                var method = type.GetMethod("Trigger",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                    null,
                    new System.Type[] {
                        asm.GetType("Death.Run.Core.Abilities.IAbility"),
                        asm.GetType("Death.Run.Behaviours.Abilities.TriggerArguments")
                    },
                    null);
                if (method != null)
                {
                    count++;
                    yield return method;
                }
            }
            CoopPlugin.FileLog($"PlayerInstancePatch: AbilityEffect.Trigger — patched {count} concrete overrides.");
        }
        private static PropertyInfo _entityProp;
        private static bool _entityPropCached;
        static void Prefix(object __instance, object __0)
        {
            if (PlayerRegistry.Count < 2) return;
            try
            {
                if (!_entityPropCached)
                {
                    _entityPropCached = true;
                    var iAbilityType = typeof(Death.Game).Assembly.GetType("Death.Run.Core.Abilities.IAbility");
                    if (iAbilityType != null)
                        _entityProp = iAbilityType.GetProperty("Entity");
                }
                if (_entityProp == null) return;
                var abilityEntity = _entityProp.GetValue(__0) as Entity;
                if (abilityEntity != null)
                {
                    var player = abilityEntity.GetComponent<Behaviour_Player>();
                    if (player != null)
                    {
                        PlayerContext.SwapTo(player);
                    }
                }
            }
            catch { }
        }
        static void Postfix()
        {
            PlayerContext.Restore();
        }
    }
    [HarmonyPatch]
    public static class Ability_Gain_Patch
    {
        static bool Prepare()
        {
            try { return TargetMethod() != null; }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"PlayerInstancePatch: AbilityBase.Gain Prepare failed: {ex.Message}");
                return false;
            }
        }
        static MethodBase TargetMethod()
        {
            var type = typeof(Death.Game).Assembly.GetType("Death.Run.Behaviours.Abilities.AbilityBase");
            if (type != null)
            {
                var method = type.GetMethod("Gain", BindingFlags.Public | BindingFlags.Instance, null, System.Type.EmptyTypes, null);
                if (method != null)
                {
                    CoopPlugin.FileLog("PlayerInstancePatch: AbilityBase.Gain resolved.");
                    return method;
                }
            }
            CoopPlugin.FileLog("PlayerInstancePatch: AbilityBase.Gain NOT found, skipping.");
            return null;
        }
        private static PropertyInfo _entityProp;
        private static bool _entityPropCached;
        static void Prefix(object __instance)
        {
            if (PlayerRegistry.Count < 2) return;
            try
            {
                if (!_entityPropCached)
                {
                    _entityPropCached = true;
                    var baseType = typeof(Death.Game).Assembly.GetType("Death.Run.Behaviours.Abilities.AbilityBase");
                    if (baseType != null)
                        _entityProp = baseType.GetProperty("Entity");
                }
                if (_entityProp == null) return;
                var entity = _entityProp.GetValue(__instance) as Entity;
                if (entity != null)
                {
                    var player = entity.GetComponent<Behaviour_Player>();
                    if (player != null)
                        PlayerContext.SwapTo(player);
                }
            }
            catch { }
        }
        static void Postfix()
        {
            PlayerContext.Restore();
        }
    }
    [HarmonyPatch(typeof(GlobalEffect_MovementPenalty), "OnBecomeActive")]
    public static class GlobalEffect_MovementPenalty_Active_Patch
    {
        static bool Prefix(GlobalEffect_MovementPenalty __instance)
        {
            if (PlayerRegistry.Count < 2) return true;
            float value = Traverse.Create(__instance).Field("_value").GetValue<float>();
            foreach (var p in PlayerRegistry.Players)
            {
                if (p != null && p.Entity != null)
                    p.Entity.Stats.Modifier.AddBaseBonus(StatId.MovementPenaltyWhenAttacking, value);
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(GlobalEffect_MovementPenalty), "OnBecomeInactive")]
    public static class GlobalEffect_MovementPenalty_Inactive_Patch
    {
        static bool Prefix(GlobalEffect_MovementPenalty __instance)
        {
            if (PlayerRegistry.Count < 2) return true;
            float value = Traverse.Create(__instance).Field("_value").GetValue<float>();
            foreach (var p in PlayerRegistry.Players)
            {
                if (p != null && p.Entity != null)
                    p.Entity.Stats.Modifier.AddBaseBonus(StatId.MovementPenaltyWhenAttacking, 0f - value);
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(GlobalEffect_NoLifegain), "OnBecomeActive")]
    public static class GlobalEffect_NoLifegain_Active_Patch
    {
        static bool Prefix()
        {
            if (PlayerRegistry.Count < 2) return true;
            foreach (var p in PlayerRegistry.Players)
            {
                if (p != null && p.Unit != null)
                    p.Unit.VoteLifegainEnabled(vote: false);
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(GlobalEffect_NoLifegain), "OnBecomeInactive")]
    public static class GlobalEffect_NoLifegain_Inactive_Patch
    {
        static bool Prefix()
        {
            if (PlayerRegistry.Count < 2) return true;
            foreach (var p in PlayerRegistry.Players)
            {
                if (p != null && p.Unit != null)
                    p.Unit.VoteLifegainEnabled(vote: true);
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(Pushback), "BeginPushback")]
    public static class Pushback_Begin_Patch
    {
        static bool Prefix()
        {
            if (PlayerRegistry.Count < 2) return true;
            foreach (var p in PlayerRegistry.Players)
            {
                if (p != null && p.Entity != null && p.Entity.IsAlive)
                {
                    p.ColliderWithEnemies.VoteDisable();
                    p.DashPushback.VoteDisable();
                }
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(Pushback), "EndPushback")]
    public static class Pushback_End_Patch
    {
        static bool Prefix()
        {
            if (PlayerRegistry.Count < 2) return true;
            foreach (var p in PlayerRegistry.Players)
            {
                if (p != null && p.Entity != null && p.Entity.IsAlive)
                {
                    p.ColliderWithEnemies.VoteEnable();
                    p.DashPushback.VoteEnable();
                }
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(MonsterDetector), "Update")]
    public static class MonsterDetector_Update_Patch
    {
        static bool Prefix(MonsterDetector __instance)
        {
            if (PlayerRegistry.Count < 2) return true;
            var nearest = PlayerRegistry.GetNearest(__instance.transform.position);
            if (nearest != null)
            {
                __instance.transform.position = nearest.transform.position;
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(Behaviour_GemCollector), "FixedUpdate")]
    public static class GemCollector_FixedUpdate_Patch
    {
        private static bool _loggedP1State;
        private static bool _loggedP2State;
        public static void Reset()
        {
            _loggedP1State = false;
            _loggedP2State = false;
            GemCollector_Collect_Patch._p1Count = 0;
            GemCollector_Collect_Patch._p2Count = 0;
        }
        static void Prefix(Behaviour_GemCollector __instance)
        {
            if (PlayerRegistry.Count < 2) return;
            if (_loggedP1State && _loggedP2State) return;
            var player = __instance.GetComponent<Behaviour_Player>();
            if (player == null) return;
            bool isPrimary = Traverse.Create(player).Field("_isPrimaryPlayerInstance").GetValue<bool>();
            if (isPrimary && !_loggedP1State)
            {
                _loggedP1State = true;
                float radius = Traverse.Create(__instance).Property("Radius").GetValue<float>();
                float pullSpeed = Traverse.Create(__instance).Property("PullSpeed").GetValue<float>();
                float consumeRadius = Traverse.Create(__instance).Field("_consumeRadius").GetValue<float>();
                float pullArea = __instance.GetComponent<Entity>()?.Stats?.Get(StatId.PullArea) ?? -1f;
                CoopPlugin.FileLog($"GemCollector DIAG: P1 collector — enabled={__instance.enabled}, radius={radius:F3}, pullSpeed={pullSpeed:F2}, consumeRadius={consumeRadius:F3}, pullAreaStat={pullArea:F3}");
            }
            else if (!isPrimary && !_loggedP2State)
            {
                _loggedP2State = true;
                float radius = Traverse.Create(__instance).Property("Radius").GetValue<float>();
                float pullSpeed = Traverse.Create(__instance).Property("PullSpeed").GetValue<float>();
                float consumeRadius = Traverse.Create(__instance).Field("_consumeRadius").GetValue<float>();
                float pullArea = __instance.GetComponent<Entity>()?.Stats?.Get(StatId.PullArea) ?? -1f;
                CoopPlugin.FileLog($"GemCollector DIAG: P2 collector — enabled={__instance.enabled}, radius={radius:F3}, pullSpeed={pullSpeed:F2}, consumeRadius={consumeRadius:F3}, pullAreaStat={pullArea:F3}");
            }
        }
    }
    [HarmonyPatch(typeof(Behaviour_GemCollector), "Collect")]
    public static class GemCollector_Collect_Patch
    {
        public static int _p1Count;
        public static int _p2Count;
        static void Postfix(Behaviour_GemCollector __instance)
        {
            if (PlayerRegistry.Count < 2) return;
            var player = __instance.GetComponent<Behaviour_Player>();
            if (player == null) return;
            bool isPrimary = Traverse.Create(player).Field("_isPrimaryPlayerInstance").GetValue<bool>();
            if (isPrimary)
            {
                _p1Count++;
                if (_p1Count <= 3 || _p1Count % 100 == 0)
                    CoopPlugin.FileLog($"GemCollector DIAG: P1 collected gem #{_p1Count}");
            }
            else
            {
                _p2Count++;
                if (_p2Count <= 3 || _p2Count % 100 == 0)
                    CoopPlugin.FileLog($"GemCollector DIAG: P2 collected gem #{_p2Count}");
            }
        }
    }
    [HarmonyPatch(typeof(PowerUp), "Consume")]
    public static class PowerUp_Consume_Diag_Patch
    {
        static void Prefix(PowerUp __instance, Behaviour_Player player)
        {
            if (PlayerRegistry.Count < 2) return;
            bool isPrimary = Traverse.Create(player).Field("_isPrimaryPlayerInstance").GetValue<bool>();
            var data = Traverse.Create(__instance).Field("_data").GetValue<object>();
            string code = data != null ? Traverse.Create(data).Field("Code").GetValue<string>() ?? "?" : "?";
            CoopPlugin.FileLog($"PowerUp DIAG: Consume code={code}, by={player.name}, isPrimary={isPrimary}, pos={__instance.transform.position}");
        }
    }
    [HarmonyPatch(typeof(System_MonsterManager), "Update")]
    public static class MonsterManager_Update_Patch
    {
        private static FieldInfo _posRandomizerField;
        private static MethodInfo _posRandomizerUpdate;
        private static MethodInfo _cleanupMethod;
        private static MethodInfo _processSpawnsMethod;
        private static bool _cached;
        private static void EnsureCache()
        {
            if (_cached) return;
            _cached = true;
            var mmType = typeof(System_MonsterManager);
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            _posRandomizerField = mmType.GetField("_posRandomizer", flags);
            _cleanupMethod = mmType.GetMethod("CleanupEnemiesOutsideBounds", flags);
            _processSpawnsMethod = mmType.GetMethod("ProcessQueuedSpawns", flags);
            var sprType = typeof(Death.Run.Systems.Spawning.SpawnPositionRandomizer);
            _posRandomizerUpdate = sprType.GetMethod("Update", new System.Type[] { typeof(Vector2) });
        }
        static bool Prefix(System_MonsterManager __instance)
        {
            if (PlayerRegistry.Count < 2) return true;
            EnsureCache();
            Vector2 midpoint = Vector2.zero;
            int alive = 0;
            foreach (var p in PlayerRegistry.Players)
            {
                if (p != null && p.Entity != null && p.Entity.IsAlive)
                {
                    midpoint += (Vector2)p.transform.position;
                    alive++;
                }
            }
            if (alive == 0) return true; 
            midpoint /= alive;
            var posRandomizer = _posRandomizerField?.GetValue(__instance);
            if (posRandomizer != null)
                _posRandomizerUpdate?.Invoke(posRandomizer, new object[] { midpoint });
            _cleanupMethod?.Invoke(__instance, null);
            _processSpawnsMethod?.Invoke(__instance, null);
            return false; 
        }
    }
    [HarmonyPatch(typeof(System_MonsterManager), "OnPostInit")]
    public static class CoopEnemyHpScaling_Patch
    {
        static void Postfix(System_MonsterManager __instance)
        {
            if (PlayerRegistry.Count < 2) return;
            var monsterManager = RunSystems.Get<IMonsterManager>();
            if (monsterManager == null)
            {
                CoopPlugin.FileLog("CoopHpScaling: IMonsterManager not found!");
                return;
            }
            float[] hpMods = { 1.0f, 2.0f, 2.0f, 1.0f }; 
            for (int i = 0; i < 4; i++)
            {
                var typeStats = monsterManager.GetMonsterTypeStats((MonsterType)i);
                typeStats.AddBoonMod(StatId.Health, hpMods[i]);
                CoopPlugin.FileLog($"CoopHpScaling: {(MonsterType)i} → {hpMods[i] + 1}x HP (BoonMod +{hpMods[i]})");
            }
            CoopPlugin.FileLog("CoopHpScaling: spawn limit unchanged (full rate).");
        }
    }
    public static class CoopXpDoubling_Patch
    {
        private static int _count;
        private static float _totalOriginal;
        private static float _totalDoubled;
        public static void Reset()
        {
            _count = 0;
            _totalOriginal = 0f;
            _totalDoubled = 0f;
        }
        static void Prefix(ref float xp)
        {
            if (PlayerRegistry.Count < 2) return;
            float original = xp;
            xp *= 2f;
            _count++;
            _totalOriginal += original;
            _totalDoubled += xp;
            if (_count <= 5 || _count % 50 == 0)
                CoopPlugin.FileLog($"XpDouble: gem #{_count} — {original:F1} → {xp:F1} (total orig={_totalOriginal:F0}, doubled={_totalDoubled:F0})");
        }
    }
    [HarmonyPatch(typeof(System_XpDropper), "DropXp")]
    public static class XpDropDiag_DropXp_Patch
    {
        private static int _totalCalls;
        private static int _skippedAlly;
        private static int _skippedNone;
        private static int _skippedChance;
        private static int _droppedXp;
        private static int _droppedChest;
        public static void Reset()
        {
            _totalCalls = 0;
            _skippedAlly = 0;
            _skippedNone = 0;
            _skippedChance = 0;
            _droppedXp = 0;
            _droppedChest = 0;
        }
        static bool Prefix(System_XpDropper __instance, Behaviour_Monster monster)
        {
            _totalCalls++;
            if (monster.Entity.IsPlayersAlly)
            {
                _skippedAlly++;
                if (_totalCalls <= 20 || _totalCalls % 100 == 0)
                    CoopPlugin.FileLog($"XpDropDiag: #{_totalCalls} SKIP ally — {monster.Data.Code}");
                return true;
            }
            var xpDrop = monster.Data.XpDrop;
            if (_totalCalls <= 20 || _totalCalls % 100 == 0)
                CoopPlugin.FileLog($"XpDropDiag: #{_totalCalls} call — {monster.Data.Code} type={xpDrop.Type} chance={xpDrop.Chance:F2} xp={xpDrop.XpValue:F1} dropEnabled={monster.DropEnabled}");
            return true; 
        }
        static void Postfix()
        {
        }
        public static void LogSummary()
        {
            CoopPlugin.FileLog($"XpDropDiag SUMMARY: total={_totalCalls}, droppedXp={_droppedXp}, droppedChest={_droppedChest}, skippedAlly={_skippedAlly}, skippedNone={_skippedNone}, skippedChance={_skippedChance}");
        }
    }
    [HarmonyPatch(typeof(System_XpDropper), "OnMonsterDied")]
    public static class XpDropDiag_OnMonsterDied_Patch
    {
        private static int _totalDied;
        private static int _dropDisabled;
        public static void Reset()
        {
            _totalDied = 0;
            _dropDisabled = 0;
        }
        static void Prefix(object ev)
        {
            _totalDied++;
            try
            {
                var evType = ev.GetType();
                var monsterField = evType.GetField("Monster");
                if (monsterField != null)
                {
                    var monster = monsterField.GetValue(ev) as Behaviour_Monster;
                    if (monster != null && !monster.DropEnabled)
                    {
                        _dropDisabled++;
                        if (_dropDisabled <= 10 || _dropDisabled % 50 == 0)
                            CoopPlugin.FileLog($"XpDropDiag: monster died with DropEnabled=false #{_dropDisabled} — {monster.Data.Code}");
                    }
                }
            }
            catch { }
            if (_totalDied <= 5 || _totalDied % 200 == 0)
                CoopPlugin.FileLog($"XpDropDiag: OnMonsterDied #{_totalDied} (dropDisabled so far: {_dropDisabled})");
        }
    }
    [HarmonyPatch(typeof(System_GemSpawner), "SpawnGem")]
    public static class XpDropDiag_SpawnGem_Patch
    {
        private static int _totalSpawned;
        private static int _collectiveRedirects;
        public static void Reset()
        {
            _totalSpawned = 0;
            _collectiveRedirects = 0;
        }
        static void Prefix(float xp)
        {
            _totalSpawned++;
            int totalActive = 0;
            try
            {
                var gemType = typeof(Death.Game).Assembly.GetType("Death.Run.Behaviours.Entities.Gem");
                if (gemType != null)
                {
                    var totalActiveProp = gemType.GetProperty("TotalActive", BindingFlags.Public | BindingFlags.Static);
                    if (totalActiveProp != null)
                        totalActive = (int)totalActiveProp.GetValue(null);
                }
            }
            catch { }
            if (totalActive >= 300)
            {
                _collectiveRedirects++;
                if (_collectiveRedirects <= 10 || _collectiveRedirects % 50 == 0)
                    CoopPlugin.FileLog($"XpDropDiag: SpawnGem #{_totalSpawned} → COLLECTIVE (activeGems={totalActive}, xp={xp:F1})");
            }
            else if (_totalSpawned <= 20 || _totalSpawned % 200 == 0)
            {
                CoopPlugin.FileLog($"XpDropDiag: SpawnGem #{_totalSpawned} xp={xp:F1} activeGems={totalActive}");
            }
        }
        public static void LogSummary()
        {
            CoopPlugin.FileLog($"XpDropDiag SpawnGem SUMMARY: total={_totalSpawned}, collectiveRedirects={_collectiveRedirects}");
        }
    }
    [HarmonyPatch(typeof(System_PlayerManager), "OnCleanup")]
    public static class PlayerInstancePatches_Cleanup
    {
        static void Postfix()
        {
            XpDropDiag_DropXp_Patch.LogSummary();
            XpDropDiag_SpawnGem_Patch.LogSummary();
            PlayerContext.Restore();
            GemCollector_FixedUpdate_Patch.Reset();
            CoopXpDoubling_Patch.Reset();
            XpDropDiag_DropXp_Patch.Reset();
            XpDropDiag_OnMonsterDied_Patch.Reset();
            XpDropDiag_SpawnGem_Patch.Reset();
            CoopRewardState.Reset();
            CoopPlugin.FileLog("PlayerInstancePatches: CoopRewardState reset on cleanup.");
        }
    }
}