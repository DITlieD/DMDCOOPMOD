using HarmonyLib;
using Death;
using Death.TimesRealm;
using Death.TimesRealm.UserInterface;
using Death.Run.Core;
using Death.Run.UserInterface.Items;
using Death.Run.Systems;
using Death.Run.Behaviours.Objects;
using Death.Run.Behaviours.Instants;
using Death.Run.Behaviours.Abilities;
using Death.Run.Core.Abilities;
using Death.App;
using UnityEngine;
namespace DeathMustDieCoop.Patches
{
    [HarmonyPatch(typeof(Facade_Lobby), "OpenShop")]
    public static class FacadeLobby_OpenShop_Patch
    {
        static void Prefix(Facade_Lobby __instance)
        {
            if (!GamepadInputHandler.LastInteractWasP2) return;
            if (CoopP2Profile.Instance == null) return;
            GamepadInputHandler.EnableGamepadUI();
            try
            {
                SwapShopProfile(__instance, CoopP2Profile.Instance);
                DeathMustDieCoop.Patches.HubScreenUtils.SwapCharSheetPreviewerProfile(__instance, CoopP2Profile.Instance);
                CoopPlugin.FileLog($"ShopPatch: Swapped shop to P2 Profile (Gold={CoopP2Profile.Instance.Gold}).");
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"ShopPatch: OpenShop P2 swap error: {ex.Message}");
            }
        }
        public static void SwapShopProfile(Facade_Lobby lobby, Profile profile)
        {
            var trav = Traverse.Create(lobby);
            var gui = trav.Field("_gui").GetValue<object>();
            if (gui == null) return;
            var tryGetMethod = gui.GetType().GetMethod("TryGetPresenter");
            if (tryGetMethod == null) return;
            var genericMethod = tryGetMethod.MakeGenericMethod(typeof(Screen_Shop));
            var args = new object[] { null };
            bool found = (bool)genericMethod.Invoke(gui, args);
            if (!found || !(args[0] is Screen_Shop shopScreen)) return;
            var shopGui = Traverse.Create(shopScreen).Field("_shopGui").GetValue<object>();
            if (shopGui == null) return;
            var controller = Traverse.Create(shopGui).Field("_controller").GetValue<object>();
            if (controller == null) return;
            DeathMustDieCoop.Patches.HubScreenUtils.SwapControllerProfile(controller, profile);
            var guiShop = Traverse.Create(shopGui).Field("_shop").GetValue<object>();
            if (guiShop != null)
            {
                var stockGrid = Traverse.Create(guiShop).Field("_stock").GetValue<object>();
                if (stockGrid != null)
                {
                    Traverse.Create(stockGrid).Method("Set", new System.Type[] { typeof(Death.Items.ItemGrid) })
                        .GetValue(profile.ShopData.Stock);
                }
            }
            var backpackGrid = Traverse.Create(shopGui).Field("_backpack").GetValue<object>();
            if (backpackGrid != null)
            {
                Traverse.Create(backpackGrid).Method("Set", new System.Type[] { typeof(Death.Items.ItemGrid) })
                    .GetValue(profile.Backpack);
            }
            var equipGui = Traverse.Create(shopGui).Field("_equipment").GetValue<object>();
            if (equipGui != null)
            {
                Traverse.Create(equipGui).Method("Init", new System.Type[] { controller.GetType() })
                    .GetValue(controller);
            }
        }
    }
    [HarmonyPatch(typeof(Screen_Shop), "OnHide")]
    public static class ScreenShop_OnHide_Patch
    {
        static void Postfix(Screen_Shop __instance)
        {
            if (!GamepadInputHandler.P2InMenu) return;
            GamepadInputHandler.DisableGamepadUI();
            try
            {
                CoopP2Profile.SaveToCoopData();
                CoopP2Save.SaveIfDirty();
                var lobby = Object.FindObjectOfType<Facade_Lobby>();
                if (lobby != null)
                {
                    var state = Traverse.Create(lobby).Field("_state").GetValue<ILobbyGameState>();
                    if (state != null)
                    {
                        FacadeLobby_OpenShop_Patch.SwapShopProfile(lobby, state.ActiveProfile);
                        DeathMustDieCoop.Patches.HubScreenUtils.SwapCharSheetPreviewerProfile(lobby, state.ActiveProfile);
                        CoopPlugin.FileLog("ShopPatch: Restored shop to P1 Profile.");
                    }
                }
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"ShopPatch: OnHide restore error: {ex.Message}");
            }
        }
    }
    public static class CoopShopHelper
    {
        private static System.Reflection.FieldInfo _shopGenField;
        public static void RegenP2ShopWithP1Stats()
        {
            var p2 = CoopP2Profile.Instance;
            var p1 = Game.ActiveProfile;
            if (p2 == null || p1 == null) return;
            if (_shopGenField == null)
                _shopGenField = typeof(Profile).GetField("_shopGenerator",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var shopGen = _shopGenField?.GetValue(p2) as Death.Shop.ShopGenerator;
            if (shopGen == null)
            {
                CoopPlugin.FileLog("CoopShopHelper: _shopGenerator null, falling back to P2.ReGenerateShop()");
                p2.ReGenerateShop();
                return;
            }
            var p1Tier = p1.Progression.GetItemTierReached();
            float p1Playtime = p1.Progression.TimeSpentInRunMin;
            int p1NetWorth = p1.CalculateNetWorth();
            p2.ShopData.Stock.Clear();
            p2.ShopData.BuyBack.Clear();
            var items = new System.Collections.Generic.List<Death.Items.Item>();
            using (var context = p2.GenerateItemContext())
            {
                shopGen.ReGenerateStock(items, context,
                    shopGen.GenerateRarityCaps(p1Tier), p1Tier, p1NetWorth, p1Playtime);
                foreach (var item in items)
                    p2.ShopData.Stock.TryAdd(item);
            }
            CoopPlugin.FileLog($"CoopShopHelper: Regenerated P2 shop with P1 stats (tier={p1Tier}, playtime={p1Playtime:F0}min, netWorth={p1NetWorth}, items={items.Count}).");
        }
    }
    [HarmonyPatch(typeof(System_PlayerManager), "OnGoldCollected")]
    public static class PlayerManager_OnGoldCollected_Patch
    {
        static void Postfix(Gold gold)
        {
            if (CoopP2Profile.Instance != null && PlayerRegistry.Count >= 2)
                CoopP2Profile.Instance.Gold += gold.Amount;
        }
    }
    [HarmonyPatch(typeof(Instant_ModifyGold), "PerformImplAsync")]
    public static class InstantModifyGold_Patch
    {
        static void Postfix(Instant_ModifyGold __instance)
        {
            if (CoopP2Profile.Instance != null && PlayerRegistry.Count >= 2)
                CoopP2Profile.Instance.Gold += __instance.Amount;
        }
    }
    [HarmonyPatch(typeof(Effect_GainGold), "Trigger")]
    public static class EffectGainGold_Patch
    {
        static void Postfix(IAbility ability)
        {
            if (CoopP2Profile.Instance != null && PlayerRegistry.Count >= 2)
            {
                int amount = ability.Stats.GetFloor(StatId.EffectValue);
                CoopP2Profile.Instance.Gold += amount;
            }
        }
    }
    [HarmonyPatch(typeof(GameState_Run), "EndRun")]
    public static class GameStateRun_EndRun_Patch
    {
        static void Postfix()
        {
            if (CoopP2Profile.Instance == null) return;
            try
            {
                var p1 = Game.ActiveProfile;
                if (p1 == null) return;
                if (p1.Progression.UnlockedShop)
                {
                    bool p1ShopRefreshed = p1.Progression.LastShopRefreshSec == p1.Progression.TimeSpentInRunSec
                                           && p1.Progression.TimeSpentInRunSec > 0;
                    if (p1ShopRefreshed)
                    {
                        CoopShopHelper.RegenP2ShopWithP1Stats();
                        CoopPlugin.FileLog("ShopPatch: Regenerated P2 shop after run end.");
                    }
                    else if (CoopP2Profile.Instance.ShopData.CheckIsEmpty())
                    {
                        CoopShopHelper.RegenP2ShopWithP1Stats();
                        CoopPlugin.FileLog("ShopPatch: Regenerated P2 shop (was empty) after run end.");
                    }
                }
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"ShopPatch: EndRun P2 error: {ex.Message}");
            }
        }
    }
    [HarmonyPatch(typeof(Screen_Items), "OnUIBackInput")]
    public static class ScreenItems_OnUIBackInput_Patch
    {
        static bool Prefix(Screen_Items __instance)
        {
            if (GamepadInputHandler.P2InMenu)
            {
                Traverse.Create(__instance).Method("PopScreen").GetValue();
                return false; 
            }
            return true;
        }
    }
}