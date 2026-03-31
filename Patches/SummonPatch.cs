using HarmonyLib;
using Death;
using Claw.Core;
using Death.Run.Behaviours;
using Death.Run.Behaviours.AI;
using Death.Run.Behaviours.Entities;
using Death.Run.Behaviours.Events;
using Death.Run.Core;
using Death.Run.Core.Entities;
using Death.Run.Core.Abilities;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection;
namespace DeathMustDieCoop.Patches
{
    public static class SummonPatch
    {
        private static readonly Dictionary<int, float> _ownerHitTimestamps = new Dictionary<int, float>();
        private const float ProtectDuration = 4f;
        private const float MinDistFromOwner = 2.5f;
        private static int _logThrottle;
        private static bool _initialized;
        public static void Init()
        {
            if (_initialized) return;
            Death.Event.AddListener<Event_TookDamage>(OnTookDamage);
            _initialized = true;
            CoopPlugin.FileLog("SummonPatch: Initialized event listener.");
        }
        private static void EnsureInitialized()
        {
            if (!_initialized) Init();
        }
        private static void OnTookDamage(Event_TookDamage ev)
        {
            if (ev.Entity == null) return;
            if (_logThrottle % 50 == 0)
                CoopPlugin.FileLog($"SummonPatch: Hit detected on {ev.Entity.name} (type={ev.Entity.Type}) for {ev.Damage.Amount} dmg");
            if (ev.Entity.Type != EntityType.Player) return;
            _logThrottle++;
            if (_logThrottle % 5 == 0)
                CoopPlugin.FileLog($"SummonPatch: Player {ev.Entity.name} hit! Monitoring for protection.");
            if (ev.Damage.Amount <= 0.05f) return;
            _ownerHitTimestamps[ev.Entity.InstanceId] = Time.time;
        }
        public static class ControllerAi_FixedUpdate_Patch
        {
            static bool Prefix(Controller_Ai __instance)
            {
                EnsureInitialized();
                if (PlayerRegistry.Count < 2) return true;
                if (!__instance.Entity.IsPlayersAlly) return true;
                if (__instance.TargetEntity != null) return true;
                if (!__instance.Steering.enabled) return true;
                var owner = GetOwner(__instance.Entity);
                if (_logThrottle % 500 == 0) 
                {
                    string ownerName = owner != null ? owner.name : "NULL";
                    CoopPlugin.FileLog($"SummonPatch: Summon {__instance.Entity.name} idle. Owner={ownerName}. P1 pos={Player.Position}");
                    _logThrottle++;
                }
                if (owner == null) return true;
                if (__instance.Entity.Condition == EntityCondition.Feared) return true;
                Vector2 ownerPos = owner.Position;
                Vector2 vector = __instance.Steering.Position - ownerPos;
                __instance.Steering.MoveTo(ownerPos + vector.normalized * 1.5f);
                var brain = AiReflectionCache.GetBrain(__instance);
                brain?.Current?.UpdateMovement();
                return false;
            }
            static void Postfix(Controller_Ai __instance)
            {
                if (PlayerRegistry.Count < 2) return;
                if (!__instance.Entity.IsPlayersAlly) return;
                if (!__instance.Steering.enabled) return;
                var owner = GetOwner(__instance.Entity);
                if (owner == null) return;
                Vector2 myPos = __instance.Entity.Position;
                Vector2 ownerPos = owner.Position;
                if (_ownerHitTimestamps.TryGetValue(owner.InstanceId, out float lastHit))
                {
                    if (Time.time - lastHit < ProtectDuration)
                    {
                        float dist = Vector2.Distance(myPos, ownerPos);
                        if (dist > MinDistFromOwner)
                        {
                            __instance.Steering.MoveTo(ownerPos);
                        }
                    }
                }
                if (SingletonBehaviour<RunCamera>.Exists)
                {
                    var wb = SingletonBehaviour<RunCamera>.Instance.WorldBounds;
                    float margin = 1.2f; 
                    float minX = wb.Min.x + margin;
                    float maxX = wb.Max.x - margin;
                    float minY = wb.Min.y + margin;
                    float maxY = wb.Max.y - margin;
                    bool offScreen = myPos.x < minX || myPos.x > maxX || myPos.y < minY || myPos.y > maxY;
                    if (offScreen)
                    {
                        Vector2 clampedPos = new Vector2(
                            Mathf.Clamp(myPos.x, minX, maxX),
                            Mathf.Clamp(myPos.y, minY, maxY)
                        );
                        __instance.Steering.MoveTo(clampedPos);
                        if (__instance.TargetEntity != null)
                        {
                            Vector2 tPos = __instance.TargetEntity.Position;
                            if (tPos.x < minX - 0.5f || tPos.x > maxX + 0.5f || tPos.y < minY - 0.5f || tPos.y > maxY + 0.5f)
                            {
                                AiReflectionCache.SetTargetEntity(__instance, null);
                            }
                        }
                    }
                }
            }
        }
        public static Entity GetOwner(Entity summon)
        {
            var source = summon.DamageSource;
            if (source != null && source is IAbility ability && ability.Entity != null)
            {
                return ability.Entity;
            }
            if (summon.Creator.TryGet(out var creator)) 
            {
                return creator;
            }
            return null;
        }
    }
    public static class AiNode_Wander_PickMovePosition_Patch
    {
        private static FieldInfo _orbitField;
        private static bool _cached;
        static bool Prefix(AiNode_Wander __instance, ref Vector2 __result)
        {
            if (PlayerRegistry.Count < 2) return true;
            if (!_cached) Cache();
            var controller = AiReflectionCache.GetController(__instance);
            if (controller == null) return true;
            if (!controller.Entity.IsPlayersAlly) return true;
            var owner = SummonPatch.GetOwner(controller.Entity);
            if (owner == null) return true;
            float orbit = (float)_orbitField.GetValue(__instance);
            var steering = AiReflectionCache.GetSteering(__instance);
            if (steering == null) return true;
            Vector2 position = steering.Position;
            Vector2 vector = ((controller.TargetEntity != null) ? controller.TargetEntity.Position : owner.Position) - position;
            float magnitude = vector.magnitude;
            if (magnitude < 1E-05f) return true;
            Vector2 vector2 = vector / magnitude;
            Vector2 vector3 = vector2 * (magnitude - orbit);
            float f = Time.time * 4f + (float)controller.Entity.InstanceId * 0.25f;
            Vector2 vector4 = new Vector2(Mathf.Cos(f), Mathf.Sin(f)) * orbit * 0.075f + vector3;
            float minSafeDist = 1.5f;
            if (vector4.sqrMagnitude < minSafeDist * minSafeDist)
            {
                vector4 = vector4.normalized * minSafeDist;
            }
            __result = position + vector4;
            return false;
        }
        private static void Cache()
        {
            var type = typeof(AiNode_Wander);
            _orbitField = type.GetField("_orbit", BindingFlags.NonPublic | BindingFlags.Instance);
            _cached = true;
        }
    }
}