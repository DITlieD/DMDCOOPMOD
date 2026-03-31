using HarmonyLib;
using System.Collections.Generic;
using Death.Run.Behaviours;
using Death.Run.Behaviours.Entities;
using Death.Run.Behaviours.Players;
using Death.Sequencing;
using UnityEngine;
using Claw.Core;
namespace DeathMustDieCoop.Patches
{
    public class CoopCameraController : MonoBehaviour
    {
        private RunCamera _runCamera;
        private float _defaultOrthoSize;
        private float _minOrthoSize;
        private float _maxOrthoSize;
        private const float ZoomPadding = 4f;
        private const float MinZoomMultiplier = 1.25f;
        private const float MaxZoomMultiplier = 2.5f;
        private const float ZoomSpeed = 3f;
        private const float PanSpeed = 5f;
        private const float LeashEdgeMargin = 2.5f; 
        private static CoopCameraController _instance;
        public static CoopCameraController Instance => _instance;
        public void Init(RunCamera runCamera)
        {
            _runCamera = runCamera;
            _defaultOrthoSize = runCamera.OrthographicSize;
            _minOrthoSize = _defaultOrthoSize * MinZoomMultiplier;
            _maxOrthoSize = _defaultOrthoSize * MaxZoomMultiplier;
            _instance = this;
            CoopPlugin.FileLog($"CameraPatch: Init, defaultOrtho={_defaultOrthoSize}, max={_maxOrthoSize}");
        }
        private List<Behaviour_Player> _alivePlayersCache = new List<Behaviour_Player>(2);
        private void LateUpdate()
        {
            if (_runCamera == null) return;
            var players = PlayerRegistry.Players;
            if (players.Count == 0) return;
            _alivePlayersCache.Clear();
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null || !p.gameObject.activeInHierarchy) continue;
                if (p.Entity == null || !p.Entity.IsAlive) continue;
                _alivePlayersCache.Add(p);
            }
            if (_alivePlayersCache.Count == 0) return;
            if (_alivePlayersCache.Count == 2)
            {
                float aspect = _runCamera.Camera.aspect;
                float maxHalfY = _maxOrthoSize - LeashEdgeMargin;
                float maxHalfX = maxHalfY * aspect;
                Vector3 p1 = _alivePlayersCache[0].transform.position;
                Vector3 p2 = _alivePlayersCache[1].transform.position;
                Vector3 mid = (p1 + p2) * 0.5f;
                Vector3 off1 = p1 - mid;
                Vector3 off2 = p2 - mid;
                bool clamped = false;
                off1.x = Mathf.Clamp(off1.x, -maxHalfX, maxHalfX);
                off1.y = Mathf.Clamp(off1.y, -maxHalfY, maxHalfY);
                off2.x = Mathf.Clamp(off2.x, -maxHalfX, maxHalfX);
                off2.y = Mathf.Clamp(off2.y, -maxHalfY, maxHalfY);
                Vector3 newP1 = mid + off1;
                Vector3 newP2 = mid + off2;
                if (newP1 != p1)
                {
                    _alivePlayersCache[0].transform.position = new Vector3(newP1.x, newP1.y, p1.z);
                    clamped = true;
                }
                if (newP2 != p2)
                {
                    _alivePlayersCache[1].transform.position = new Vector3(newP2.x, newP2.y, p2.z);
                    clamped = true;
                }
            }
            Vector3 sum = Vector3.zero;
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < _alivePlayersCache.Count; i++)
            {
                Vector3 pos = _alivePlayersCache[i].transform.position;
                sum += pos;
                if (pos.x < minX) minX = pos.x;
                if (pos.x > maxX) maxX = pos.x;
                if (pos.y < minY) minY = pos.y;
                if (pos.y > maxY) maxY = pos.y;
            }
            int aliveCount = _alivePlayersCache.Count;
            Vector3 midpoint = sum / aliveCount;
            midpoint.z = 0f;
            transform.position = Vector3.Lerp(transform.position, midpoint, Time.deltaTime * PanSpeed);
            if (aliveCount > 1)
            {
                float aspect2 = _runCamera.Camera.aspect;
                float spanX = (maxX - minX) + ZoomPadding * 2f;
                float spanY = (maxY - minY) + ZoomPadding * 2f;
                float requiredForHeight = spanY / 2f;
                float requiredForWidth = (spanX / 2f) / aspect2;
                float required = Mathf.Max(requiredForHeight, requiredForWidth);
                float targetSize = Mathf.Clamp(required, _minOrthoSize, _maxOrthoSize);
                _runCamera.OrthographicSize = Mathf.Lerp(_runCamera.OrthographicSize, targetSize, Time.deltaTime * ZoomSpeed);
            }
            else
            {
                _runCamera.OrthographicSize = Mathf.Lerp(_runCamera.OrthographicSize, _minOrthoSize, Time.deltaTime * ZoomSpeed);
            }
        }
        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
        public static void SetupForRun()
        {
            Cleanup();
            var runCamera = SingletonBehaviour<RunCamera>.Instance;
            if (runCamera == null)
            {
                CoopPlugin.FileLog("CameraPatch: RunCamera not found, skipping.");
                return;
            }
            var go = new GameObject("__CoopCameraMidpoint__");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            var players = PlayerRegistry.Players;
            if (players.Count > 0 && players[0] != null)
                go.transform.position = players[0].transform.position;
            var controller = go.AddComponent<CoopCameraController>();
            controller.Init(runCamera);
            runCamera.VirtualCamera.Follow = go.transform;
            CoopPlugin.FileLog("CameraPatch: Midpoint tracker active, camera redirected.");
        }
        public float ZoomRatio
        {
            get
            {
                if (_runCamera == null || _defaultOrthoSize <= 0f) return 1f;
                return _runCamera.OrthographicSize / _defaultOrthoSize;
            }
        }
        public static void Cleanup()
        {
            if (_instance != null && _instance.gameObject != null)
            {
                Object.Destroy(_instance.gameObject);
                _instance = null;
                CoopPlugin.FileLog("CameraPatch: Cleaned up midpoint tracker.");
            }
        }
    }
    [HarmonyPatch(typeof(LevelUpFlash), "Play")]
    public static class LevelUpFlash_Play_Patch
    {
        static void Prefix(LevelUpFlash __instance)
        {
            if (PlayerRegistry.Count < 2) return;
            var ctrl = CoopCameraController.Instance;
            if (ctrl == null) return;
            float ratio = ctrl.ZoomRatio;
            __instance.transform.localScale = Vector3.one * ratio;
            foreach (var sr in __instance.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr.name.Contains("Blackness") || sr.name.Contains("Overlay"))
                {
                    sr.transform.localScale = Vector3.one * 20f;
                }
                else
                {
                    sr.transform.localScale = Vector3.one;
                }
            }
        }
    }
    [HarmonyPatch(typeof(PostLevelUpFlash), "Play")]
    public static class PostLevelUpFlash_Play_Patch
    {
        static void Prefix(PostLevelUpFlash __instance)
        {
            if (PlayerRegistry.Count < 2) return;
            var ctrl = CoopCameraController.Instance;
            if (ctrl == null) return;
            float ratio = ctrl.ZoomRatio;
            __instance.transform.localScale = Vector3.one * ratio;
            foreach (var sr in __instance.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr.name.Contains("Blackness") || sr.name.Contains("Overlay"))
                {
                    sr.transform.localScale = Vector3.one * 20f;
                }
                else
                {
                    sr.transform.localScale = Vector3.one;
                }
            }
        }
    }
    [HarmonyPatch(typeof(DeathSequence), "PlayAsync")]
    public static class DeathSequence_PlayAsync_Patch
    {
        static void Prefix(DeathSequence __instance)
        {
            if (PlayerRegistry.Count < 2) return;
            var ctrl = CoopCameraController.Instance;
            if (ctrl == null) return;
            float ratio = ctrl.ZoomRatio;
            __instance.transform.localScale = Vector3.one * ratio;
            foreach (var sr in __instance.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr.name.Contains("blackness") || sr.name.Contains("Overlay") || sr.name.Contains("Blackness"))
                {
                    sr.transform.localScale = Vector3.one * 20f;
                    CoopPlugin.FileLog($"DeathSequence: Scaled background child '{sr.name}' to 20x");
                }
                else
                {
                    sr.transform.localScale = Vector3.one;
                }
            }
            if (__instance.Header != null)
            {
                __instance.Header.transform.localScale = Vector3.one; 
            }
        }
    }
    [HarmonyPatch(typeof(PlayerLight), "SetOverlayEnabled")]
    public static class PlayerLight_SetOverlayEnabled_Patch
    {
        static void Prefix(PlayerLight __instance, bool value)
        {
            if (PlayerRegistry.Count < 2) return;
            if (!value) return; 
            var ctrl = CoopCameraController.Instance;
            if (ctrl == null) return;
            float ratio = ctrl.ZoomRatio;
            float finalScale = Mathf.Max(ratio * 4f, 10.0f);
            var sr = __instance.GetComponentInChildren<SpriteRenderer>(true);
            if (sr != null && sr.name.Contains("Overlay"))
            {
                sr.transform.localScale = Vector3.one * finalScale;
                CoopPlugin.FileLog($"CameraPatch: Scaled PlayerLight overlay to {finalScale:F2}x for zoom (ratio={ratio:F2}).");
            }
        }
    }
}