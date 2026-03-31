using HarmonyLib;
using Death.Run.Behaviours.Players;
namespace DeathMustDieCoop.Patches
{
    [HarmonyPatch(typeof(Behaviour_Player), "Awake")]
    public static class BehaviourPlayer_Awake_Patch
    {
        static void Postfix(Behaviour_Player __instance)
        {
            if (__instance.name.Contains("CharacterStatDummy"))
            {
                CoopPlugin.FileLog($"Behaviour_Player.Awake: {__instance.name} — skipped registration (dummy).");
                return;
            }
            PlayerRegistry.Register(__instance);
            bool isPrimary = Traverse.Create(__instance).Field("_isPrimaryPlayerInstance").GetValue<bool>();
            CoopPlugin.FileLog($"Behaviour_Player.Awake: {__instance.name}, isPrimary={isPrimary}");
            if (PlayerRegistry.Count >= 2 && !CoopFudgeStats.IsActive)
            {
                CoopFudgeStats.Init();
                CoopPlugin.FileLog("PlayerPatches: CoopFudgeStats initialized on P2 registration.");
            }
        }
    }
    [HarmonyPatch(typeof(Behaviour_Player), "Cleanup")]
    public static class BehaviourPlayer_Cleanup_Patch
    {
        static void Postfix(Behaviour_Player __instance)
        {
            if (__instance.name.Contains("CharacterStatDummy")) return;
            PlayerRegistry.Unregister(__instance);
            CoopPlugin.FileLog($"Behaviour_Player.Cleanup: {__instance.name}");
        }
    }
}