using HarmonyLib;
using Death.App;
using Death.TimesRealm;
using UnityEngine;
using Cysharp.Threading.Tasks;
namespace DeathMustDieCoop.Patches
{
    [HarmonyPatch(typeof(Profile), "TryGetNewlyUnlockedCharacter")]
    public static class NewCharacterHookPatch
    {
        static void Postfix(Profile __instance, bool __result)
        {
            if (!__result) return;
            var gameManager = GameObject.FindWithTag("GameManager")?.GetComponent<GameManager>();
            if (gameManager == null) return;
            if (__instance == gameManager.ProfileManager.Active)
            {
                __instance.TryGetNewlyUnlockedCharacter(out string charId);
                CoopPlugin.FileLog($"NewCharacterHookPatch: New character detected: '{charId}'. " +
                    $"RevealedCount={__instance.Progression.RevealedCharacterCount}");
            }
        }
    }
    [HarmonyPatch(typeof(Facade_Lobby), "MarkCharacterRevealed")]
    public static class MarkCharacterRevealed_Patch
    {
        static void Postfix(Facade_Lobby __instance)
        {
            try
            {
                var state = Traverse.Create(__instance).Field("_state").GetValue<ILobbyGameState>();
                string charCode = state?.ActiveProfile?.Progression?.SelectedCharacterCode;
                CoopPlugin.FileLog($"MarkCharacterRevealed: Marked '{charCode}' as revealed. " +
                    $"RevealedCount={state?.ActiveProfile?.Progression?.RevealedCharacterCount}");
                var gameManager = GameObject.FindWithTag("GameManager")?.GetComponent<GameManager>();
                if (gameManager != null)
                {
                    gameManager.ProfileManager.SaveActiveProfileAsync().Forget();
                    CoopPlugin.FileLog("MarkCharacterRevealed: Forced profile save.");
                }
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"MarkCharacterRevealed: Postfix error (non-fatal): {ex.Message}");
            }
        }
    }
}