using HarmonyLib;
using Death.Run.Behaviours.Players;
using Death.Run.UserInterface;
using Death.Run.UserInterface.HUD;
using Death.Run.UserInterface.PlayerOverlay;
using UnityEngine;
using UnityEngine.UI;
namespace DeathMustDieCoop.Patches
{
    public static class CoopHud
    {
        private const float DashChargeExtraYOffset = 12f;
        private const float OverheadBarScale = 2.0f;
        public static void SetupForP2(Behaviour_Player p2)
        {
            var p1 = PlayerRegistry.GetPlayer(0);
            var p1Bars = Object.FindObjectOfType<GUI_PlayerOverheadBars>();
            if (p1Bars == null)
            {
                CoopPlugin.FileLog("HudPatch: GUI_PlayerOverheadBars not found — cannot setup HUD.");
                return;
            }
            var clone = Object.Instantiate(p1Bars.gameObject, p1Bars.transform.parent);
            clone.name = "GUI_PlayerOverheadBars_P2";
            var p2Bars = clone.GetComponent<GUI_PlayerOverheadBars>();
            Traverse.Create(p2Bars).Field("_initialized").SetValue(false);
            p2Bars.Init(p2);
            CoopPlugin.FileLog("HudPatch: P2 overhead bars created.");
            ScaleOverheadBars(p1Bars);
            ScaleOverheadBars(p2Bars);
            CreateXpBarForPlayer(p1, p1Bars);
            CreateXpBarForPlayer(p2, p2Bars);
            ShiftDashChargesUp();
            HideBottomHud();
        }
        private static void ScaleOverheadBars(GUI_PlayerOverheadBars bars)
        {
            var root = Traverse.Create(bars).Field("_root").GetValue<RectTransform>();
            if (root != null)
            {
                root.localScale = Vector3.one * OverheadBarScale;
                CoopPlugin.FileLog($"HudPatch: Scaled overhead bars '{bars.name}' to {OverheadBarScale}x.");
            }
        }
        private static void CreateXpBarForPlayer(Behaviour_Player player, GUI_PlayerOverheadBars overheadBars)
        {
            if (player == null)
            {
                CoopPlugin.FileLog("HudPatch: CreateXpBarForPlayer — player is null.");
                return;
            }
            if (overheadBars == null)
            {
                CoopPlugin.FileLog($"HudPatch: CreateXpBarForPlayer — overheadBars is null for {player.name}.");
                return;
            }
            var trav = Traverse.Create(overheadBars);
            var healthBarFab = trav.Field("_healthBarFab").GetValue<GUI_HealthBar>();
            var root = trav.Field("_root").GetValue<RectTransform>();
            if (healthBarFab == null || root == null)
            {
                CoopPlugin.FileLog($"HudPatch: CreateXpBarForPlayer — healthBarFab={healthBarFab != null}, root={root != null} for {player.name}.");
                return;
            }
            var xpBarObj = Object.Instantiate(healthBarFab.gameObject, root);
            xpBarObj.name = $"CoopXpBar_{player.name}";
            var origHealthBar = xpBarObj.GetComponent<GUI_HealthBar>();
            var fillHp = Traverse.Create(origHealthBar).Field("_fillHp").GetValue<Image>();
            var fillPosture = Traverse.Create(origHealthBar).Field("_fillPosture").GetValue<Image>();
            var fillPrev = Traverse.Create(origHealthBar).Field("_fillPrev").GetValue<Image>();
            var fillDoom = Traverse.Create(origHealthBar).Field("_fillDoom").GetValue<Image>();
            if (fillPosture != null) fillPosture.gameObject.SetActive(false);
            if (fillPrev != null) fillPrev.gameObject.SetActive(false);
            if (fillDoom != null) fillDoom.gameObject.SetActive(false);
            Object.Destroy(origHealthBar);
            var xpBar = xpBarObj.AddComponent<CoopXpBar>();
            xpBar.Init(player, fillHp);
            xpBarObj.SetActive(true);
            CoopPlugin.FileLog($"HudPatch: XP bar created for {player.name}.");
        }
        private static void ShiftDashChargesUp()
        {
            var allOverhead = Object.FindObjectsOfType<GUI_PlayerOverheadBars>();
            foreach (var overhead in allOverhead)
            {
                var chargeBar = Traverse.Create(overhead).Field("_chargeBar").GetValue<GUI_ChargeBar>();
                if (chargeBar != null)
                {
                    chargeBar.YOffsetPx += DashChargeExtraYOffset;
                    CoopPlugin.FileLog($"HudPatch: Shifted dash charges up by {DashChargeExtraYOffset}px.");
                }
            }
        }
        public static void HideBottomHud()
        {
            DestroyAll<GUI_CharacterPortrait>();
            DestroyAll<GUI_DashCharges>();
            DestroyAll<GUI_PlayerHealth>();
            DestroyAll<GUI_Revivals>();
            DestroyAll<GUI_XpBar>();
            var screenHud = Object.FindObjectOfType<Screen_HUD>();
            if (screenHud != null)
            {
                var trav = Traverse.Create(screenHud);
                var invButton = trav.Field("_inventoryButton").GetValue<MonoBehaviour>();
                var boonsButton = trav.Field("_boonsButton").GetValue<MonoBehaviour>();
                if (invButton != null)
                {
                    var parent = invButton.transform.parent;
                    Object.Destroy(parent != null ? parent.gameObject : invButton.gameObject);
                    CoopPlugin.FileLog($"HudPatch: Destroyed inventory button parent ({(parent != null ? parent.name : invButton.name)}).");
                }
                if (boonsButton != null)
                {
                    var parent = boonsButton.transform.parent;
                    Object.Destroy(parent != null ? parent.gameObject : boonsButton.gameObject);
                    CoopPlugin.FileLog($"HudPatch: Destroyed boons button parent ({(parent != null ? parent.name : boonsButton.name)}).");
                }
                var levelIndicator = screenHud.transform.Find("GUI_LevelIndicator");
                if (levelIndicator != null)
                {
                    Object.Destroy(levelIndicator.gameObject);
                    CoopPlugin.FileLog("HudPatch: Destroyed GUI_LevelIndicator.");
                }
                LogChildren(screenHud.transform, 0, 3);
            }
            else
            {
                CoopPlugin.FileLog("HudPatch: Screen_HUD not found!");
            }
            CoopPlugin.FileLog("HudPatch: Bottom HUD cleanup done.");
        }
        private static void LogChildren(Transform parent, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;
            string indent = new string(' ', depth * 2);
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                var components = child.GetComponents<Component>();
                string compNames = "";
                foreach (var c in components)
                {
                    if (c != null && !(c is Transform))
                        compNames += c.GetType().Name + ",";
                }
                CoopPlugin.FileLog($"HudPatch DIAG: {indent}[{child.name}] active={child.gameObject.activeSelf} comps={compNames}");
                LogChildren(child, depth + 1, maxDepth);
            }
        }
        private static void DestroyAll<T>() where T : MonoBehaviour
        {
            var all = Resources.FindObjectsOfTypeAll<T>();
            int count = 0;
            foreach (var obj in all)
            {
                if (obj.gameObject.scene.name == null) continue;
                Object.Destroy(obj.gameObject);
                count++;
            }
            CoopPlugin.FileLog($"HudPatch: Destroyed {count} {typeof(T).Name} instances.");
        }
    }
    [HarmonyPatch(typeof(Screen_HUD), "PostShow")]
    public static class ScreenHUD_PostShow_Patch
    {
        static void Postfix()
        {
            if (PlayerRegistry.Count < 2) return;
            CoopHud.HideBottomHud();
        }
    }
}