using UnityEngine;
using UnityEngine.InputSystem;
using Death.Run.Behaviours.Players;
using Death.Run.Behaviours;
using Death.Run.Behaviours.Objects;
using Death.Control;
using HarmonyLib;
using System.Reflection;
namespace DeathMustDieCoop
{
    public class GamepadInputHandler : MonoBehaviour
    {
        public static bool LastInteractWasP2 { get; set; }
        public static bool P2HasInteractFocus;
        public static bool P2InMenu { get; set; }
        public static void EnableGamepadUI()
        {
            P2InMenu = true;
            try
            {
                DeathMustDieCoop.Patches.PrimaryInput_SetControlScheme_Patch.AllowNextChange = true;
                Traverse.Create(typeof(Death.Control.PrimaryInput))
                    .Method("SetControlScheme", new System.Type[] { typeof(string) })
                    .GetValue("Gamepad");
                CoopPlugin.FileLog("GamepadInputHandler: EnableGamepadUI — scheme=Gamepad.");
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"GamepadInputHandler: EnableGamepadUI error: {ex.Message}");
            }
        }
        public static void DisableGamepadUI()
        {
            P2InMenu = false;
            LastInteractWasP2 = false;
            try
            {
                DeathMustDieCoop.Patches.PrimaryInput_SetControlScheme_Patch.AllowNextChange = true;
                Traverse.Create(typeof(Death.Control.PrimaryInput))
                    .Method("SetControlScheme", new System.Type[] { typeof(string) })
                    .GetValue("MouseKeyboard");
                CoopPlugin.FileLog("GamepadInputHandler: DisableGamepadUI — scheme=MouseKeyboard.");
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"GamepadInputHandler: DisableGamepadUI error: {ex.Message}");
            }
        }
        private PlayerInputProcessor _processor;
        private PlayerInteraction _interaction;
        private float _attackCooldown;
        private float _dashCooldown;
        private bool _initialized;
        private bool _prevAutoAttackBtn;
        private bool _prevInteractBtn;
        private bool _prevInventoryBtn;
        private bool _prevAbilitiesBtn;
        private static FieldInfo _moveDirField;
        private static FieldInfo _lastAimMagField;
        private static PropertyInfo _aimProp;
        private static FieldInfo _actionCallbacksField;
        private static FieldInfo _currentInteractableField;
        private static bool _reflectionCached;
        private static void EnsureReflectionCache()
        {
            if (_reflectionCached) return;
            _reflectionCached = true;
            var pipType = typeof(PlayerInputProcessor);
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            _moveDirField = pipType.GetField("_moveDir", flags);
            _lastAimMagField = pipType.GetField("_lastAimMagnitude", flags);
            _aimProp = pipType.GetProperty("Aim", BindingFlags.Public | BindingFlags.Instance);
            _actionCallbacksField = pipType.GetField("_actionCallbacks", flags);
            _currentInteractableField = typeof(PlayerInteraction).GetField("_currentInteractable", flags);
        }
        private void Start()
        {
            _processor = GetComponent<PlayerInputProcessor>();
            _interaction = GetComponent<PlayerInteraction>();
            EnsureReflectionCache();
            if (_processor != null)
            {
                _initialized = true;
                CoopPlugin.FileLog("GamepadInputHandler: bound to P2's PlayerInputProcessor.");
            }
            if (_interaction == null)
            {
                CoopPlugin.FileLog("GamepadInputHandler: WARNING — no PlayerInputProcessor on this entity.");
            }
        }
        private void Update()
        {
            if (!_initialized || !_processor.enabled) return;
            var gamepad = GetP2Gamepad();
            if (gamepad == null) return;
            bool inventoryBtn = gamepad.buttonWest.isPressed;
            if (inventoryBtn && !_prevInventoryBtn && !P2InMenu)
            {
                LastInteractWasP2 = true;
            }
            _prevInventoryBtn = inventoryBtn;
            bool abilitiesBtn = gamepad.buttonNorth.isPressed;
            if (abilitiesBtn && !_prevAbilitiesBtn && !P2InMenu)
            {
                LastInteractWasP2 = true;
            }
            _prevAbilitiesBtn = abilitiesBtn;
            if (P2InMenu) return;
            Vector2 move = gamepad.leftStick.ReadValue();
            _moveDirField.SetValue(_processor, move);
            Vector2 aimRaw = gamepad.rightStick.ReadValue();
            float aimMag = aimRaw.magnitude;
            _lastAimMagField.SetValue(_processor, aimMag);
            if (aimMag > 0.2f)
            {
                _aimProp.SetValue(_processor, PlayerAim.Direction(aimRaw.normalized));
            }
            _attackCooldown -= Time.deltaTime;
            if (gamepad.rightTrigger.isPressed && _attackCooldown <= 0f)
            {
                _attackCooldown = 0.15f;
                FireAction(PlayerAction.Attack);
            }
            _dashCooldown -= Time.deltaTime;
            if (gamepad.leftTrigger.isPressed && _dashCooldown <= 0f)
            {
                _dashCooldown = 0.3f;
                FireAction(PlayerAction.Dash);
            }
            bool autoAttackBtn = gamepad.rightShoulder.isPressed;
            if (autoAttackBtn && !_prevAutoAttackBtn)
            {
                _processor.AutoAttackEnabled = !_processor.AutoAttackEnabled;
            }
            _prevAutoAttackBtn = autoAttackBtn;
            if (_processor.AutoAttackEnabled && aimMag > 0.5f && _attackCooldown <= 0f)
            {
                _attackCooldown = 0.15f;
                FireAction(PlayerAction.Attack);
            }
            bool interactBtn = gamepad.buttonSouth.isPressed;
            if (interactBtn && !_prevInteractBtn)
            {
                if (_interaction != null)
                {
                    var interactable = _currentInteractableField.GetValue(_interaction) as Interactable;
                    if (interactable != null)
                    {
                        LastInteractWasP2 = true;
                        var player = GetComponent<Behaviour_Player>();
                        if (player != null)
                            interactable.Interact(player);
                    }
                }
            }
            _prevInteractBtn = interactBtn;
        }
        private void FireAction(PlayerAction action)
        {
            var callbacks = _actionCallbacksField.GetValue(_processor) as PlayerInputProcessor.ActionCallback[];
            if (callbacks != null && (int)action < callbacks.Length)
            {
                callbacks[(int)action]?.Invoke();
            }
        }
        private Gamepad GetP2Gamepad()
        {
            var pads = Gamepad.all;
            if (pads.Count == 0) return null;
            return pads.Count == 1 ? pads[0] : pads[1];
        }
    }
}