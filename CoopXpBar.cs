using Death.Run.Behaviours;
using Death.Run.Behaviours.Players;
using Claw.Core;
using Cinemachine;
using UnityEngine;
using UnityEngine.UI;
namespace DeathMustDieCoop
{
    public class CoopXpBar : MonoBehaviour
    {
        private Behaviour_Player _player;
        private RunCamera _camera;
        private RectTransform _rectTransform;
        private Image _fillXp;
        private int _prevLevel = -1;
        private float _flashTimer;
        private const float YOffsetAboveHealth = 12f;
        private const float LerpFactor = 0.9f;
        private const float FlashDuration = 0.40f;
        public void Init(Behaviour_Player player, Image fillImage)
        {
            _player = player;
            _camera = SingletonBehaviour<RunCamera>.Instance;
            _rectTransform = GetComponent<RectTransform>();
            _rectTransform.anchorMin = Vector2.zero;
            _rectTransform.anchorMax = Vector2.zero;
            _fillXp = fillImage;
            _fillXp.color = new Color(1f, 0.85f, 0.1f, 1f);
            UpdateFill();
        }
        private void OnEnable()
        {
            CinemachineCore.CameraUpdatedEvent.AddListener(HandleCameraUpdated);
        }
        private void OnDisable()
        {
            CinemachineCore.CameraUpdatedEvent.RemoveListener(HandleCameraUpdated);
        }
        private void HandleCameraUpdated(CinemachineBrain brain)
        {
            if (_player == null || _player.Entity == null || _camera == null) return;
            if (!_player.Entity.IsAlive)
            {
                gameObject.SetActive(false);
                return;
            }
            UpdatePosition();
            UpdateFill();
        }
        private void UpdatePosition()
        {
            float healthOffset = _player.Entity.DamageHandler.HealthBarOffset;
            Vector3 worldPos = _player.transform.position + Vector3.up * healthOffset;
            _rectTransform.anchoredPosition = _camera.WorldToCanvasPoint(worldPos) + Vector2.up * YOffsetAboveHealth;
        }
        private void UpdateFill()
        {
            if (_player.XpTracker == null) return;
            float xpForNext = _player.XpTracker.XpForNextLevel;
            float target = (xpForNext > 0f) ? (_player.XpTracker.CurXp / xpForNext) : 1f;
            if (_player.XpTracker.CurLevel != _prevLevel)
            {
                _prevLevel = _player.XpTracker.CurLevel;
                _fillXp.fillAmount = 1f;
                _flashTimer = FlashDuration;
                return;
            }
            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.unscaledDeltaTime;
                _fillXp.fillAmount = 1f;
                if (_flashTimer <= 0f)
                    _fillXp.fillAmount = target; 
                return;
            }
            if (target < _fillXp.fillAmount)
                _fillXp.fillAmount = target;
            else
                _fillXp.fillAmount = Mathf.Lerp(_fillXp.fillAmount, target, LerpFactor);
        }
    }
}