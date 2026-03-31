using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using HarmonyLib;
using Death;
using Death.Run.Core;
using Death.Run.Behaviours.Events;
using Death.Rendering;
using Death.Run.Behaviours.TerrainEffects;
using Death.Run.Behaviours.Entities;
namespace DeathMustDieCoop.Patches
{
    public class VisualClarityController : MonoBehaviour
    {
        private const float HitFlashIntensity = 0.2f;
        private const float HitFlashDuration = 0.15f;
        private const float AbilityVfxAlpha = 0.8f;
        private const float SaturationBoost = 1.0f;
        private const float ContrastBoost = 1.1f;
        private const float VfxScanInterval = 0.5f;
        private static VisualClarityController _instance;
        public static VisualClarityController Instance => _instance;
        private readonly Dictionary<int, FlashState> _flashingEntities = new Dictionary<int, FlashState>();
        private readonly List<int> _expiredFlashes = new List<int>();
        private float _nextVfxScan;
        private bool _loggedFirstScan;
        private struct FlashState
        {
            public MatInterface_PixelsSet MatInterface;
            public float TimeRemaining;
        }
        public static void Setup()
        {
            Cleanup();
            var go = new GameObject("__CoopVisualClarity__");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<VisualClarityController>();
            CoopPlugin.FileLog("VisualClarity: Controller active.");
        }
        public static void Cleanup()
        {
            if (_instance != null && _instance.gameObject != null)
            {
                Object.Destroy(_instance.gameObject);
                _instance = null;
            }
        }
        private void Awake()
        {
            _instance = this;
        }
        private void OnEnable()
        {
            Death.Event.AddListener<Event_TookDamage>(OnTookDamage);
        }
        private void OnDisable()
        {
            Death.Event.RemoveListener<Event_TookDamage>(OnTookDamage);
        }
        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
        private void OnTookDamage(Event_TookDamage ev)
        {
            if (ev.Entity == null) return;
            if (ev.Entity.Type != EntityType.Monster) return;
            var matInterface = ev.Entity.GetComponent<MatInterface_PixelsSet>();
            if (matInterface == null) return;
            int id = ev.Entity.GetInstanceID();
            _flashingEntities[id] = new FlashState
            {
                MatInterface = matInterface,
                TimeRemaining = HitFlashDuration
            };
            matInterface.SetFlash(HitFlashIntensity);
        }
        private void Update()
        {
            UpdateFlashes();
            UpdateAbilityFade();
        }
        private static readonly List<int> _keysCache = new List<int>(32);
        private void UpdateFlashes()
        {
            if (_flashingEntities.Count == 0) return;
            _expiredFlashes.Clear();
            float dt = Time.deltaTime;
            _keysCache.Clear();
            _keysCache.AddRange(_flashingEntities.Keys);
            foreach (var id in _keysCache)
            {
                var state = _flashingEntities[id];
                state.TimeRemaining -= dt;
                if (state.MatInterface == null || state.TimeRemaining <= 0f)
                {
                    if (state.MatInterface != null)
                        state.MatInterface.SetFlash(0f);
                    _expiredFlashes.Add(id);
                }
                else
                {
                    float t = state.TimeRemaining / HitFlashDuration;
                    state.MatInterface.SetFlash(HitFlashIntensity * t);
                    _flashingEntities[id] = state;
                }
            }
            foreach (var id in _expiredFlashes)
                _flashingEntities.Remove(id);
        }
        private void UpdateAbilityFade()
        {
            if (Time.time < _nextVfxScan) return;
            _nextVfxScan = Time.time + 1.5f; 
            var entities = FindObjectsOfType<Entity>();
            var terrainEffects = FindObjectsOfType<TerrainEffect>();
            int fadedCount = 0;
            foreach (var entity in entities)
            {
                if (entity == null || !entity.gameObject.activeInHierarchy) continue;
                if (entity.Type == EntityType.Player) continue;
                if (entity.Type == EntityType.Monster)
                {
                    var layers = entity.GetComponentsInChildren<CharacterEffectLayer>(true);
                    foreach (var layer in layers)
                    {
                        var srs = layer.GetComponentsInChildren<SpriteRenderer>(true);
                        foreach (var sr in srs)
                        {
                            if (sr.color.a > AbilityVfxAlpha)
                            {
                                sr.color = FadeAndBoost(sr.color);
                                fadedCount++;
                            }
                        }
                    }
                }
                else
                {
                    var srs = entity.GetComponentsInChildren<SpriteRenderer>(true);
                    foreach (var sr in srs)
                    {
                        if (sr.color.a > AbilityVfxAlpha)
                        {
                            sr.color = FadeAndBoost(sr.color);
                            fadedCount++;
                        }
                    }
                }
            }
            foreach (var te in terrainEffects)
            {
                if (te == null || !te.gameObject.activeInHierarchy) continue;
                var srs = te.GetComponentsInChildren<SpriteRenderer>(true);
                foreach (var sr in srs)
                {
                    if (sr.color.a > AbilityVfxAlpha)
                    {
                        sr.color = FadeAndBoost(sr.color);
                        fadedCount++;
                    }
                }
            }
            if (fadedCount > 0 && !_loggedFirstScan)
            {
                CoopPlugin.FileLog($"VisualClarity: Faded {fadedCount} VFX sprites (alpha={AbilityVfxAlpha}, satBoost={SaturationBoost}).");
                _loggedFirstScan = true;
            }
        }
        private static Color FadeAndBoost(Color c)
        {
            c.a = AbilityVfxAlpha;
            float avg = (c.r + c.g + c.b) / 3f;
            c.r = Mathf.Clamp01(avg + (c.r - avg) * SaturationBoost);
            c.g = Mathf.Clamp01(avg + (c.g - avg) * SaturationBoost);
            c.b = Mathf.Clamp01(avg + (c.b - avg) * SaturationBoost);
            c.r = Mathf.Clamp01(0.5f + (c.r - 0.5f) * ContrastBoost);
            c.g = Mathf.Clamp01(0.5f + (c.g - 0.5f) * ContrastBoost);
            c.b = Mathf.Clamp01(0.5f + (c.b - 0.5f) * ContrastBoost);
            return c;
        }
    }
    [HarmonyPatch(typeof(MatInterface_PixelsSet), "UpdateProperties")]
    public static class VfxShaderDampen_Patch
    {
        private const float MaxFlash = 0.3f;
        private const float OverlayMultiplier = 0.4f;
        private static readonly int Id_Float_Flash = Shader.PropertyToID("_Flash");
        private static readonly int Id_Float_Overlay = Shader.PropertyToID("_Overlay");
        private static readonly Dictionary<int, bool> _isVfxCache = new Dictionary<int, bool>();
        private static readonly Dictionary<int, Renderer> _rendererCache = new Dictionary<int, Renderer>();
        private static readonly MaterialPropertyBlock _sharedBlock = new MaterialPropertyBlock();
        static void Postfix(MatInterface_PixelsSet __instance)
        {
            int id = __instance.GetInstanceID();
            if (!_isVfxCache.TryGetValue(id, out bool isVfx))
            {
                var entity = __instance.GetComponentInParent<Entity>();
                isVfx = (entity != null && entity.Type != EntityType.Player && entity.Type != EntityType.Monster);
                _isVfxCache[id] = isVfx;
            }
            if (!isVfx) return;
            if (!_rendererCache.TryGetValue(id, out Renderer renderer))
            {
                renderer = __instance.GetComponent<Renderer>();
                _rendererCache[id] = renderer;
            }
            if (renderer == null) return;
            renderer.GetPropertyBlock(_sharedBlock);
            float flash = _sharedBlock.GetFloat(Id_Float_Flash);
            if (flash > MaxFlash)
            {
                _sharedBlock.SetFloat(Id_Float_Flash, MaxFlash);
            }
            float overlay = _sharedBlock.GetFloat(Id_Float_Overlay);
            if (overlay > 0f)
            {
                _sharedBlock.SetFloat(Id_Float_Overlay, overlay * OverlayMultiplier);
            }
            renderer.SetPropertyBlock(_sharedBlock);
        }
    }
}