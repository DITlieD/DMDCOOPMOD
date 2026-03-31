using HarmonyLib;
using Death.Run.Behaviours;
using Death.Run.Behaviours.Players;
using Death.Run.Behaviours.Entities;
using Death.Run.Behaviours.Events;
using Death.Run.Behaviours.AI;
using Death.Run.Behaviours.Abilities.Actives;
using Death.Run.Core;
using Death.Run.Core.Abilities;
using Death.Run.Core.Abilities.Actives;
using Death.Run.Core.Entities;
using Claw.Core.Chaos;
using Death.Run.Systems;
using Death.Steering;
using UnityEngine;
using System.Collections.Generic;
using Claw.Core;
namespace DeathMustDieCoop.Patches
{
    internal static class AiReflectionCache
    {
        private static System.Reflection.MethodInfo _targetEntitySetter;
        private static System.Reflection.FieldInfo _targetEntityOneShotField;
        private static System.Reflection.FieldInfo _brainField;
        private static bool _cached;
        internal static void SetTargetEntity(Controller_Ai ai, Entity target)
        {
            EnsureCached();
            _targetEntitySetter?.Invoke(ai, new object[] { target });
        }
        internal static void SetTargetEntityOneShot(Controller_Ai ai, Entity target)
        {
            EnsureCached();
            _targetEntityOneShotField?.SetValue(ai, target);
        }
        internal static MonsterAiTree GetBrain(Controller_Ai ai)
        {
            EnsureCached();
            return (MonsterAiTree)_brainField?.GetValue(ai);
        }
        private static System.Reflection.PropertyInfo _nodeControllerProp;
        private static System.Reflection.PropertyInfo _nodeSteeringProp;
        internal static Controller_Ai GetController(MonsterAiNode node)
        {
            EnsureCached();
            return (Controller_Ai)_nodeControllerProp?.GetValue(node);
        }
        internal static SteeringAgent2D GetSteering(MonsterAiNode node)
        {
            EnsureCached();
            return (SteeringAgent2D)_nodeSteeringProp?.GetValue(node);
        }
        private static void EnsureCached()
        {
            if (_cached) return;
            _cached = true;
            var type = typeof(Controller_Ai);
            var prop = type.GetProperty("TargetEntity",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            _targetEntitySetter = prop?.GetSetMethod(true);
            _targetEntityOneShotField = type.GetField("_targetEntityOneShot",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            _brainField = type.GetField("_brain",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var nodeType = typeof(MonsterAiNode);
            _nodeControllerProp = nodeType.GetProperty("Controller",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            _nodeSteeringProp = nodeType.GetProperty("Steering",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (_targetEntityOneShotField == null)
                CoopPlugin.FileLog("AiReflectionCache: WARNING — _targetEntityOneShot field not found!");
            if (_brainField == null)
                CoopPlugin.FileLog("AiReflectionCache: WARNING — _brain field not found!");
            if (_nodeControllerProp == null)
                CoopPlugin.FileLog("AiReflectionCache: WARNING — MonsterAiNode.Controller prop not found!");
        }
    }
    public static class CoopAggroTracker
    {
        private static readonly Dictionary<int, float[]> _damageMap = new Dictionary<int, float[]>();
        private static readonly Dictionary<int, float> _lastDecayTime = new Dictionary<int, float>();
        private const float DecayRate = 0.92f;       
        private const float SwitchThreshold = 0.2f;   
        public const float SwitchCooldown = 2f;        
        private static int _trackLogCount;
        public static void TrackDamage(int enemyInstanceId, int playerIndex, float amount)
        {
            if (playerIndex < 0 || playerIndex > 1 || amount <= 0f) return;
            if (!_damageMap.TryGetValue(enemyInstanceId, out var dmg))
            {
                dmg = new float[2];
                _damageMap[enemyInstanceId] = dmg;
                _lastDecayTime[enemyInstanceId] = Time.time;
            }
            dmg[playerIndex] += amount;
            _trackLogCount++;
            if (_trackLogCount <= 20 || _trackLogCount % 200 == 0)
                CoopPlugin.FileLog($"Aggro: P{playerIndex + 1} hit enemy#{enemyInstanceId} for {amount:F0} (totals: P1={dmg[0]:F0}, P2={dmg[1]:F0})");
        }
        private static int _aggroDecisionLogCount;
        public static Behaviour_Player GetAggroTarget(Entity enemy)
        {
            if (!_damageMap.TryGetValue(enemy.InstanceId, out var dmg))
                return null;
            ApplyDecay(enemy.InstanceId, dmg);
            float p1 = dmg[0];
            float p2 = dmg[1];
            if (p1 < 1f && p2 < 1f)
                return null;
            Behaviour_Player result = null;
            string reason = null;
            if (p1 >= 1f && p2 < 1f)
            {
                result = GetIfAlive(0);
                reason = "sole attacker";
            }
            else if (p2 >= 1f && p1 < 1f)
            {
                result = GetIfAlive(1);
                reason = "sole attacker";
            }
            else if (p2 > p1 * (1f + SwitchThreshold))
            {
                result = GetIfAlive(1);
                reason = $"more dmg ({p2:F0} vs {p1:F0})";
            }
            else if (p1 > p2 * (1f + SwitchThreshold))
            {
                result = GetIfAlive(0);
                reason = $"more dmg ({p1:F0} vs {p2:F0})";
            }
            if (result != null)
            {
                _aggroDecisionLogCount++;
                if (_aggroDecisionLogCount <= 30 || _aggroDecisionLogCount % 500 == 0)
                    CoopPlugin.FileLog($"Aggro: enemy#{enemy.InstanceId} → {result.name} ({reason})");
            }
            return result;
        }
        private static Behaviour_Player GetIfAlive(int index)
        {
            var p = PlayerRegistry.GetPlayer(index);
            if (p != null && p.Entity != null && p.Entity.IsAlive && !p.Entity.Untargetable)
                return p;
            return null;
        }
        private static void ApplyDecay(int enemyId, float[] dmg)
        {
            if (!_lastDecayTime.TryGetValue(enemyId, out float lastTime))
            {
                _lastDecayTime[enemyId] = Time.time;
                return;
            }
            float elapsed = Time.time - lastTime;
            if (elapsed < 0.1f) return;
            float factor = Mathf.Pow(DecayRate, elapsed);
            dmg[0] *= factor;
            dmg[1] *= factor;
            _lastDecayTime[enemyId] = Time.time;
        }
        public static void Remove(int enemyInstanceId)
        {
            _damageMap.Remove(enemyInstanceId);
            _lastDecayTime.Remove(enemyInstanceId);
        }
        public static void Clear()
        {
            _damageMap.Clear();
            _lastDecayTime.Clear();
            _trackLogCount = 0;
            _aggroDecisionLogCount = 0;
            CoopPlugin.FileLog("Aggro: Tracker cleared for new run.");
        }
    }
    [HarmonyPatch(typeof(System_Analytics), "OnTookDamage")]
    public static class AggroTracker_OnTookDamage_Patch
    {
        static void Postfix(Event_TookDamage ev)
        {
            if (PlayerRegistry.Count < 2) return;
            if (ev.Dealer == null) return;
            if (ev.Entity.Type != EntityType.Monster) return;
            float amount = ev.Damage.Amount - ev.ExcessDamage;
            if (amount <= 0f) return;
            var damageSource = ev.Dealer.DamageSource;
            if (damageSource == null || !Teams.IsPlayersAlly(damageSource.TeamId)) return;
            Entity dealerEntity = null;
            if (ev.Dealer is IAbility ability)
                dealerEntity = ability.Entity;
            else if (damageSource is IAbility abilitySource)
                dealerEntity = abilitySource.Entity;
            if (dealerEntity == null) return;
            for (int i = 0; i < PlayerRegistry.Players.Count; i++)
            {
                var p = PlayerRegistry.Players[i];
                if (p != null && p.Entity == dealerEntity)
                {
                    CoopAggroTracker.TrackDamage(ev.Entity.InstanceId, i, amount);
                    return;
                }
            }
        }
    }
    [HarmonyPatch(typeof(Controller_Ai), "TrySetTargetToPlayer")]
    public static class ControllerAi_TrySetTargetToPlayer_Patch
    {
        static bool Prefix(Controller_Ai __instance, ref bool __result)
        {
            if (PlayerRegistry.Count < 2) return true;
            var aggroTarget = CoopAggroTracker.GetAggroTarget(__instance.Entity);
            if (aggroTarget != null)
            {
                AiReflectionCache.SetTargetEntity(__instance, aggroTarget.Entity);
                __result = true;
                return false;
            }
            var nearest = PlayerRegistry.GetNearest(__instance.Entity.Position);
            if (nearest != null && nearest.Entity != null && nearest.Entity.IsAlive)
            {
                AiReflectionCache.SetTargetEntity(__instance, nearest.Entity);
                __result = true;
                return false;
            }
            if (Player.Exists) return true;
            __result = false;
            return false;
        }
    }
    [HarmonyPatch(typeof(Controller_Ai), "UpdateTargetEntity")]
    public static class ControllerAi_UpdateTargetEntity_Patch
    {
        private static int _retargetLogCount;
        static bool Prefix(Controller_Ai __instance, ref bool __result)
        {
            if (PlayerRegistry.Count < 2) return true;
            if (__instance.Entity.IsPlayersAlly)
            {
                if (!SingletonBehaviour<RunCamera>.Exists) return true;
                var wb = SingletonBehaviour<RunCamera>.Instance.WorldBounds;
                var monsters = EntityManager.Monsters;
                Entity best = null;
                float bestScore = float.MaxValue;
                Vector2 myPos = __instance.Entity.Position;
                foreach (var monster in monsters)
                {
                    if (monster == null || monster.IsDead || monster.Untargetable || monster.IsPlayersAlly) continue;
                    var targetBf = wb;
                    targetBf.Shrink(2.0f); 
                    if (!targetBf.Contains(monster.Position)) continue;
                    float distSq = (monster.Position - myPos).sqrMagnitude;
                    float score = distSq / (1f + monster.TauntMultiplier);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = monster;
                    }
                }
                AiReflectionCache.SetTargetEntity(__instance, best);
                __result = best != null;
                return false; 
            }
            return true; 
        }
        static void Postfix(Controller_Ai __instance, ref bool __result)
        {
            if (PlayerRegistry.Count < 2) return;
            if (__instance.Entity.IsPlayersAlly) return;
            var target = __instance.TargetEntity;
            if (target == null || !target.IsAlive)
            {
                var best = GetBestPlayerTarget(__instance);
                if (best != null)
                {
                    AiReflectionCache.SetTargetEntity(__instance, best.Entity);
                    __result = true;
                    _retargetLogCount++;
                    if (_retargetLogCount <= 5)
                        CoopPlugin.FileLog($"AiTargeting: Retargeted from dead/null to {best.name}");
                }
                return;
            }
            bool isPlayerTarget = false;
            for (int i = 0; i < PlayerRegistry.Players.Count; i++)
            {
                var p = PlayerRegistry.Players[i];
                if (p != null && p.Entity == target)
                {
                    isPlayerTarget = true;
                    break;
                }
            }
            if (!isPlayerTarget)
            {
                if (Time.time - __instance.TargetSwitchTimestamp <= CoopAggroTracker.SwitchCooldown)
                    return;
                var bestPlayer = GetBestPlayerTarget(__instance);
                if (bestPlayer != null && bestPlayer.Entity != null)
                {
                    AiReflectionCache.SetTargetEntity(__instance, bestPlayer.Entity);
                    __instance.TargetSwitchTimestamp = Time.time;
                    _retargetLogCount++;
                    if (_retargetLogCount <= 30 || _retargetLogCount % 500 == 0)
                        CoopPlugin.FileLog($"AiTargeting: redirected from summon to {bestPlayer.name}");
                }
                else
                {
                }
                return;
            }
            var bestTarget = GetBestPlayerTarget(__instance);
            if (bestTarget != null && bestTarget.Entity != null && bestTarget.Entity != target)
            {
                if (Time.time - __instance.TargetSwitchTimestamp <= CoopAggroTracker.SwitchCooldown)
                    return;
                _retargetLogCount++;
                if (_retargetLogCount <= 30 || _retargetLogCount % 500 == 0)
                    CoopPlugin.FileLog($"AiTargeting: enemy switched target to {bestTarget.name} (aggro/proximity)");
                AiReflectionCache.SetTargetEntity(__instance, bestTarget.Entity);
                __instance.TargetSwitchTimestamp = Time.time;
            }
        }
        private static Behaviour_Player GetBestPlayerTarget(Controller_Ai enemy)
        {
            var aggroTarget = CoopAggroTracker.GetAggroTarget(enemy.Entity);
            if (aggroTarget != null) return aggroTarget;
            var nearest = PlayerRegistry.GetNearest(enemy.Entity.Position);
            if (nearest != null && nearest.Entity != null && nearest.Entity.IsAlive && !nearest.Entity.Untargetable)
            {
                float dist = Vector2.Distance(enemy.Entity.Position, nearest.Entity.Position);
                if (dist <= 12f)
                    return nearest;
            }
            return null;
        }
    }
    [HarmonyPatch(typeof(Controller_Ai), "GetActionInternal")]
    public static class ControllerAi_GetActionInternal_Patch
    {
        private static int _patchLogCount;
        static bool Prefix(Controller_Ai __instance, ref ActiveAction __result)
        {
            if (PlayerRegistry.Count < 2) return true;
            if (__instance.Entity.IsPlayersAlly) return true;
            if (!Player.Instance) return true;
            if (Player.Instance.Entity.Untargetable) return true;
            var currentTarget = __instance.TargetEntity;
            bool targetIsPlayer = false;
            for (int i = 0; i < PlayerRegistry.Players.Count; i++)
            {
                var p = PlayerRegistry.Players[i];
                if (p != null && p.Entity == currentTarget)
                {
                    targetIsPlayer = true;
                    break;
                }
            }
            if (targetIsPlayer)
            {
                __result = __instance.Brain.Current?.GetAction();
                return false;
            }
            Entity bestEntity = GetBestPlayerEntity(__instance);
            if (bestEntity == null)
            {
                __result = __instance.Brain.Current?.GetAction();
                return false;
            }
            ActiveAction action = __instance.Brain.Current?.GetAction();
            float chance = action?.Ability.TargetPlayerChance ?? 0f;
            if (chance < 1E-05f)
            {
                __result = action;
                return false;
            }
            if (chance > 0.9999f)
            {
                AiReflectionCache.SetTargetEntityOneShot(__instance, bestEntity);
                __result = __instance.Brain.Current?.GetAction();
                _patchLogCount++;
                if (_patchLogCount <= 10)
                    CoopPlugin.FileLog($"GetActionInternal: redirected oneshot to {bestEntity.name} (chance=1)");
                return false;
            }
            AiReflectionCache.SetTargetEntityOneShot(__instance, bestEntity);
            action = __instance.Brain.Current?.GetAction();
            if (action != null)
            {
                chance = action.Ability.TargetPlayerChance;
                if (RunRng.Instance.RollChance(chance))
                {
                    _patchLogCount++;
                    if (_patchLogCount <= 10)
                        CoopPlugin.FileLog($"GetActionInternal: redirected oneshot to {bestEntity.name} (chance={chance:F2})");
                    __result = action;
                    return false;
                }
            }
            AiReflectionCache.SetTargetEntityOneShot(__instance, null);
            __result = __instance.Brain.Current?.GetAction();
            return false;
        }
        private static Entity GetBestPlayerEntity(Controller_Ai enemy)
        {
            var aggroTarget = CoopAggroTracker.GetAggroTarget(enemy.Entity);
            if (aggroTarget != null) return aggroTarget.Entity;
            var nearest = PlayerRegistry.GetNearest(enemy.Entity.Position);
            if (nearest != null && nearest.Entity != null && nearest.Entity.IsAlive && !nearest.Entity.Untargetable)
                return nearest.Entity;
            return Player.Exists ? Player.Instance.Entity : null;
        }
    }
    [HarmonyPatch(typeof(Controller_Ai), "SetTargetByAggro")]
    public static class ControllerAi_SetTargetByAggro_Patch
    {
        private static int _redirectLogCount;
        static void Prefix(Controller_Ai __instance, ref Entity newTarget, ref float accumulatedDamage)
        {
            if (PlayerRegistry.Count < 2) return;
            if (__instance.Entity.IsPlayersAlly) return;
            for (int i = 0; i < PlayerRegistry.Players.Count; i++)
            {
                var p = PlayerRegistry.Players[i];
                if (p != null && p.Entity == newTarget)
                    return; 
            }
            var aggroPlayer = CoopAggroTracker.GetAggroTarget(__instance.Entity);
            if (aggroPlayer != null)
            {
                _redirectLogCount++;
                if (_redirectLogCount <= 20 || _redirectLogCount % 200 == 0)
                    CoopPlugin.FileLog($"SetTargetByAggro: redirected from summon to {aggroPlayer.name} (player damage)");
                newTarget = aggroPlayer.Entity;
                return;
            }
            var nearest = PlayerRegistry.GetNearest(__instance.Entity.Position);
            if (nearest != null && nearest.Entity != null && nearest.Entity.IsAlive && !nearest.Entity.Untargetable)
            {
                float dist = Vector2.Distance(__instance.Entity.Position, nearest.Entity.Position);
                if (dist <= 12f)
                {
                    _redirectLogCount++;
                    if (_redirectLogCount <= 20 || _redirectLogCount % 200 == 0)
                        CoopPlugin.FileLog($"SetTargetByAggro: redirected from summon to {nearest.name} (proximity, dist={dist:F1})");
                    newTarget = nearest.Entity;
                }
            }
        }
    }
    [HarmonyPatch(typeof(Behaviour_ProximityBomb), "Update_Idle")]
    public static class ProximityBomb_UpdateIdle_Patch
    {
        static bool Prefix(Behaviour_ProximityBomb __instance)
        {
            if (PlayerRegistry.Count < 2) return true;
            Vector2 bombPos = __instance.transform.position;
            float triggerRadius = __instance.Entity.Stats.GetAsRadius(StatId.Static1);
            for (int i = 0; i < PlayerRegistry.Players.Count; i++)
            {
                var p = PlayerRegistry.Players[i];
                if (p == null || p.Entity == null || !p.Entity.IsAlive) continue;
                if (Vector2.Distance((Vector2)p.transform.position, bombPos) < triggerRadius)
                    return true; 
            }
            return false; 
        }
    }
    [HarmonyPatch(typeof(Behaviour_SpinBlades), "GetTargetPos")]
    public static class SpinBlades_GetTargetPos_Patch
    {
        static bool Prefix(ref Vector2 __result)
        {
            if (PlayerRegistry.Count < 2) return true;
            var nearest = PlayerRegistry.GetNearest(Vector2.zero);
            for (int i = 0; i < PlayerRegistry.Players.Count; i++)
            {
                var p = PlayerRegistry.Players[i];
                if (p != null && p.Entity != null && p.Entity.IsAlive)
                {
                    __result = p.Entity.AttackPos;
                    return false;
                }
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(Cast_BossMeteorShower), "PickMeteorPosition")]
    public static class BossMeteor_PickMeteorPosition_Patch
    {
        static bool Prefix(Cast_BossMeteorShower __instance, ref Vector2 __result)
        {
            if (PlayerRegistry.Count < 2) return true;
            int alive = 0;
            for (int i = 0; i < PlayerRegistry.Players.Count; i++)
            {
                var p = PlayerRegistry.Players[i];
                if (p != null && p.Entity != null && p.Entity.IsAlive) alive++;
            }
            if (alive == 0) return true;
            int pick = Random.Range(0, alive);
            int idx = 0;
            for (int i = 0; i < PlayerRegistry.Players.Count; i++)
            {
                var p = PlayerRegistry.Players[i];
                if (p != null && p.Entity != null && p.Entity.IsAlive)
                {
                    if (idx == pick)
                    {
                        __result = (Vector2)p.transform.position +
                            Random.insideUnitCircle * __instance.Stats.Get(StatId.Static1);
                        return false;
                    }
                    idx++;
                }
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(FollowPlayer), "Update")]
    public static class FollowPlayer_Update_Patch
    {
        static bool Prefix(FollowPlayer __instance)
        {
            if (PlayerRegistry.Count < 2) return true;
            var nearest = PlayerRegistry.GetNearest(__instance.transform.position);
            if (nearest != null)
            {
                __instance.transform.position = nearest.transform.position;
                return false;
            }
            return true;
        }
    }
}