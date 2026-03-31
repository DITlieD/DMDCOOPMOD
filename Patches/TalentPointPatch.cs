using HarmonyLib;
using Death;
using Death.Run.Behaviours.Objects;
using Death.Run.Behaviours.Players;
using Death.Run.Core;
using Death.Run.Systems;
using UnityEngine;
using System.Reflection;
namespace DeathMustDieCoop.Patches
{
    [HarmonyPatch(typeof(TalentPointPickUp), "OnInteract")]
    public static class TalentPointPickUp_OnInteract_Patch
    {
        private static FieldInfo _animPlayerField;
        private static FieldInfo _sfxOpenField;
        private static MethodInfo _fire2DMethod;
        private static MethodInfo _playEndMethod;
        private static bool _cacheInit;
        static void InitCache(object instance)
        {
            if (_cacheInit) return;
            var t = instance.GetType();
            _animPlayerField = t.GetField("_animationPlayer",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _sfxOpenField = t.GetField("Sfx_Open",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (_sfxOpenField != null)
            {
                var cueRef = _sfxOpenField.GetValue(null);
                if (cueRef != null)
                    _fire2DMethod = cueRef.GetType().GetMethod("Fire2D",
                        BindingFlags.Public | BindingFlags.Instance, null, System.Type.EmptyTypes, null);
            }
            _cacheInit = true;
        }
        static bool Prefix(object __instance, Interactable interactable, Behaviour_Player player)
        {
            if (PlayerRegistry.Count < 2) return true; 
            try
            {
                var p1Profile = Game.ActiveProfile;
                string p1Char = p1Profile.Progression.SelectedCharacterCode;
                int p1Before = p1Profile.TalentsState.GetTotalPoints(CharacterCode.FromString(p1Char));
                if (p1Before < 36)
                    p1Profile.TalentsState.AddPoints(CharacterCode.FromString(p1Char), 1);
                var p2Profile = CoopP2Profile.Instance;
                if (p2Profile != null)
                {
                    string p2Char = p2Profile.Progression.SelectedCharacterCode;
                    int p2Before = p2Profile.TalentsState.GetTotalPoints(CharacterCode.FromString(p2Char));
                    if (p2Before < 36)
                        p2Profile.TalentsState.AddPoints(CharacterCode.FromString(p2Char), 1);
                }
                InitCache(__instance);
                if (_sfxOpenField != null && _fire2DMethod != null)
                {
                    var cueRef = _sfxOpenField.GetValue(null);
                    if (cueRef != null)
                        _fire2DMethod.Invoke(cueRef, null);
                }
                if (_animPlayerField != null)
                {
                    var animPlayer = _animPlayerField.GetValue(__instance);
                    if (animPlayer != null)
                    {
                        if (_playEndMethod == null)
                            _playEndMethod = animPlayer.GetType().GetMethod("PlayEnd",
                                BindingFlags.Public | BindingFlags.Instance);
                        if (_playEndMethod != null)
                            _playEndMethod.Invoke(animPlayer, null);
                    }
                }
                if (interactable != null)
                    interactable.enabled = false;
                bool isPrimary = Traverse.Create(player).Field("_isPrimaryPlayerInstance").GetValue<bool>();
                CoopPlugin.FileLog($"TalentPointPatch: {(isPrimary ? "P1" : "P2")} picked up essence → both players +1 point.");
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"TalentPointPatch: Error: {ex.Message}");
                return true; 
            }
            return false; 
        }
    }
    [HarmonyPatch(typeof(System_TalentPointDropper), "DropTalentPoints")]
    public static class TalentPointDropper_DropTalentPoints_Patch
    {
        private static FieldInfo _pickUpFabField;
        private static FieldInfo _uiConfigField;
        private static MethodInfo _onInteractMethod;
        private static FieldInfo _mapIconField;
        private static bool _cacheInit;
        static bool Prefix(object __instance, int points, Vector2 pos)
        {
            if (PlayerRegistry.Count < 2) return true;
            try
            {
                var profile = Game.ActiveProfile;
                int p1Total = profile.TalentsState.GetTotalPoints(
                    CharacterCode.FromString(profile.Progression.SelectedCharacterCode));
                int p1Room = 36 - p1Total;
                int p2Room = 0;
                var p2Profile = CoopP2Profile.Instance;
                if (p2Profile != null)
                {
                    int p2Total = p2Profile.TalentsState.GetTotalPoints(
                        CharacterCode.FromString(p2Profile.Progression.SelectedCharacterCode));
                    p2Room = 36 - p2Total;
                }
                int maxRoom = Mathf.Max(p1Room, p2Room);
                int toDrop = Mathf.Min(points, maxRoom);
                if (toDrop <= 0) return false; 
                if (!_cacheInit)
                {
                    var t = __instance.GetType();
                    _pickUpFabField = t.GetField("_pickUpFab",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    _uiConfigField = t.GetField("_uiConfig",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    _onInteractMethod = t.GetMethod("OnInteract",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    _cacheInit = true;
                }
                if (_pickUpFabField == null || _uiConfigField == null || _onInteractMethod == null)
                {
                    CoopPlugin.FileLog("TalentPointPatch: reflection failed on DropTalentPoints, falling back.");
                    return true;
                }
                var pickUpFab = _pickUpFabField.GetValue(__instance) as TalentPointPickUp;
                var uiConfig = _uiConfigField.GetValue(__instance);
                if (_mapIconField == null)
                    _mapIconField = uiConfig.GetType().GetField("MapIcon_TalentPoint",
                        BindingFlags.Public | BindingFlags.Instance);
                var mapIcon = _mapIconField != null ? _mapIconField.GetValue(uiConfig) as Sprite : null;
                var onInteractDelegate = (System.Action<TalentPointPickUp>)System.Delegate.CreateDelegate(
                    typeof(System.Action<TalentPointPickUp>), __instance, _onInteractMethod);
                for (int i = 0; i < toDrop; i++)
                {
                    Object.Instantiate(pickUpFab).Init(pos, mapIcon, onInteractDelegate);
                }
                return false;
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"TalentPointPatch: DropTalentPoints error: {ex.Message}");
                return true;
            }
        }
    }
}