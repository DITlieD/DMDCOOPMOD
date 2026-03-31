using HarmonyLib;
using Death.Run.Behaviours;
using Death.Run.Behaviours.Entities;
using Death.Run.Core;
using Death.Run.Systems;
using Death.Data;
using UnityEngine;
using System.Reflection;
namespace DeathMustDieCoop.Patches
{
    [HarmonyPatch(typeof(System_PowerUpDropper), "Drop",
        new[] { typeof(Behaviour_Monster) })]
    public static class PowerUpDropper_Drop_Patch
    {
        private static FieldInfo _enabledField;
        private static PropertyInfo _rngProp;
        private static bool _cacheInit;
        static void Postfix(object __instance, Behaviour_Monster monster)
        {
            if (PlayerRegistry.Count < 2) return;
            try
            {
                if (!_cacheInit)
                {
                    var t = __instance.GetType();
                    _enabledField = t.GetField("_enabled",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    _rngProp = t.BaseType.GetProperty("Rng",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    _cacheInit = true;
                }
                if (_enabledField != null)
                {
                    var enabled = _enabledField.GetValue(__instance);
                    if (!((Death.Utils.Collections.BoolStack)enabled)) return;
                }
                var powerUpDrop = monster.Data.PowerUpDrop;
                if (powerUpDrop.IsEmpty) return;
                RuntimeStats stats = Player.Stats;
                float boonMod = stats.Modifier.GetTotalBoonMod(StatId.PickUpDropRate);
                float itemMod = stats.Modifier.GetTotalItemMod(StatId.PickUpDropRate);
                if (stats.Modifier.GetTotalScalingFlag(StatId.Chance))
                {
                    boonMod += stats.Modifier.GetTotalBoonMod(StatId.Chance);
                    itemMod += stats.Modifier.GetTotalItemMod(StatId.Chance);
                }
                float chance = StatRules.ApplyModifiers(powerUpDrop.Chance, boonMod, itemMod);
                var rng = _rngProp?.GetValue(__instance);
                if (rng == null) return;
                var rollMethod = rng.GetType().GetMethod("RollChance",
                    BindingFlags.Public | BindingFlags.Instance);
                if (rollMethod == null) return;
                bool rolled = (bool)rollMethod.Invoke(rng, new object[] { chance });
                if (rolled)
                {
                    var weights = powerUpDrop.Weights;
                    var pickMethod = weights.GetType().GetMethod("PickRandom",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (pickMethod == null) return;
                    string id = (string)pickMethod.Invoke(weights, new object[] { rng });
                    PowerUpSpawner.Spawn(monster.transform.position, Database.PowerUps.Get(id));
                }
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"PowerUpDropPatch: Error: {ex.Message}");
            }
        }
    }
}