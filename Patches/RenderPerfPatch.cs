using HarmonyLib;
using UnityEngine;
using Death.Rendering;
using Death.Run.Behaviours.Animations;
using System.Collections.Generic;
namespace DeathMustDieCoop.Patches
{
    [HarmonyPatch(typeof(Animations_Unit), "Awake")]
    public static class Animations_Unit_Awake_Perf_Patch
    {
        static void Postfix(Animations_Unit __instance)
        {
            var tr = Traverse.Create(__instance);
            var animator = tr.Field("_animator").GetValue<Animator>();
            if (animator != null)
            {
                tr.Field("_defaultCullingMode").SetValue(AnimatorCullingMode.CullUpdateTransforms);
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            }
        }
    }
    [HarmonyPatch(typeof(Animations_Unit), "OnEnable")]
    public static class Animations_Unit_OnEnable_Perf_Patch
    {
        static void Postfix(Animations_Unit __instance)
        {
            var animator = Traverse.Create(__instance).Field("_animator").GetValue<Animator>();
            if (animator != null)
            {
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            }
        }
    }
    [HarmonyPatch(typeof(DeathShadowCaster), "Update")]
    public static class DeathShadowCaster_Update_Perf_Patch
    {
        private const int ShadowUpdateRate = 4;
        private const float ShadowCullDistanceSq = 144f;
        private static System.Reflection.FieldInfo _rendererField;
        static bool Prefix(DeathShadowCaster __instance)
        {
            if ((Time.frameCount + __instance.gameObject.GetInstanceID()) % ShadowUpdateRate != 0)
                return false;
            bool anyoneClose = false;
            Vector3 pos = __instance.transform.position;
            foreach (var p in PlayerRegistry.Players)
            {
                if (p == null) continue;
                if ((p.transform.position - pos).sqrMagnitude < ShadowCullDistanceSq)
                {
                    anyoneClose = true;
                    break;
                }
            }
            if (!anyoneClose)
                return false;
            if (_rendererField == null)
            {
                _rendererField = typeof(DeathShadowCaster).GetField("_renderer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            }
            if (_rendererField != null)
            {
                var renderer = (Renderer)_rendererField.GetValue(__instance);
                if (renderer != null && !renderer.isVisible)
                    return false;
            }
            return true; 
        }
    }
    [HarmonyPatch(typeof(MatInterface_Pixelization), "Update")]
    public static class MatInterface_Pixelization_Update_Perf_Patch
    {
        static bool Prefix(MatInterface_Pixelization __instance)
        {
            return (Time.frameCount + __instance.gameObject.GetInstanceID()) % 8 == 0;
        }
    }
    [HarmonyPatch(typeof(MatInterface_PixelsSet), "LateUpdate")]
    public static class MatInterface_PixelsSet_LateUpdate_Patch
    {
        static bool Prefix(MatInterface_PixelsSet __instance)
        {
            return (Time.frameCount + __instance.gameObject.GetInstanceID()) % 4 == 0;
        }
    }
}