using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Death.Run.UserInterface.HUD.Minimap;
using Death.Run.Behaviours.Players;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
namespace DeathMustDieCoop.Patches
{
    [HarmonyPatch(typeof(GUI_Minimap), "Update")]
    public static class Minimap_Update_Patch
    {
        private static readonly Color P1Color = new Color(0.2f, 0.9f, 0.2f, 1f);
        private static readonly Color P2Color = new Color(0.3f, 0.5f, 1f, 1f);
        private static RectTransform _p2MarkerRect;
        private static bool _p1Colored;
        private static GUI_Minimap _lastInstance;
        private static FieldInfo _playerMarkerField;
        private static FieldInfo _configField;
        private static FieldInfo _boundsImageField;
        private static FieldInfo _markersField;
        private const float ZoomPadding = 2.5f;
        static bool Prefix(GUI_Minimap __instance)
        {
            if (_playerMarkerField == null)
            {
                var t = typeof(GUI_Minimap);
                _playerMarkerField = t.GetField("_playerMarker", BindingFlags.NonPublic | BindingFlags.Instance);
                _configField = t.GetField("_config", BindingFlags.NonPublic | BindingFlags.Instance);
                _boundsImageField = t.GetField("_boundsImage", BindingFlags.NonPublic | BindingFlags.Instance);
                _markersField = t.GetField("_markers", BindingFlags.NonPublic | BindingFlags.Instance);
            }
            var playerMarker = (RectTransform)_playerMarkerField.GetValue(__instance);
            var config = (GUI_Minimap.Config)_configField.GetValue(__instance);
            var boundsImage = (Image)_boundsImageField.GetValue(__instance);
            var markers = (List<Marker>)_markersField.GetValue(__instance);
            if (playerMarker == null || config == null || boundsImage == null || markers == null)
                return true; 
            if (_lastInstance != __instance)
            {
                _lastInstance = __instance;
                _p2MarkerRect = null;
                _p1Colored = false;
            }
            if (_p2MarkerRect == null && PlayerRegistry.Count > 1)
            {
                var p2Obj = Object.Instantiate(playerMarker.gameObject, playerMarker.parent);
                p2Obj.name = "PlayerMarker_P2";
                _p2MarkerRect = p2Obj.GetComponent<RectTransform>();
                var img = p2Obj.GetComponent<Image>();
                if (img == null) img = p2Obj.GetComponentInChildren<Image>();
                if (img != null) img.color = P2Color;
                CoopPlugin.FileLog("MinimapPatch: P2 marker created (blue).");
            }
            if (!_p1Colored)
            {
                var img = playerMarker.GetComponent<Image>();
                if (img == null) img = playerMarker.GetComponentInChildren<Image>();
                if (img != null) img.color = P1Color;
                _p1Colored = true;
                CoopPlugin.FileLog("MinimapPatch: P1 marker colored green.");
            }
            var livingPlayers = PlayerRegistry.Players.Where(p => p != null && p.Entity != null && p.Entity.IsAlive).ToList();
            Vector2 center;
            float effectiveDimension;
            if (livingPlayers.Count == 0)
            {
                playerMarker.gameObject.SetActive(false);
                if (_p2MarkerRect != null) _p2MarkerRect.gameObject.SetActive(false);
                return false; 
            }
            else if (livingPlayers.Count == 1)
            {
                var survivor = livingPlayers[0];
                center = survivor.transform.position;
                effectiveDimension = config.MapDimensionUnits;
                bool p1Survived = survivor == PlayerRegistry.GetPlayer(0);
                playerMarker.gameObject.SetActive(p1Survived);
                if (_p2MarkerRect != null) _p2MarkerRect.gameObject.SetActive(!p1Survived);
                if (p1Survived) playerMarker.anchoredPosition = Vector2.zero;
                else if (_p2MarkerRect != null) _p2MarkerRect.anchoredPosition = Vector2.zero;
            }
            else 
            {
                var p1 = livingPlayers[0];
                var p2 = livingPlayers[1];
                playerMarker.gameObject.SetActive(true);
                if (_p2MarkerRect != null) _p2MarkerRect.gameObject.SetActive(true);
                Vector2 p1Pos = p1.transform.position;
                Vector2 p2Pos = p2.transform.position;
                center = (p1Pos + p2Pos) / 2f;
                float playerDist = Vector2.Distance(p1Pos, p2Pos);
                float requiredDimension = playerDist * ZoomPadding;
                effectiveDimension = Mathf.Max(config.MapDimensionUnits, requiredDimension);
                float scaleForOffsets = boundsImage.rectTransform.rect.size.x / effectiveDimension;
                playerMarker.anchoredPosition = ((Vector2)p1.transform.position - center) * scaleForOffsets;
                if(_p2MarkerRect != null) _p2MarkerRect.anchoredPosition = ((Vector2)p2.transform.position - center) * scaleForOffsets;
            }
            float boundsWidth = boundsImage.rectTransform.rect.size.x;
            float scale = boundsWidth / effectiveDimension;
            float cullDist = effectiveDimension / 2f * scale + config.MapBorderSizePixels;
            float cullDistSq = cullDist * cullDist;
            float constrainDist = effectiveDimension / 2f * scale - config.MapBorderSizePixels;
            float constrainDistSq = constrainDist * constrainDist;
            foreach (var marker in markers)
            {
                if (marker.Target == null)
                {
                    marker.Cull();
                    continue;
                }
                Vector2 markerOffset = ((Vector2)marker.Target.position - center) * scale;
                float sqrMag = markerOffset.sqrMagnitude;
                if (sqrMag < cullDistSq || marker.Constrained)
                {
                    if (marker.Constrained && sqrMag > constrainDistSq)
                    {
                        marker.SetPosition(markerOffset.normalized * constrainDist);
                    }
                    else
                    {
                        marker.SetPosition(markerOffset);
                    }
                }
                else
                {
                    marker.Cull();
                }
            }
            return false; 
        }
        public static void Reset()
        {
            _p2MarkerRect = null;
            _p1Colored = false;
            _lastInstance = null;
        }
    }
}