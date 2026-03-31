using HarmonyLib;
using Death.Run.Behaviours.Players;
using Death.Run.Behaviours;
using Death.Control;
using Death.ResourceManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using System.Collections.Generic;
using System.Reflection;
using Claw.Core;
using Death.Utils.Collections;
namespace DeathMustDieCoop.Patches
{
    [HarmonyPatch(typeof(PlayerInputProcessor), "Awake")]
    public static class PlayerInputProcessor_Awake_Patch
    {
        static bool Prefix(PlayerInputProcessor __instance)
        {
            var player = __instance.GetComponent<Behaviour_Player>();
            if (player == null) return true;
            bool isPrimary = Traverse.Create(player).Field("_isPrimaryPlayerInstance").GetValue<bool>();
            bool isP2Spawn = PlayerRegistry.SpawningP2;
            CoopPlugin.FileLog($"InputPatch: Awake on {__instance.gameObject.name}, isPrimary={isPrimary}, SpawningP2={isP2Spawn}");
            if (!isP2Spawn) return true; 
            CoopPlugin.FileLog("InputPatch: P2 PlayerInputProcessor — skipping global bindings.");
            try
            {
                var trav = Traverse.Create(__instance);
                var runCam = SingletonBehaviour<RunCamera>.Instance;
                trav.Field("_camera").SetValue(runCam);
                var config = ConfigManager.Get<InputConfig>();
                trav.Field("_config").SetValue(config.Player);
                var instances = Traverse.Create(typeof(PlayerInputProcessor)).Field("Instances").GetValue<List<PlayerInputProcessor>>();
                instances.Add(__instance);
                var gEnabled = Traverse.Create(typeof(PlayerInputProcessor)).Field("gEnabled").GetValue<BoolStack>();
                __instance.enabled = gEnabled;
                CoopPlugin.FileLog("InputPatch: P2 setup complete.");
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"InputPatch: ERROR during P2 setup: {ex}");
            }
            return false;
        }
        static void Postfix(PlayerInputProcessor __instance)
        {
            if (PlayerRegistry.SpawningP2) return;
            var player = __instance.GetComponent<Behaviour_Player>();
            if (player == null) return;
            bool isPrimary = Traverse.Create(player).Field("_isPrimaryPlayerInstance").GetValue<bool>();
            if (!isPrimary) return;
            try
            {
                var playerMap = Death.Game.Controls.Player.Get();
                int overrideCount = 0;
                foreach (var action in playerMap)
                {
                    var bindings = action.bindings;
                    for (int i = 0; i < bindings.Count; i++)
                    {
                        string groups = bindings[i].groups;
                        if (groups != null && groups.Contains("Gamepad"))
                        {
                            action.ApplyBindingOverride(i, new InputBinding { overridePath = "" });
                            overrideCount++;
                        }
                    }
                }
                CoopPlugin.FileLog($"InputPatch: Nulled {overrideCount} gamepad bindings on Player action map.");
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"InputPatch: ERROR removing gamepad bindings: {ex}");
            }
        }
    }
    [HarmonyPatch(typeof(PrimaryInput), "Awake")]
    public static class PrimaryInput_Awake_Patch
    {
        static void Postfix(PrimaryInput __instance)
        {
            var playerInput = __instance.GetComponent<PlayerInput>();
            if (playerInput != null)
            {
                playerInput.neverAutoSwitchControlSchemes = true;
                if (Gamepad.current != null)
                {
                    InputUser.PerformPairingWithDevice(Gamepad.current, playerInput.user);
                    if (playerInput.actions != null)
                    {
                        playerInput.actions.bindingMask = null;
                        CoopPlugin.FileLog($"InputPatch: PrimaryInput.Awake — cleared bindingMask on actions asset.");
                    }
                    CoopPlugin.FileLog($"InputPatch: PrimaryInput.Awake — scheme: {PrimaryInput.CurSchemeName}, autoSwitch DISABLED, gamepad paired, mask cleared.");
                }
                else
                {
                    CoopPlugin.FileLog($"InputPatch: PrimaryInput.Awake — scheme: {PrimaryInput.CurSchemeName}, autoSwitch DISABLED, no gamepad to pair.");
                }
            }
        }
    }
    [HarmonyPatch(typeof(PrimaryInput), "SetControlScheme")]
    public static class PrimaryInput_SetControlScheme_Patch
    {
        public static bool AllowNextChange;
        static bool Prefix(string scheme)
        {
            if (AllowNextChange)
            {
                AllowNextChange = false;
                return true;
            }
            if (GamepadInputHandler.P2InMenu)
            {
                return scheme == "Gamepad";
            }
            if (scheme == "Gamepad")
                return false;
            return true;
        }
    }
    [HarmonyPatch(typeof(PlayerInteraction), "OnEnable")]
    public static class PlayerInteraction_OnEnable_Patch
    {
        public static readonly HashSet<PlayerInteraction> P2Instances = new HashSet<PlayerInteraction>();
        public static PlayerInteraction P1Instance;
        static bool Prefix(PlayerInteraction __instance)
        {
            if (PlayerRegistry.SpawningP2)
            {
                P2Instances.Add(__instance);
                CoopPlugin.FileLog("InputPatch: P2 PlayerInteraction OnEnable — skipped global Interact binding.");
                return false;
            }
            if (P2Instances.Contains(__instance))
                return false; 
            var player = __instance.GetComponent<Behaviour_Player>();
            if (player != null)
            {
                bool isPrimary = Traverse.Create(player).Field("_isPrimaryPlayerInstance").GetValue<bool>();
                if (isPrimary)
                {
                    P1Instance = __instance;
                }
                else if (PlayerRegistry.IsRegistered(player))
                {
                    P2Instances.Add(__instance);
                    CoopPlugin.FileLog("InputPatch: P2 PlayerInteraction re-enabled — re-added to P2Instances.");
                    return false;
                }
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(PlayerInteraction), "OnDisable")]
    public static class PlayerInteraction_OnDisable_Patch
    {
        static bool Prefix(PlayerInteraction __instance)
        {
            if (__instance == PlayerInteraction_OnEnable_Patch.P1Instance)
                PlayerInteraction_OnEnable_Patch.P1Instance = null;
            if (!PlayerInteraction_OnEnable_Patch.P2Instances.Contains(__instance))
                return true; 
            try
            {
                if (__instance.gameObject.scene.isLoaded)
                {
                    var setTarget = typeof(PlayerInteraction).GetMethod("SetInteractionTarget",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    setTarget?.Invoke(__instance, new object[] { null });
                }
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"InputPatch: P2 PlayerInteraction OnDisable error: {ex.Message}");
            }
            PlayerInteraction_OnEnable_Patch.P2Instances.Remove(__instance);
            return false;
        }
    }
    [HarmonyPatch(typeof(PlayerInteraction), "SetInteractionTarget")]
    public static class PlayerInteraction_SetTarget_Patch
    {
        private static FieldInfo _interactableField;
        private static FieldInfo _controlsChangedField;
        private static bool _controlsChangedCached;
        private static object _preCallTarget;
        static bool Prefix(PlayerInteraction __instance, object __0)
        {
            EnsureField();
            if (PlayerInteraction_OnEnable_Patch.P2Instances.Contains(__instance))
            {
                var current = _interactableField?.GetValue(__instance);
                if (!ReferenceEquals(__0, current))
                {
                    bool shouldBeP2 = (__0 != null);
                    if (shouldBeP2 != GamepadInputHandler.P2HasInteractFocus)
                    {
                        GamepadInputHandler.P2HasInteractFocus = shouldBeP2;
                        FireControlsChanged();
                    }
                }
            }
            if (__instance == PlayerInteraction_OnEnable_Patch.P1Instance)
                _preCallTarget = _interactableField?.GetValue(__instance);
            return true; 
        }
        static void Postfix(PlayerInteraction __instance, object __0)
        {
            if (__instance != PlayerInteraction_OnEnable_Patch.P1Instance)
                return;
            if (ReferenceEquals(__0, _preCallTarget))
                return;
            if (__0 != null)
            {
                if (GamepadInputHandler.P2HasInteractFocus)
                {
                    GamepadInputHandler.P2HasInteractFocus = false;
                    FireControlsChanged();
                }
            }
            else
            {
                bool p2Focus = CheckP2HasFocus();
                if (p2Focus != GamepadInputHandler.P2HasInteractFocus)
                {
                    GamepadInputHandler.P2HasInteractFocus = p2Focus;
                    FireControlsChanged();
                }
            }
        }
        private static void EnsureField()
        {
            if (_interactableField == null)
                _interactableField = typeof(PlayerInteraction).GetField("_currentInteractable",
                    BindingFlags.NonPublic | BindingFlags.Instance);
        }
        private static bool CheckP2HasFocus()
        {
            if (_interactableField == null) return false;
            foreach (var pi in PlayerInteraction_OnEnable_Patch.P2Instances)
            {
                if (pi == null) continue;
                if (_interactableField.GetValue(pi) != null) return true;
            }
            return false;
        }
        private static void FireControlsChanged()
        {
            try
            {
                if (!_controlsChangedCached)
                {
                    _controlsChangedCached = true;
                    _controlsChangedField = typeof(PrimaryInput).GetField("OnControlsChangedEv",
                        BindingFlags.NonPublic | BindingFlags.Static);
                }
                if (_controlsChangedField != null)
                {
                    var handler = _controlsChangedField.GetValue(null) as System.Action;
                    handler?.Invoke();
                }
            }
            catch { }
        }
    }
    [HarmonyPatch]
    public static class InputPromptUtils_P2Override_Patch
    {
        private static System.Reflection.MethodInfo _tryGetSpriteId;
        static bool Prepare()
        {
            try { return TargetMethod() != null; }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"InputPromptUtils patch: Prepare failed: {ex.Message}");
                return false;
            }
        }
        static MethodBase TargetMethod()
        {
            var type = typeof(Death.Game).Assembly.GetType("Death.UserInterface.Prompts.InputPromptUtils");
            if (type == null)
            {
                CoopPlugin.FileLog("InputPromptUtils patch: type not found!");
                return null;
            }
            return type.GetMethod("GetPromptString",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null,
                new System.Type[] { typeof(InputAction) },
                null);
        }
        static bool Prefix(InputAction action, ref string __result)
        {
            if (!GamepadInputHandler.P2HasInteractFocus) return true;
            if (action == null) return true;
            foreach (var binding in action.bindings)
            {
                if (binding.isComposite || binding.isPartOfComposite) continue;
                string groups = binding.groups;
                if (groups == null || !groups.Contains("Gamepad")) continue;
                string originalPath = binding.path;
                if (string.IsNullOrEmpty(originalPath)) continue;
                if (_tryGetSpriteId == null)
                {
                    var emojisType = typeof(Death.Game).Assembly
                        .GetType("Death.UserInterface.Emojis");
                    if (emojisType != null)
                    {
                        _tryGetSpriteId = emojisType.GetMethod("TryGetSpriteId",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    }
                }
                if (_tryGetSpriteId != null)
                {
                    var args = new object[] { originalPath, 0 };
                    bool found = (bool)_tryGetSpriteId.Invoke(null, args);
                    if (found)
                    {
                        __result = $"<sprite={(int)args[1]}>";
                        return false;
                    }
                }
            }
            return true; 
        }
    }
    [HarmonyPatch(typeof(PlayerInputProcessor), "OnDestroy")]
    public static class PlayerInputProcessor_OnDestroy_Patch
    {
        static bool Prefix(PlayerInputProcessor __instance)
        {
            var player = __instance.GetComponent<Behaviour_Player>();
            if (player == null) return true;
            bool isPrimary = Traverse.Create(player).Field("_isPrimaryPlayerInstance").GetValue<bool>();
            if (isPrimary) return true;
            var instances = Traverse.Create(typeof(PlayerInputProcessor)).Field("Instances").GetValue<List<PlayerInputProcessor>>();
            instances.Remove(__instance);
            return false;
        }
    }
}