using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Death;
using Death.Run.Core;
using Death.Run.Core.Entities;
using Death.Run.Behaviours;
using Death.Run.Behaviours.AI;
using Death.Run.Behaviours.Entities;
using Death.Steering;
using Death.Utils;
using Claw.Core;
namespace DeathMustDieCoop.Patches
{
    [HarmonyPatch(typeof(FrequencyScheduler), "Update")]
    public static class PerfGrid_FreqScheduler_Update_Patch
    {
        static void Prefix()
        {
            PerfStats.StartUpdate();
            if (PerfSpatialGrid.Instance != null)
                PerfSpatialGrid.Instance.Rebuild();
        }
        static void Postfix()
        {
            PerfStats.EndUpdate();
        }
    }
    [HarmonyPatch(typeof(FrequencyScheduler), "FixedUpdate")]
    public static class PerfTimer_FreqScheduler_FixedUpdate_Patch
    {
        static void Prefix()
        {
            PerfStats.StartPhysFixed();
        }
        static void Postfix()
        {
            PerfStats.EndPhysFixed();
        }
    }
    [HarmonyPatch(typeof(RunCamera), "Start")]
    public static class PerfGrid_RunCamera_Start_Patch
    {
        static void Postfix()
        {
            PerfSpatialGrid.EnsureExists();
            CoopPlugin.FileLog("PerfPatches: Spatial grid created for run.");
        }
    }
    [HarmonyPatch(typeof(Controller_Ai), "UpdateTargetEntity")]
    public static class PerfTargeting_UpdateTargetEntity_Patch
    {
        private static Func<Controller_Ai, Behaviour_Unbunch> _getUnbunch;
        private static Func<Controller_Ai, float> _getDistTargetTimestamp;
        private static Action<Controller_Ai, float> _setDistTargetTimestamp;
        private static Action<Controller_Ai, Entity> _setTargetEntity;
        private static Func<Controller_Ai, Entity> _getTargetEntityOneShot;
        private static Action<Controller_Ai, Entity> _setTargetEntityOneShot;
        private static bool _cached;
        private static bool _cacheValid;
        static bool Prepare()
        {
            EnsureCached();
            return _cacheValid;
        }
        private static void EnsureCached()
        {
            if (_cached) return;
            _cached = true;
            try
            {
                var aiType = typeof(Controller_Ai);
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var unbunchField = aiType.GetField("_unbunch", flags);
                if (unbunchField != null)
                    _getUnbunch = (Func<Controller_Ai, Behaviour_Unbunch>)CreateGetter(unbunchField, typeof(Behaviour_Unbunch));
                var distTsField = aiType.GetField("_distTargetTimestamp", flags);
                if (distTsField != null)
                {
                    _getDistTargetTimestamp = (Func<Controller_Ai, float>)CreateGetter(distTsField, typeof(float));
                    _setDistTargetTimestamp = (Action<Controller_Ai, float>)CreateSetter(distTsField, typeof(float));
                }
                var targetField = aiType.GetField("_targetEntity", flags);
                if (targetField != null)
                {
                    _setTargetEntity = (Action<Controller_Ai, Entity>)CreateSetter(targetField, typeof(Entity));
                }
                var targetOneShotField = aiType.GetField("_targetEntityOneShot", flags);
                if (targetOneShotField != null)
                {
                    _getTargetEntityOneShot = (Func<Controller_Ai, Entity>)CreateGetter(targetOneShotField, typeof(Entity));
                    _setTargetEntityOneShot = (Action<Controller_Ai, Entity>)CreateSetter(targetOneShotField, typeof(Entity));
                }
                _cacheValid = _getUnbunch != null && _getDistTargetTimestamp != null && _setDistTargetTimestamp != null && _setTargetEntity != null;
            }
            catch (Exception ex)
            {
                CoopPlugin.FileLog($"PerfPatches: Spatial targeting cache failed with exception: {ex}");
            }
            if (_cacheValid)
                CoopPlugin.FileLog("PerfPatches: Spatial targeting ready.");
            else
                CoopPlugin.FileLog("PerfPatches: WARNING — spatial targeting cache incomplete.");
        }
        private static Delegate CreateGetter(FieldInfo field, Type returnType)
        {
            string methodName = field.ReflectedType.FullName + ".get_" + field.Name;
            var getterMethod = new System.Reflection.Emit.DynamicMethod(methodName, returnType, new Type[] { typeof(Controller_Ai) }, true);
            var gen = getterMethod.GetILGenerator();
            gen.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            gen.Emit(System.Reflection.Emit.OpCodes.Ldfld, field);
            gen.Emit(System.Reflection.Emit.OpCodes.Ret);
            return getterMethod.CreateDelegate(typeof(Func<,>).MakeGenericType(typeof(Controller_Ai), returnType));
        }
        private static Delegate CreateSetter(FieldInfo field, Type valueType)
        {
            string methodName = field.ReflectedType.FullName + ".set_" + field.Name;
            var setterMethod = new System.Reflection.Emit.DynamicMethod(methodName, null, new Type[] { typeof(Controller_Ai), valueType }, true);
            var gen = setterMethod.GetILGenerator();
            gen.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            gen.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
            gen.Emit(System.Reflection.Emit.OpCodes.Stfld, field);
            gen.Emit(System.Reflection.Emit.OpCodes.Ret);
            return setterMethod.CreateDelegate(typeof(Action<,>).MakeGenericType(typeof(Controller_Ai), valueType));
        }
        static bool Prefix(Controller_Ai __instance, ref bool __result)
        {
            if (!_cached) EnsureCached();
            if (!_cacheValid) return true;
            if (PerfSpatialGrid.Instance == null)
            {
                PerfStats.TargetNoGrid++;
                return true; 
            }
            if (!Player.Exists)
            {
                SetTarget(__instance, null);
                __result = false;
                return false;
            }
            var unbunch = _getUnbunch(__instance);
            if (unbunch != null && unbunch.IsBunched)
            {
                SetTarget(__instance, null);
                __result = false;
                return false;
            }
            var currentTarget = __instance.TargetEntity;
            var oneShotTarget = _getTargetEntityOneShot != null ? _getTargetEntityOneShot(__instance) : null;
            if (currentTarget != null && currentTarget.IsAlive
                && currentTarget.Team.IsEnemy(__instance.Entity.TeamId)
                && !currentTarget.Untargetable)
            {
                bool inRange = __instance.Entity.IsPlayersEnemy
                    || (currentTarget.Position - Player.Position).sqrMagnitude < 36f;
                if (inRange && (__instance.TauntSpecial
                    || __instance.AggroDamage > 0f
                    || oneShotTarget != null
                    || Time.time - _getDistTargetTimestamp(__instance) < 1.5f))
                {
                    PerfStats.TargetFastPath++;
                    __result = true;
                    return false;
                }
            }
            Vector2 position = __instance.Entity.Position;
            Entity best = null;
            float bestScore = float.MaxValue;
            if (__instance.Entity.IsPlayersAlly)
            {
                var nearby = PerfSpatialGrid.Instance.QueryNearby(position, 12f);
                for (int i = 0; i < nearby.Count; i++)
                {
                    var e = nearby[i];
                    if (e.IsPlayersAlly || e.IsDead || e.Untargetable) continue;
                    float score = (e.Position - position).sqrMagnitude / (1f + e.TauntMultiplier);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = e;
                    }
                }
            }
            else
            {
                if (!Player.Instance.Entity.Untargetable)
                {
                    best = Player.Instance.Entity;
                    bestScore = (best.Position - position).sqrMagnitude / (1f + best.TauntMultiplier);
                }
                var nearby = PerfSpatialGrid.Instance.QueryNearby(position, 6f);
                for (int i = 0; i < nearby.Count; i++)
                {
                    var e = nearby[i];
                    if (e.IsPlayersEnemy || e.IsDead || e.Untargetable) continue;
                    float score = (e.Position - position).sqrMagnitude / (1f + e.TauntMultiplier);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = e;
                    }
                }
            }
            PerfStats.TargetGridScan++;
            _setDistTargetTimestamp(__instance, Time.time);
            __instance.AggroDamage = 0f;
            __instance.TargetSwitchTimestamp = 0f;
            SetTarget(__instance, best);
            __result = best != null;
            return false; 
        }
        private static void SetTarget(Controller_Ai ai, Entity target)
        {
            _setTargetEntity(ai, target);
            if (_setTargetEntityOneShot != null)
                _setTargetEntityOneShot(ai, null);
        }
    }
    [HarmonyPatch(typeof(Controller_Ai), "FixedUpdate")]
    public static class PerfSteering_FixedUpdate_Patch
    {
        private static int _fixedFrame;
        private static int _boundsFrame = -1;
        private static float _minX, _minY, _maxX, _maxY;
        private const float OffscreenMargin = 2f;
        static bool Prefix(Controller_Ai __instance)
        {
            _fixedFrame++;
            if (_boundsFrame != _fixedFrame)
            {
                _boundsFrame = _fixedFrame;
                if (SingletonBehaviour<RunCamera>.Exists)
                {
                    var wb = SingletonBehaviour<RunCamera>.Instance.WorldBounds;
                    var min = wb.Min;
                    var max = wb.Max;
                    _minX = min.x - OffscreenMargin;
                    _minY = min.y - OffscreenMargin;
                    _maxX = max.x + OffscreenMargin;
                    _maxY = max.y + OffscreenMargin;
                }
            }
            Vector2 pos = __instance.Entity.Position;
            if (pos.x >= _minX && pos.x <= _maxX && pos.y >= _minY && pos.y <= _maxY)
            {
                PerfStats.FixedClose++;
                PerfStats.StartAiFixed();
                return true; 
            }
            int phase = __instance.GetInstanceID() & 0x7FFFFFFF;
            bool runs = (_fixedFrame + phase) % 3 == 0;
            if (runs)
            {
                PerfStats.FixedOffRan++;
                PerfStats.StartAiFixed();
            }
            else
            {
                PerfStats.FixedOffSkipped++;
            }
            return runs;
        }
        static void Postfix()
        {
            PerfStats.EndAiFixed();
        }
    }
    [HarmonyPatch(typeof(SteeringAgent2D), "FixedUpdate")]
    public static class PerfTimer_Steering_FixedUpdate_Patch
    {
        static void Prefix()
        {
            PerfStats.StartSteering();
        }
        static void Postfix()
        {
            PerfStats.EndSteering();
        }
    }
    [HarmonyPatch(typeof(Controller_Ai), "ScheduledUpdate")]
    public static class PerfBrain_ScheduledUpdate_Patch
    {
        private static bool _cached;
        private static bool _cacheValid;
        private static Func<Controller_Ai, SteeringAgent2D> _getSteering;
        private static Func<Controller_Ai, bool> _updateTargetEntity;
        private static Action<Controller_Ai> _updateHeading;
        private static int _cacheFrame = -1;
        private static float _minX, _minY, _maxX, _maxY;
        private const float OffscreenMargin = 2f;
        static bool Prepare()
        {
            EnsureCached();
            return _cacheValid;
        }
        private static void EnsureCached()
        {
            if (_cached) return;
            _cached = true;
            try
            {
                var aiType = typeof(Controller_Ai);
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var steeringField = aiType.GetField("_steering", flags);
                if (steeringField != null)
                {
                    _getSteering = (Func<Controller_Ai, SteeringAgent2D>)CreateGetter(steeringField);
                }
                var updateTargetMethod = aiType.GetMethod("UpdateTargetEntity", BindingFlags.Public | BindingFlags.Instance);
                if (updateTargetMethod != null)
                {
                    _updateTargetEntity = (Func<Controller_Ai, bool>)Delegate.CreateDelegate(typeof(Func<Controller_Ai, bool>), updateTargetMethod);
                }
                var updateHeadingMethod = aiType.GetMethod("UpdateHeading", flags);
                if (updateHeadingMethod != null)
                {
                    _updateHeading = (Action<Controller_Ai>)Delegate.CreateDelegate(typeof(Action<Controller_Ai>), updateHeadingMethod);
                }
                _cacheValid = _getSteering != null && _updateTargetEntity != null;
            }
            catch (Exception ex)
            {
                CoopPlugin.FileLog($"PerfPatches: Brain throttle cache failed with exception: {ex}");
            }
            if (_cacheValid)
                CoopPlugin.FileLog("PerfPatches: Brain throttle ready.");
            else
                CoopPlugin.FileLog("PerfPatches: WARNING — brain throttle cache failed.");
        }
        private static Delegate CreateGetter(FieldInfo field)
        {
            string methodName = field.ReflectedType.FullName + ".get_" + field.Name;
            var getterMethod = new System.Reflection.Emit.DynamicMethod(methodName, field.FieldType, new Type[] { typeof(Controller_Ai) }, true);
            var gen = getterMethod.GetILGenerator();
            gen.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            gen.Emit(System.Reflection.Emit.OpCodes.Ldfld, field);
            gen.Emit(System.Reflection.Emit.OpCodes.Ret);
            return getterMethod.CreateDelegate(typeof(Func<Controller_Ai, SteeringAgent2D>));
        }
        static bool Prefix(Controller_Ai __instance)
        {
            if (!_cached) EnsureCached();
            if (!_cacheValid) return true;
            int frame = Time.frameCount;
            if (_cacheFrame != frame)
            {
                _cacheFrame = frame;
                if (SingletonBehaviour<RunCamera>.Exists)
                {
                    var wb = SingletonBehaviour<RunCamera>.Instance.WorldBounds;
                    var min = wb.Min;
                    var max = wb.Max;
                    _minX = min.x - OffscreenMargin;
                    _minY = min.y - OffscreenMargin;
                    _maxX = max.x + OffscreenMargin;
                    _maxY = max.y + OffscreenMargin;
                }
            }
            Vector2 pos = __instance.Entity.Position;
            if (pos.x >= _minX && pos.x <= _maxX && pos.y >= _minY && pos.y <= _maxY)
            {
                PerfStats.BrainFull++;
                PerfStats.StartBrain();
                return true; 
            }
            PerfStats.BrainOffLite++;
            DoLightweightUpdate(__instance);
            return false;
        }
        static void Postfix()
        {
            PerfStats.EndBrain();
        }
        private static void DoLightweightUpdate(Controller_Ai ai)
        {
            _updateTargetEntity(ai);
            if (ai.ShouldUpdateHeading && _updateHeading != null)
                _updateHeading(ai);
            var steering = _getSteering(ai);
            if (steering != null)
            {
                float speed = ai.Entity.Stats.Get(StatId.MovementSpeed);
                steering.MaxSpeed = speed * ai.Unit.Animations.MovementSpeedAdjust;
                ai.Unit.Animations.SetMovementSpeed(speed);
                ai.Unit.Animations.SetIsWalking(steering.enabled && !steering.IsIdle);
            }
        }
    }
}