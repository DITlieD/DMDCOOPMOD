using HarmonyLib;
using Death.TimesRealm;
using Death.Run.Behaviours.Players;
using Death.Run.Behaviours.Entities;
using Death.Run.Core;
using Death.Run.Core.Entities;
using Death.Data;
using Death.ResourceManagement;
using Death.Run.Behaviours;
using UnityEngine;
using Death.TimesRealm.Entrances;
namespace DeathMustDieCoop.Patches
{
    public static class CoopLobbyState
    {
        public static Behaviour_Player P2Player;
        public static Entity P2Entity;
        public static void Clear()
        {
            P2Player = null;
            P2Entity = null;
        }
    }
    [HarmonyPatch(typeof(Facade_Lobby), "Init")]
    public static class FacadeLobby_Init_Patch
    {
        static void Postfix(Facade_Lobby __instance)
        {
            CoopPlugin.FileLog("LobbyPatch: Facade_Lobby.Init postfix fired.");
            CoopRuntime.EnsureExists();
            try
            {
                var config = Traverse.Create(__instance).Field("_config").GetValue<object>();
                if (config != null)
                {
                    var dummyFab = Traverse.Create(config).Field("DummyPlayerFab")
                        .GetValue<Behaviour_Player>();
                    if (dummyFab != null)
                    {
                        HubScreenUtils.CachedDummyPlayerFab = dummyFab;
                        CoopPlugin.FileLog("LobbyPatch: Cached DummyPlayerFab for run preview.");
                    }
                }
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"LobbyPatch: DummyPlayerFab cache failed (non-fatal): {ex.Message}");
            }
            try
            {
                var trav = Traverse.Create(__instance);
                var p1 = trav.Field("_player").GetValue<Behaviour_Player>();
                if (p1 == null)
                {
                    CoopPlugin.FileLog("LobbyPatch: P1 is null, cannot spawn P2.");
                    return;
                }
                if (CoopLobbyState.P2Entity != null && CoopLobbyState.P2Entity.gameObject == null)
                {
                    CoopPlugin.FileLog("LobbyPatch: Clearing stale P2 references from previous room.");
                    CoopLobbyState.Clear();
                }
                if (CoopLobbyState.P2Player != null && CoopLobbyState.P2Player.gameObject != null)
                {
                    CoopPlugin.FileLog("LobbyPatch: P2 already exists, skipping spawn.");
                    return;
                }
                string p2CharCode = CoopP2Save.Data.SelectedCharacterCode;
                if (string.IsNullOrEmpty(p2CharCode))
                {
                    p2CharCode = "Knight";
                    CoopP2Save.Data.SelectedCharacterCode = p2CharCode;
                    CoopP2Save.MarkDirty();
                    CoopPlugin.FileLog($"LobbyPatch: No P2 character saved, defaulting to {p2CharCode}");
                }
                CharacterData charData;
                try
                {
                    charData = Database.Characters.Get(p2CharCode);
                }
                catch
                {
                    p2CharCode = "Knight";
                    charData = Database.Characters.Get(p2CharCode);
                    CoopP2Save.Data.SelectedCharacterCode = p2CharCode;
                    CoopP2Save.MarkDirty();
                    CoopPlugin.FileLog($"LobbyPatch: P2 character invalid, reset to {p2CharCode}");
                }
                var p1Profile = trav.Field("_state").GetValue<ILobbyGameState>().ActiveProfile;
                CoopP2Profile.Create(p1Profile);
                if (CoopP2Profile.Instance != null && CoopP2Profile.Instance.ShopData.CheckIsEmpty())
                {
                    try
                    {
                        DeathMustDieCoop.Patches.CoopShopHelper.RegenP2ShopWithP1Stats();
                        CoopPlugin.FileLog("LobbyPatch: Regenerated P2 shop (was empty on lobby re-entry).");
                    }
                    catch (System.Exception shopEx)
                    {
                        CoopPlugin.FileLog($"LobbyPatch: P2 shop regen failed: {shopEx.Message}");
                    }
                }
                Vector2 spawnPos = (Vector2)p1.transform.position;
                try
                {
                    var entrance = Traverse.Create(__instance).Method("GetEntrance").GetValue<Entrance>();
                    if (entrance != null)
                    {
                        spawnPos = entrance.Position;
                        CoopPlugin.FileLog($"LobbyPatch: Using entrance position {spawnPos}");
                    }
                }
                catch (System.Exception ex)
                {
                    CoopPlugin.FileLog($"LobbyPatch: GetEntrance failed, using P1 pos: {ex.Message}");
                }
                SpawnP2InLobby(__instance, charData, spawnPos);
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"LobbyPatch: FAILED: {ex}");
            }
        }
        public static void SpawnP2InLobby(Facade_Lobby lobby, CharacterData charData, Vector2 spawnPos)
        {
            var trav = Traverse.Create(lobby);
            string p2CharCode = charData.Code.ToString();
            CoopPlugin.FileLog($"LobbyPatch: Spawning P2 as {p2CharCode} at {spawnPos}...");
            Entity entityPrefab = ResourceManager.Load<Entity>(charData.EntityPath);
            PlayerRegistry.SpawningP2 = true;
            Entity p2Entity = Object.Instantiate(entityPrefab);
            p2Entity.SetDestroyOnReclaim();
            var p2Behaviour = p2Entity.GetComponent<Behaviour_Player>();
            if (p2Behaviour != null)
                Traverse.Create(p2Behaviour).Field("_isPrimaryPlayerInstance").SetValue(false);
            var p1Player = trav.Field("_player").GetValue<Behaviour_Player>();
            if (p1Player != null)
            {
                Player.Init(p1Player);
                CoopPlugin.FileLog("LobbyPatch: Restored Player.Instance to P1.");
            }
            StatHierarchy statHierarchy = Teams.Get(TeamId.Player).StatHierarchy;
            var damageSource = new DamageSource("Coop-P2-Lobby", TeamId.Player);
            p2Entity.Init(damageSource, charData.BaseStats, statHierarchy.GetPlayerGroup(), TeamId.Player);
            p2Entity.transform.position = spawnPos;
            PlayerRegistry.SpawningP2 = false;
            if (p2Behaviour == null)
            {
                CoopPlugin.FileLog("LobbyPatch: P2 entity missing Behaviour_Player!");
                return;
            }
            var p2Profile = CoopP2Profile.Instance;
            if (p2Profile != null)
            {
                p2Profile.Progression.SelectedCharacterCode = charData.Code.ToString();
                p2Behaviour.Prepare(charData, p2Profile.GetActiveEquipment(), p2Profile.Backpack);
                CoopPlugin.FileLog("LobbyPatch: P2 using own equipment from CoopP2Profile.");
            }
            else
            {
                var profile = trav.Field("_state").GetValue<ILobbyGameState>().ActiveProfile;
                p2Behaviour.Prepare(charData, profile.GetActiveEquipment(), profile.Backpack);
                CoopPlugin.FileLog("LobbyPatch: WARNING — CoopP2Profile null, using P1 equipment.");
            }
            var colliderGo = new GameObject("Collider");
            colliderGo.AddComponent<CircleCollider2D>().radius = 0.12f;
            colliderGo.transform.SetParent(p2Entity.transform);
            colliderGo.transform.localPosition = Vector2.zero;
            var config = Traverse.Create(lobby).Field("_config").GetValue<object>();
            if (config != null)
            {
                var dashOpt = Traverse.Create(config).Field("Dash").GetValue<object>();
                if (dashOpt != null)
                {
                    var tryGetMethod = dashOpt.GetType().GetMethod("TryGet");
                    if (tryGetMethod != null)
                    {
                        var args = new object[] { null };
                        bool hasDash = (bool)tryGetMethod.Invoke(dashOpt, args);
                        if (hasDash && args[0] is string dashCode)
                        {
                            var dashData = Database.Weapon.Get(dashCode);
                            var dashAbility = dashData.CreateAbility(p2Behaviour.Unit,
                                p2Behaviour.Unit.Team.StatHierarchy.GetPlayerDash());
                            p2Behaviour.Unit.AbilityManager.Add(dashAbility);
                            CoopPlugin.FileLog($"LobbyPatch: P2 dash ability added: {dashCode}");
                        }
                    }
                }
            }
            var lightFab = trav.Field("_playerLightFab").GetValue<PlayerLight>();
            if (lightFab != null)
            {
                PlayerLight p2Light = Object.Instantiate(lightFab);
                p2Light.Init(p2Behaviour.transform);
                LightPatch_ScaleUp.ScaleLight(p2Light);
                bool disableOverlay = trav.Field("_disablePlayerOverlay").GetValue<bool>();
                p2Light.SetOverlayEnabled(!disableOverlay);
                CoopPlugin.FileLog("LobbyPatch: P2 light added.");
            }
            LightPatch_ScaleUp.ScaleAllLights();
            p2Entity.gameObject.AddComponent<GamepadInputHandler>();
            CoopLobbyState.P2Player = p2Behaviour;
            CoopLobbyState.P2Entity = p2Entity;
            CoopPlugin.FileLog($"LobbyPatch: P2 spawned as {p2CharCode} at {spawnPos}");
            CoopCameraController.SetupForRun();
            CoopPlugin.FileLog("LobbyPatch: Camera midpoint tracking active.");
        }
    }
    [HarmonyPatch(typeof(Facade_Lobby), "OpenCharSelect")]
    public static class FacadeLobby_OpenCharSelect_Patch
    {
        static void Prefix()
        {
            if (GamepadInputHandler.LastInteractWasP2)
            {
                GamepadInputHandler.EnableGamepadUI();
            }
            CoopPlugin.FileLog($"LobbyPatch: OpenCharSelect — P2={GamepadInputHandler.LastInteractWasP2}");
        }
    }
    [HarmonyPatch(typeof(Facade_Lobby), "SelectCharacter")]
    public static class FacadeLobby_SelectCharacter_Patch
    {
        static bool Prefix(Facade_Lobby __instance, string charCode)
        {
            if (!GamepadInputHandler.P2InMenu)
                return true; 
            try
            {
                CoopPlugin.FileLog($"LobbyPatch: P2 selected character: {charCode}");
                GamepadInputHandler.DisableGamepadUI();
                bool sameChar = CoopP2Save.Data.SelectedCharacterCode == charCode;
                if (!sameChar)
                {
                    CoopP2Save.Data.SelectedCharacterCode = charCode;
                    CoopP2Save.MarkDirty();
                    CoopP2Save.Save();
                    if (CoopLobbyState.P2Entity != null)
                    {
                        Vector2 prevPos = CoopLobbyState.P2Entity.transform.position;
                        var oldPlayer = CoopLobbyState.P2Player;
                        if (oldPlayer != null)
                            PlayerRegistry.Unregister(oldPlayer);
                        Object.Destroy(CoopLobbyState.P2Entity.gameObject);
                        CoopLobbyState.Clear();
                        var charData = Database.Characters.Get(charCode);
                        FacadeLobby_Init_Patch.SpawnP2InLobby(__instance, charData, prevPos);
                    }
                }
                else
                {
                    CoopPlugin.FileLog("LobbyPatch: P2 selected same character, just closing screen.");
                }
                CloseCharacterSelectScreen(__instance);
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"LobbyPatch: SelectCharacter P2 error: {ex}");
            }
            return false; 
        }
        private static void CloseCharacterSelectScreen(Facade_Lobby lobby)
        {
            try
            {
                var screenMgr = Traverse.Create(lobby).Field("_screenManager").GetValue<object>();
                if (screenMgr == null) return;
                var smType = screenMgr.GetType();
                foreach (var m in smType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    if (m.Name == "Enter" && m.IsGenericMethod && m.GetParameters().Length == 0)
                    {
                        var screenDefaultType = typeof(Facade_Lobby).Assembly
                            .GetType("Death.TimesRealm.UserInterface.Screen_Default");
                        if (screenDefaultType != null)
                        {
                            m.MakeGenericMethod(screenDefaultType).Invoke(screenMgr, null);
                            CoopPlugin.FileLog("LobbyPatch: Closed character select screen.");
                        }
                        break;
                    }
                }
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"LobbyPatch: Screen close failed (non-fatal): {ex.Message}");
            }
        }
    }
    [HarmonyPatch(typeof(Death.TimesRealm.UserInterface.CharacterSelect.Screen_CharacterSelect), "OnHide")]
    public static class ScreenCharSelect_OnHide_Patch
    {
        static void Postfix()
        {
            if (GamepadInputHandler.P2InMenu)
            {
                GamepadInputHandler.DisableGamepadUI();
                CoopPlugin.FileLog("LobbyPatch: Char select screen closed, P2InMenu cleared.");
            }
        }
    }
    [HarmonyPatch(typeof(Facade_Lobby), "Cleanup")]
    public static class FacadeLobby_Cleanup_Patch
    {
        static void Postfix()
        {
            CoopPlugin.FileLog("LobbyPatch: Facade_Lobby.Cleanup — saving P2 data.");
            if (CoopLobbyState.P2Entity != null)
            {
                try
                {
                    if (CoopLobbyState.P2Player != null)
                        PlayerRegistry.Unregister(CoopLobbyState.P2Player);
                    CoopLobbyState.P2Entity.Reclaim();
                    CoopPlugin.FileLog("LobbyPatch: P2 entity reclaimed.");
                }
                catch (System.Exception ex)
                {
                    CoopPlugin.FileLog($"LobbyPatch: P2 reclaim failed (non-fatal): {ex.Message}");
                }
            }
            CoopP2Profile.Cleanup();
            CoopP2Save.SaveIfDirty();
            CoopLobbyState.Clear();
            GamepadInputHandler.LastInteractWasP2 = false;
            GamepadInputHandler.P2InMenu = false;
        }
    }
}