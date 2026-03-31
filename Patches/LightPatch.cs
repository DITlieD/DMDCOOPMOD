using UnityEngine;
using Death.Run.Behaviours.Players;
namespace DeathMustDieCoop.Patches
{
    public static class LightPatch_ScaleUp
    {
        private const float LightScale = 3f;
        public static void ScaleLight(PlayerLight light)
        {
            if (light == null) return;
            light.transform.localScale *= LightScale;
            var sr = light.GetComponentInChildren<SpriteRenderer>();
            if (sr != null)
            {
                var c = sr.color;
                c.a /= LightScale;
                sr.color = c;
                CoopPlugin.FileLog($"LightPatch: Scaled light to {LightScale}x, alpha reduced to {c.a:F3}");
            }
            else
            {
                CoopPlugin.FileLog($"LightPatch: Scaled light to {LightScale}x (no SpriteRenderer found for alpha adjust)");
            }
        }
        public static void ScaleAllLights()
        {
            var allLights = Object.FindObjectsOfType<PlayerLight>();
            foreach (var light in allLights)
            {
                if (light.transform.localScale.x < LightScale * 0.5f)
                {
                    ScaleLight(light);
                }
            }
        }
    }
}