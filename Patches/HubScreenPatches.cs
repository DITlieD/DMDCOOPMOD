using HarmonyLib;
using Death.TimesRealm;
using Death.TimesRealm.UserInterface;
using Death.TimesRealm.UserInterface.Talents;
using Death.TimesRealm.UserInterface.Darkness;
using Death.Run.UserInterface.Items;
using Death.Run.UserInterface.Boons;
using Death.Run.UserInterface.Encounters;
using Death.Run.Core;
using Death.App;
using Death.Items;
using Death.Data;
using Death;
using UnityEngine;
using UnityEngine.InputSystem;
using Death.Utils;
using Death.Run.Core.Abilities;
using Death.Run.Systems;
using Death.Run.Behaviours.Players;
using Death.Run.Behaviours.Abilities;
using System.Linq;
using System.Collections.Generic;
namespace DeathMustDieCoop.Patches
{
    [HarmonyPatch(typeof(Facade_Lobby), "OpenArmory")]
    public static class FacadeLobby_OpenArmory_Patch
    {
        static void Prefix(Facade_Lobby __instance)
        {
            if (!GamepadInputHandler.LastInteractWasP2) return;
            if (CoopP2Profile.Instance == null) return;
            GamepadInputHandler.EnableGamepadUI();
            try
            {
                SwapArmoryToP2(__instance);
                CoopPlugin.FileLog($"HubScreenPatches: Armory swapped to P2 Profile.");
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"HubScreenPatches: OpenArmory P2 swap error: {ex.Message}");
            }
        }
        public static void SwapArmoryToP2(Facade_Lobby lobby)
        {
            var p2Profile = CoopP2Profile.Instance;
            if (p2Profile == null) return;
            var trav = Traverse.Create(lobby);
            var gui = trav.Field("_gui").GetValue<object>();
            if (gui == null) return;
            var tryGetMethod = gui.GetType().GetMethod("TryGetPresenter");
            if (tryGetMethod == null) return;
            var genericMethod = tryGetMethod.MakeGenericMethod(typeof(Screen_Armory));
            var args = new object[] { null };
            bool found = (bool)genericMethod.Invoke(gui, args);
            if (!found || !(args[0] is Screen_Armory armoryScreen)) return;
            var armoryGui = Traverse.Create(armoryScreen).Field("_armoryGui").GetValue<object>();
            if (armoryGui == null) return;
            var controller = Traverse.Create(armoryGui).Field("_controller").GetValue<object>();
            if (controller == null) return;
            HubScreenUtils.SwapControllerProfile(controller, p2Profile);
            var p1Profile = Traverse.Create(lobby).Field("_state").GetValue<ILobbyGameState>()?.ActiveProfile;
            IEnumerable<Item> combinedItems = p2Profile.GetAllPlayerItems().Where(item => item.Type != ItemType.Lore);
            if (p1Profile != null)
            {
                var stashItems = p1Profile.Stashes.GetAllSlots()
                    .Where(slot => slot.Item != null && slot.Item.Type != ItemType.Lore)
                    .Select(slot => slot.Item);
                combinedItems = combinedItems.Concat(stashItems);
            }
            Traverse.Create(controller).Field("_allItems").SetValue(combinedItems);
            Traverse.Create(controller).Method("Populate").GetValue();
            var backpackGrid = Traverse.Create(armoryGui).Field("_backpack").GetValue<object>();
            if (backpackGrid != null)
            {
                Traverse.Create(backpackGrid).Method("Set", new System.Type[] { typeof(ItemGrid) })
                    .GetValue(p2Profile.Backpack);
            }
            HubScreenUtils.SwapCharSheetPreviewerProfile(lobby, p2Profile);
        }
        public static void SwapArmoryToP1(Facade_Lobby lobby)
        {
            var trav = Traverse.Create(lobby);
            var state = trav.Field("_state").GetValue<ILobbyGameState>();
            if (state == null) return;
            var p1Profile = state.ActiveProfile;
            var gui = trav.Field("_gui").GetValue<object>();
            if (gui == null) return;
            var tryGetMethod = gui.GetType().GetMethod("TryGetPresenter");
            if (tryGetMethod == null) return;
            var genericMethod = tryGetMethod.MakeGenericMethod(typeof(Screen_Armory));
            var args = new object[] { null };
            bool found = (bool)genericMethod.Invoke(gui, args);
            if (!found || !(args[0] is Screen_Armory armoryScreen)) return;
            var armoryGui = Traverse.Create(armoryScreen).Field("_armoryGui").GetValue<object>();
            if (armoryGui == null) return;
            var controller = Traverse.Create(armoryGui).Field("_controller").GetValue<object>();
            if (controller == null) return;
            HubScreenUtils.SwapControllerProfile(controller, p1Profile);
            var p1Items = p1Profile.GetAllPlayerItems().Where(item => item.Type != ItemType.Lore);
            Traverse.Create(controller).Field("_allItems").SetValue(p1Items);
            Traverse.Create(controller).Method("Populate").GetValue();
            var backpackGrid = Traverse.Create(armoryGui).Field("_backpack").GetValue<object>();
            if (backpackGrid != null)
            {
                Traverse.Create(backpackGrid).Method("Set", new System.Type[] { typeof(ItemGrid) })
                    .GetValue(p1Profile.Backpack);
            }
            HubScreenUtils.SwapCharSheetPreviewerProfile(lobby, p1Profile);
        }
    }
    public static class HubScreenUtils
    {
        public static Behaviour_Player CachedDummyPlayerFab;
        public static Death.Run.Core.CharacterInfo BuildCharacterInfoViaPreview(Profile profile)
        {
            if (CachedDummyPlayerFab == null)
            {
                CoopPlugin.FileLog("HubScreenUtils: CachedDummyPlayerFab is null, cannot build preview CharacterInfo.");
                return null;
            }
            Behaviour_Player tempPlayer = null;
            try
            {
                var charCode = profile.SelectedCharacter.Code;
                var charData = Database.Characters.Get(charCode);
                var tempTeam = new Team(TeamId.CharacterPreview, Database.GlobalStats);
                tempPlayer = Object.Instantiate(CachedDummyPlayerFab, UnityEngine.Vector2.zero, UnityEngine.Quaternion.identity);
                var entity = tempPlayer.GetComponent<Entity>();
                entity.SetDestroyOnReclaim();
                entity.Init(
                    new DamageSource("CoopPreview", TeamId.Systems),
                    charData.BaseStats,
                    tempTeam.StatHierarchy.GetPlayerGroup(),
                    tempTeam);
                var loadout = profile.GetLoadoutsFor(charCode).GetSelectedLoadout();
                int loadoutItems = 0;
                foreach (var slot in loadout)
                {
                    if (slot.IsFull) loadoutItems++;
                }
                CoopPlugin.FileLog($"HubScreenUtils: Preview — charCode={charCode}, loadoutItems={loadoutItems}, teamId={tempTeam.Id}");
                tempPlayer.Prepare(charData, loadout, profile.Backpack);
                tempPlayer.Init(profile.TalentsState.GetForCharacter(charCode), null);
                tempPlayer.AllowItemPickUp = false;
                try
                {
                    var eqTracker = tempPlayer.EquipmentTracker;
                    int trackerItems = 0;
                    if (eqTracker != null)
                    {
                        var itemInstances = eqTracker.ItemInstances;
                        if (itemInstances != null) trackerItems = itemInstances.Count;
                    }
                    CoopPlugin.FileLog($"HubScreenUtils: Preview — EquipmentTracker.ItemInstances={trackerItems}, entityTeam={tempPlayer.Entity.Team.Id}");
                }
                catch (System.Exception diagEx)
                {
                    CoopPlugin.FileLog($"HubScreenUtils: Preview diag error: {diagEx.Message}");
                }
                var dashAbility = Database.Weapon.Get(charData.StartingDashCode)
                    .CreateAbility(tempPlayer.Unit, tempTeam.StatHierarchy.GetPlayerDash());
                tempPlayer.Unit.AbilityManager.Add(dashAbility);
                var primeAbility = new Ability_PrimeTransfer.Data()
                    .CreateInstance(tempPlayer.Unit, Stats.Empty, tempTeam.StatHierarchy.GetPrimeGroup());
                tempPlayer.Unit.AbilityManager.Add(primeAbility);
                var info = new Death.Run.Core.CharacterInfo();
                RuntimeStats weaponStats = null;
                RuntimeStats dashStats = null;
                var deathAsm = typeof(Death.Game).Assembly;
                var iAttackType = deathAsm.GetType("Death.Run.Core.Abilities.Actives.IAttack")
                    ?? deathAsm.GetType("Death.Run.Core.Abilities.IAttack");
                var iDefenseType = deathAsm.GetType("Death.Run.Behaviours.Abilities.IDefense");
                if (iAttackType != null)
                {
                    var tryGet = typeof(AbilityManager).GetMethod("TryGet")?.MakeGenericMethod(iAttackType);
                    if (tryGet != null)
                    {
                        var args = new object[] { null };
                        if ((bool)tryGet.Invoke(tempPlayer.Unit.AbilityManager, args) && args[0] != null)
                            weaponStats = Traverse.Create(args[0]).Property("Stats").GetValue<RuntimeStats>();
                    }
                }
                if (iDefenseType != null)
                {
                    var tryGet = typeof(AbilityManager).GetMethod("TryGet")?.MakeGenericMethod(iDefenseType);
                    if (tryGet != null)
                    {
                        var args = new object[] { null };
                        if ((bool)tryGet.Invoke(tempPlayer.Unit.AbilityManager, args) && args[0] != null)
                            dashStats = Traverse.Create(args[0]).Property("Stats").GetValue<RuntimeStats>();
                    }
                }
                info.Populate(
                    tempTeam.StatHierarchy,
                    tempPlayer.Unit.AbilityManager,
                    tempPlayer.Boons,
                    profile.TalentsState,
                    charData,
                    tempTeam,
                    tempTeam.StatHierarchy.Root,
                    weaponStats,
                    dashStats,
                    tempPlayer.Entity.Stats,
                    () => false 
                );
                try
                {
                    var eStats = tempPlayer.Entity.Stats;
                    CoopPlugin.FileLog($"HubScreenUtils: Preview Entity.Stats — HP={eStats.Get(StatId.Health)}, MoveSpd={eStats.Get(StatId.MovementSpeed)}, AtkSpd={eStats.Get(StatId.AttackSpeed)}");
                    CoopPlugin.FileLog($"HubScreenUtils: Preview — weaponStats={weaponStats != null}, dashStats={dashStats != null}, iAttack={iAttackType != null}, iDefense={iDefenseType != null}");
                }
                catch (System.Exception diagEx2)
                {
                    CoopPlugin.FileLog($"HubScreenUtils: Preview root diag error: {diagEx2.Message}");
                }
                CoopPlugin.FileLog($"HubScreenUtils: BuildCharacterInfoViaPreview — built for {charCode}.");
                return info;
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"HubScreenUtils: BuildCharacterInfoViaPreview error: {ex.Message}");
                return null;
            }
            finally
            {
                if (tempPlayer != null)
                {
                    try { tempPlayer.Entity.Reclaim(); }
                    catch { try { Object.Destroy(tempPlayer.gameObject); } catch { } }
                }
            }
        }
        public static void SwapControllerProfile(object controller, Profile profile)
        {
            var controllerType = typeof(ItemController);
            var profileField = controllerType.GetField("Profile",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var opsField = controllerType.GetField("Operations",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var charCodeField = controllerType.GetField("_inspectedCharacterCode",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (profileField != null)
                profileField.SetValue(controller, profile);
            if (opsField != null)
                opsField.SetValue(controller, new ItemOperations(profile));
            if (charCodeField != null)
                charCodeField.SetValue(controller, profile.SelectedCharacter.Code);
            var verifyProfile = profileField?.GetValue(controller) as Profile;
            CoopPlugin.FileLog($"SwapControllerProfile: target Gold={profile.Gold}, verify={verifyProfile?.Gold}");
        }
        public static void SwapCharSheetPreviewerProfile(Facade_Lobby lobby, Profile profile)
        {
            try
            {
                var previewer = Traverse.Create(lobby).Field("_charSheetPreview").GetValue<object>();
                if (previewer == null) return;
                var previewerType = previewer.GetType();
                var profileField = previewerType.GetField("_profile",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (profileField != null)
                {
                    profileField.SetValue(previewer, profile);
                    var verify = profileField.GetValue(previewer) as Profile;
                    CoopPlugin.FileLog($"HubScreenUtils: CharSheetPreviewer _profile set (readonly bypass). verify={verify?.SelectedCharacter.Code}");
                }
                else
                {
                    CoopPlugin.FileLog("HubScreenUtils: CharSheetPreviewer _profile field not found!");
                }
                var charCode = profile.SelectedCharacter.Code;
                try
                {
                    var loadouts = profile.GetLoadoutsFor(charCode);
                    int filled = 0, total = 0;
                    foreach (var slot in loadouts.GetSelectedLoadout())
                    {
                        total++;
                        if (!slot.IsEmpty) filled++;
                    }
                    CoopPlugin.FileLog($"HubScreenUtils: Before Select — {charCode} loadout has {filled}/{total} items equipped.");
                }
                catch (System.Exception diagEx)
                {
                    CoopPlugin.FileLog($"HubScreenUtils: Loadout diagnostic failed: {diagEx.Message}");
                }
                Traverse.Create(previewer).Method("Select", new System.Type[] { typeof(CharacterCode) })
                    .GetValue(charCode);
                CoopPlugin.FileLog($"HubScreenUtils: CharSheetPreviewer swapped for {charCode}.");
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"HubScreenUtils: CharSheetPreviewer swap error: {ex.Message}");
            }
        }
        public static Death.Run.Core.CharacterInfo BuildCharacterInfo(Behaviour_Player player, Profile profile)
        {
            try
            {
                var info = new Death.Run.Core.CharacterInfo();
                var team = Teams.Get(TeamId.Player);
                RuntimeStats weaponStats = null;
                RuntimeStats dashStats = null;
                var deathAsm = typeof(Death.Game).Assembly;
                var iAttackType = deathAsm.GetType("Death.Run.Core.Abilities.IAttack");
                var iDefenseType = deathAsm.GetType("Death.Run.Behaviours.Abilities.IDefense");
                if (iAttackType != null)
                {
                    var tryGetAttack = typeof(AbilityManager).GetMethod("TryGet")?.MakeGenericMethod(iAttackType);
                    if (tryGetAttack != null)
                    {
                        var args = new object[] { null };
                        bool found = (bool)tryGetAttack.Invoke(player.Unit.AbilityManager, args);
                        if (found && args[0] != null)
                            weaponStats = Traverse.Create(args[0]).Property("Stats").GetValue<RuntimeStats>();
                    }
                }
                if (iDefenseType != null)
                {
                    var tryGetDefense = typeof(AbilityManager).GetMethod("TryGet")?.MakeGenericMethod(iDefenseType);
                    if (tryGetDefense != null)
                    {
                        var args = new object[] { null };
                        bool found = (bool)tryGetDefense.Invoke(player.Unit.AbilityManager, args);
                        if (found && args[0] != null)
                            dashStats = Traverse.Create(args[0]).Property("Stats").GetValue<RuntimeStats>();
                    }
                }
                info.Populate(
                    team.StatHierarchy,
                    player.Unit.AbilityManager,
                    player.Boons,
                    profile.TalentsState,
                    player.Data,
                    team,
                    team.StatHierarchy.Root,
                    weaponStats,
                    dashStats,
                    player.Entity.Stats,
                    () => player != null && player.Unit.ArmorEnabled
                );
                return info;
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"HubScreenUtils: BuildCharacterInfo error: {ex.Message}");
                return null;
            }
        }
        public static void SwapEquipmentIconOnGui(object equipGui, CharacterCode charCode)
        {
            try
            {
                var fixedSelect = Traverse.Create(equipGui).Field("_fixedEquipmentSelect").GetValue<object>();
                if (fixedSelect == null) return;
                var selectIcon = Traverse.Create(fixedSelect).Field("_icon").GetValue<object>();
                if (selectIcon == null) return;
                var charData = Database.Characters.Get(charCode);
                var sprite = Death.ResourceManagement.ResourceManager.Load<Sprite>(charData.EquipmentIconPath);
                var img = Traverse.Create(selectIcon).Field("_icon").GetValue<UnityEngine.UI.Image>();
                if (img != null)
                    img.sprite = sprite;
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"HubScreenUtils: SwapEquipmentIcon error: {ex.Message}");
            }
        }
        public static void SwapEquipmentIconOnScreen(Screen_Inventory screen, CharacterCode charCode)
        {
            var inventoryGui = Traverse.Create(screen).Field("_inventoryGui").GetValue<object>();
            if (inventoryGui == null) return;
            var equipGui = Traverse.Create(inventoryGui).Field("_equipment").GetValue<object>();
            if (equipGui == null) return;
            SwapEquipmentIconOnGui(equipGui, charCode);
        }
        public static void SyncLoadoutUnlocks(Profile p1, Profile p2)
        {
            try
            {
                int synced = 0;
                foreach (var charData in Database.Characters.All)
                {
                    var code = charData.Code;
                    var p1Loadouts = p1.GetLoadoutsFor(code).Loadouts;
                    var p2Loadouts = p2.GetLoadoutsFor(code).Loadouts;
                    var p1Unlocked = Traverse.Create(p1Loadouts).Field("_unlocked").GetValue<bool[]>();
                    var p2Unlocked = Traverse.Create(p2Loadouts).Field("_unlocked").GetValue<bool[]>();
                    if (p1Unlocked != null && p2Unlocked != null)
                    {
                        int len = System.Math.Min(p1Unlocked.Length, p2Unlocked.Length);
                        for (int i = 0; i < len; i++)
                        {
                            if (p1Unlocked[i] && !p2Unlocked[i])
                            {
                                p2Unlocked[i] = true;
                                synced++;
                            }
                        }
                    }
                    var p1TalentLoadouts = p1.TalentsState.GetLoadoutsFor(code);
                    var p2TalentLoadouts = p2.TalentsState.GetLoadoutsFor(code);
                    var p1TUnlocked = Traverse.Create(p1TalentLoadouts).Field("_unlocked").GetValue<bool[]>();
                    var p2TUnlocked = Traverse.Create(p2TalentLoadouts).Field("_unlocked").GetValue<bool[]>();
                    if (p1TUnlocked != null && p2TUnlocked != null)
                    {
                        int len = System.Math.Min(p1TUnlocked.Length, p2TUnlocked.Length);
                        for (int i = 0; i < len; i++)
                        {
                            if (p1TUnlocked[i] && !p2TUnlocked[i])
                            {
                                p2TUnlocked[i] = true;
                                synced++;
                            }
                        }
                    }
                }
                if (synced > 0)
                    CoopPlugin.FileLog($"HubScreenUtils: Synced {synced} loadout unlocks from P1 to P2.");
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"HubScreenUtils: SyncLoadoutUnlocks error: {ex.Message}");
            }
        }
    }
    [HarmonyPatch(typeof(Screen_Armory), "OnHide")]
    public static class ScreenArmory_OnHide_Patch
    {
        static void Postfix()
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
                    FacadeLobby_OpenArmory_Patch.SwapArmoryToP1(lobby);
                    CoopPlugin.FileLog("HubScreenPatches: Armory restored to P1.");
                }
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"HubScreenPatches: Armory OnHide error: {ex.Message}");
            }
        }
    }
    [HarmonyPatch(typeof(Facade_Lobby), "OpenTalents")]
    public static class FacadeLobby_OpenTalents_Patch
    {
        static void Prefix(Facade_Lobby __instance)
        {
            if (!GamepadInputHandler.LastInteractWasP2) return;
            if (CoopP2Profile.Instance == null) return;
            GamepadInputHandler.EnableGamepadUI();
            try
            {
                SwapTalentsProfile(__instance, CoopP2Profile.Instance);
                HubScreenUtils.SwapCharSheetPreviewerProfile(__instance, CoopP2Profile.Instance);
                CoopPlugin.FileLog("HubScreenPatches: Talents swapped to P2 Profile.");
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"HubScreenPatches: OpenTalents P2 swap error: {ex.Message}");
            }
        }
        public static void SwapTalentsProfile(Facade_Lobby lobby, Profile profile)
        {
            var trav = Traverse.Create(lobby);
            var gui = trav.Field("_gui").GetValue<object>();
            if (gui == null) return;
            var tryGetMethod = gui.GetType().GetMethod("TryGetPresenter");
            if (tryGetMethod == null) return;
            var genericMethod = tryGetMethod.MakeGenericMethod(typeof(Screen_Talents));
            var args = new object[] { null };
            bool found = (bool)genericMethod.Invoke(gui, args);
            if (!found || !(args[0] is Screen_Talents talentsScreen)) return;
            var talentsGui = Traverse.Create(talentsScreen).Field("_talentsGui").GetValue<object>();
            if (talentsGui == null) return;
            Traverse.Create(talentsGui).Field("_profile").SetValue(profile);
            var talentsState = profile.TalentsState;
            var charCode = profile.SelectedCharacter.Code;
            Traverse.Create(talentsGui).Field("_selectedCharCode").SetValue(charCode);
            var talentTree = Traverse.Create(talentsGui).Field("_talentTree").GetValue<object>();
            if (talentTree != null)
            {
                Traverse.Create(talentTree).Field("_talentsState").SetValue(talentsState);
                Traverse.Create(talentTree).Method("ShowForCharacter", new System.Type[] { typeof(CharacterCode) })
                    .GetValue(charCode);
            }
            var loadoutSelect = Traverse.Create(talentsGui).Field("_talentLoadoutSelect").GetValue<object>();
            if (loadoutSelect != null)
            {
                Traverse.Create(loadoutSelect).Field("_talentsState").SetValue(talentsState);
            }
            Traverse.Create(talentsGui).Method("UpdateResetButton").GetValue();
        }
    }
    [HarmonyPatch(typeof(Screen_Talents), "OnHide")]
    public static class ScreenTalents_OnHide_Patch
    {
        static void Postfix()
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
                        FacadeLobby_OpenTalents_Patch.SwapTalentsProfile(lobby, state.ActiveProfile);
                        HubScreenUtils.SwapCharSheetPreviewerProfile(lobby, state.ActiveProfile);
                        CoopPlugin.FileLog("HubScreenPatches: Talents restored to P1.");
                    }
                }
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"HubScreenPatches: Talents OnHide error: {ex.Message}");
            }
        }
    }
    [HarmonyPatch(typeof(Facade_Lobby), "OpenDarkness")]
    public static class FacadeLobby_OpenDarkness_Patch
    {
        static void Prefix()
        {
            if (!GamepadInputHandler.LastInteractWasP2) return;
            GamepadInputHandler.EnableGamepadUI();
            CoopPlugin.FileLog("HubScreenPatches: Darkness opened by P2 (shared, gamepad UI enabled).");
        }
    }
    [HarmonyPatch(typeof(Screen_Darkness), "OnHide")]
    public static class ScreenDarkness_OnHide_Patch
    {
        static void Postfix()
        {
            if (!GamepadInputHandler.P2InMenu) return;
            GamepadInputHandler.DisableGamepadUI();
            CoopPlugin.FileLog("HubScreenPatches: Darkness closed by P2.");
        }
    }
    [HarmonyPatch(typeof(Facade_Lobby), "OpenUpgrades")]
    public static class FacadeLobby_OpenUpgrades_Patch
    {
        static void Prefix()
        {
            if (!GamepadInputHandler.LastInteractWasP2) return;
            GamepadInputHandler.EnableGamepadUI();
            CoopPlugin.FileLog("HubScreenPatches: Upgrades opened by P2 (shared, gamepad UI enabled).");
        }
    }
    [HarmonyPatch]
    public static class ScreenUpgrades_OnHide_Patch
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            var screenType = typeof(Facade_Lobby).Assembly
                .GetType("Death.TimesRealm.UserInterface.Upgrades.Screen_Upgrades");
            if (screenType != null)
                return screenType.GetMethod("OnHide",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return null;
        }
        static void Postfix()
        {
            if (!GamepadInputHandler.P2InMenu) return;
            GamepadInputHandler.DisableGamepadUI();
            try
            {
                var lobby = Object.FindObjectOfType<Facade_Lobby>();
                if (lobby != null && CoopP2Profile.Instance != null)
                {
                    var state = Traverse.Create(lobby).Field("_state").GetValue<ILobbyGameState>();
                    if (state != null)
                        HubScreenUtils.SyncLoadoutUnlocks(state.ActiveProfile, CoopP2Profile.Instance);
                }
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"HubScreenPatches: Upgrades loadout sync error: {ex.Message}");
            }
            CoopPlugin.FileLog("HubScreenPatches: Upgrades closed by P2.");
        }
    }
    [HarmonyPatch(typeof(Screen_Inventory), "OnShow")]
    public static class ScreenInventory_OnShow_Patch
    {
        public static bool IsP2Open;
        static void Prefix(Screen_Inventory __instance)
        {
            IsP2Open = false;
            if (CoopP2Profile.Instance == null) return;
            bool isP2 = GamepadInputHandler.LastInteractWasP2;
            if (!isP2)
            {
                var gp = Gamepad.current;
                if (gp != null && gp.buttonWest.wasPressedThisFrame)
                    isP2 = true;
            }
            if (!isP2) return;
            IsP2Open = true;
            GamepadInputHandler.EnableGamepadUI();
            try
            {
                SwapInventoryToP2(__instance);
                CoopPlugin.FileLog("HubScreenPatches: Inventory OnShow Prefix — swapped to P2.");
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"HubScreenPatches: Inventory OnShow Prefix P2 swap error: {ex.Message}");
            }
        }
        static void SwapInventoryToP2(Screen_Inventory screen)
        {
            var p2Profile = CoopP2Profile.Instance;
            ScreenInventory_SwapHelper.SwapInventoryToProfile(screen, p2Profile);
            var charPanel = Traverse.Create(screen).Field("_characterPanel").GetValue<object>();
            var lobby = Object.FindObjectOfType<Facade_Lobby>();
            if (lobby != null)
                HubScreenUtils.SwapCharSheetPreviewerProfile(lobby, p2Profile);
            bool inRun = Claw.Core.SingletonBehaviour<Facade_Run>.Exists;
            if (inRun)
            {
                var p2Info = CoopRunCharacterInfo.Info;
                if (p2Info != null && charPanel != null)
                {
                    CoopRunCharacterInfo.CachedP1Info = Traverse.Create(charPanel)
                        .Field("_info").GetValue<Death.Run.Core.CharacterInfo>();
                    Traverse.Create(charPanel).Field("_info").SetValue(p2Info);
                    Traverse.Create(charPanel).Field("_profile").SetValue(p2Profile);
                    var charStats = Traverse.Create(charPanel).Field("_characterStats").GetValue<object>();
                    if (charStats != null)
                        Traverse.Create(charStats).Field("_info").SetValue(p2Info);
                    var talentTree = Traverse.Create(charPanel).Field("_talentTree").GetValue<object>();
                    if (talentTree != null)
                        Traverse.Create(talentTree).Field("_talentsState").SetValue(p2Profile.TalentsState);
                    p2Info.OnInfoChangedEv += screen.RefreshStats;
                    CoopPlugin.FileLog("HubScreenPatches: Inventory charPanel swapped to P2 run-path (live).");
                }
            }
            else if (charPanel != null)
            {
                Traverse.Create(charPanel).Field("_profile").SetValue(p2Profile);
                var talentTree = Traverse.Create(charPanel).Field("_talentTree").GetValue<object>();
                if (talentTree != null)
                    Traverse.Create(talentTree).Field("_talentsState").SetValue(p2Profile.TalentsState);
                CoopPlugin.FileLog("HubScreenPatches: Inventory charPanel swapped to P2 hub-path.");
            }
            var inventoryGui = Traverse.Create(screen).Field("_inventoryGui").GetValue<object>();
            if (inventoryGui != null)
            {
                var equipGui = Traverse.Create(inventoryGui).Field("_equipment").GetValue<object>();
                if (equipGui != null)
                {
                    HubScreenUtils.SwapEquipmentIconOnGui(equipGui, p2Profile.SelectedCharacter.Code);
                    var refreshMethod = equipGui.GetType().GetMethod("Refresh",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    refreshMethod?.Invoke(equipGui, null);
                }
            }
            if (charPanel != null)
            {
                var refreshAsync = charPanel.GetType().GetMethod("RefreshAsync",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                refreshAsync?.Invoke(charPanel, null);
            }
        }
    }
    public static class ScreenInventory_SwapHelper
    {
        public static void SwapInventoryToProfile(Screen_Inventory screen, Profile profile)
        {
            var inventoryGui = Traverse.Create(screen).Field("_inventoryGui").GetValue<object>();
            if (inventoryGui == null)
            {
                CoopPlugin.FileLog("SwapInventoryToProfile: _inventoryGui is null");
                return;
            }
            var controller = Traverse.Create(inventoryGui).Field("_controller").GetValue<object>();
            if (controller == null)
            {
                CoopPlugin.FileLog("SwapInventoryToProfile: _controller is null");
                return;
            }
            HubScreenUtils.SwapControllerProfile(controller, profile);
            var backpackGrid = Traverse.Create(inventoryGui).Field("_backpack").GetValue<object>();
            if (backpackGrid != null)
            {
                Traverse.Create(backpackGrid).Method("Set", new System.Type[] { typeof(ItemGrid) })
                    .GetValue(profile.Backpack);
            }
            CoopPlugin.FileLog($"SwapInventoryToProfile: Swapped to profile Gold={profile.Gold}");
        }
    }
    [HarmonyPatch(typeof(Screen_Inventory), "OnHide")]
    public static class ScreenInventory_OnHide_Patch
    {
        static void Postfix(Screen_Inventory __instance)
        {
            if (!GamepadInputHandler.P2InMenu) return;
            GamepadInputHandler.DisableGamepadUI();
            try
            {
                CoopP2Profile.SaveToCoopData();
                CoopP2Save.SaveIfDirty();
                Profile p1Profile = null;
                Facade_Lobby lobby = null;
                lobby = Object.FindObjectOfType<Facade_Lobby>();
                if (lobby != null)
                {
                    var state = Traverse.Create(lobby).Field("_state").GetValue<ILobbyGameState>();
                    if (state != null)
                        p1Profile = state.ActiveProfile;
                }
                if (p1Profile == null)
                    p1Profile = Death.Game.ActiveProfile;
                if (p1Profile != null)
                {
                    ScreenInventory_SwapHelper.SwapInventoryToProfile(__instance, p1Profile);
                    var charPanel = Traverse.Create(__instance).Field("_characterPanel").GetValue<object>();
                    if (lobby != null)
                        HubScreenUtils.SwapCharSheetPreviewerProfile(lobby, p1Profile);
                    bool inRun = Claw.Core.SingletonBehaviour<Facade_Run>.Exists;
                    if (inRun)
                    {
                        var p2Info = CoopRunCharacterInfo.Info;
                        if (p2Info != null)
                            p2Info.OnInfoChangedEv -= __instance.RefreshStats;
                        if (charPanel != null)
                        {
                            var p1Info = CoopRunCharacterInfo.CachedP1Info;
                            if (p1Info != null)
                            {
                                Traverse.Create(charPanel).Field("_info").SetValue(p1Info);
                                Traverse.Create(charPanel).Field("_profile").SetValue(p1Profile);
                                var charStats = Traverse.Create(charPanel).Field("_characterStats").GetValue<object>();
                                if (charStats != null)
                                    Traverse.Create(charStats).Field("_info").SetValue(p1Info);
                                var talentTree = Traverse.Create(charPanel).Field("_talentTree").GetValue<object>();
                                if (talentTree != null)
                                    Traverse.Create(talentTree).Field("_talentsState").SetValue(p1Profile.TalentsState);
                            }
                            HubScreenUtils.SwapEquipmentIconOnScreen(__instance, p1Profile.SelectedCharacter.Code);
                        }
                    }
                    else if (charPanel != null)
                    {
                        Traverse.Create(charPanel).Field("_profile").SetValue(p1Profile);
                        var talentTree = Traverse.Create(charPanel).Field("_talentTree").GetValue<object>();
                        if (talentTree != null)
                            Traverse.Create(talentTree).Field("_talentsState").SetValue(p1Profile.TalentsState);
                    }
                    CoopPlugin.FileLog("HubScreenPatches: Inventory restored to P1.");
                }
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"HubScreenPatches: Inventory OnHide error: {ex.Message}");
            }
        }
    }
    [HarmonyPatch(typeof(Facade_Lobby), "OpenStash")]
    public static class FacadeLobby_OpenStash_Patch
    {
        static void Prefix(Facade_Lobby __instance)
        {
            if (!GamepadInputHandler.LastInteractWasP2) return;
            if (CoopP2Profile.Instance == null) return;
            GamepadInputHandler.EnableGamepadUI();
            try
            {
                SwapStashToProfile(__instance, CoopP2Profile.Instance);
                CoopPlugin.FileLog("HubScreenPatches: Stash controller swapped to P2 (stash grid stays shared).");
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"HubScreenPatches: OpenStash P2 swap error: {ex.Message}");
            }
        }
        public static void SwapStashToProfile(Facade_Lobby lobby, Profile profile)
        {
            var trav = Traverse.Create(lobby);
            var gui = trav.Field("_gui").GetValue<object>();
            if (gui == null) return;
            var tryGetMethod = gui.GetType().GetMethod("TryGetPresenter");
            if (tryGetMethod == null) return;
            var genericMethod = tryGetMethod.MakeGenericMethod(typeof(Screen_Stash));
            var args = new object[] { null };
            bool found = (bool)genericMethod.Invoke(gui, args);
            if (!found || !(args[0] is Screen_Stash stashScreen)) return;
            var stashGui = Traverse.Create(stashScreen).Field("_stashGui").GetValue<object>();
            if (stashGui == null) return;
            Traverse.Create(stashGui).Field("_profile").SetValue(profile);
            var controller = Traverse.Create(stashGui).Field("_controller").GetValue<object>();
            if (controller != null)
            {
                HubScreenUtils.SwapControllerProfile(controller, profile);
            }
            var backpackGrid = Traverse.Create(stashGui).Field("_backpack").GetValue<object>();
            if (backpackGrid != null)
            {
                Traverse.Create(backpackGrid).Method("Set", new System.Type[] { typeof(ItemGrid) })
                    .GetValue(profile.Backpack);
            }
        }
    }
    [HarmonyPatch(typeof(Screen_Stash), "OnHide")]
    public static class ScreenStash_OnHide_Patch
    {
        static void Postfix()
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
                        FacadeLobby_OpenStash_Patch.SwapStashToProfile(lobby, state.ActiveProfile);
                        CoopPlugin.FileLog("HubScreenPatches: Stash restored to P1.");
                    }
                }
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"HubScreenPatches: Stash OnHide error: {ex.Message}");
            }
        }
    }
    [HarmonyPatch(typeof(GUI_Items_Stash), "MoveLoadoutSelection")]
    public static class GUIItemsStash_MoveLoadoutSelection_Patch
    {
        static bool Prefix(object __instance, ArrayDirection direction)
        {
            try
            {
                var focusedSlot = Traverse.Create(__instance).Property("FocusedSlot").GetValue<object>();
                if (focusedSlot != null)
                {
                    var collection = Traverse.Create(focusedSlot).Property("Collection")
                        .GetValue<ItemCollection>();
                    if (collection == ItemCollection.Stash)
                    {
                        Traverse.Create(__instance).Field("_stashPageTabManager")
                            .Method("MoveSelection", new System.Type[] { typeof(ArrayDirection) })
                            .GetValue(direction);
                    }
                    else
                    {
                        var equipGui = Traverse.Create(__instance).Property("EquipmentGui").GetValue<object>();
                        if (equipGui != null)
                        {
                            Traverse.Create(equipGui)
                                .Method("MoveLoadoutSelection", new System.Type[] { typeof(ArrayDirection) })
                                .GetValue(direction);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"StashLoadoutFix: Error: {ex.Message}");
                return true; 
            }
            return false; 
        }
    }
    [HarmonyPatch(typeof(Screen_Boons), "OnShow")]
    public static class ScreenBoons_OnShow_Patch
    {
        static void Postfix(Screen_Boons __instance)
        {
            if (PlayerRegistry.Count < 2) return;
            bool isP2 = GamepadInputHandler.LastInteractWasP2;
            if (!isP2)
            {
                var gp = Gamepad.current;
                if (gp != null && gp.buttonNorth.wasPressedThisFrame)
                    isP2 = true;
            }
            if (!isP2) return;
            GamepadInputHandler.EnableGamepadUI();
            try
            {
                var p2 = PlayerRegistry.GetPlayer(1);
                if (p2 == null || p2.Boons == null)
                {
                    CoopPlugin.FileLog("HubScreenPatches: Boons — P2 or P2.Boons is null.");
                    return;
                }
                var boonListGui = Traverse.Create(__instance).Field("_boonListGui").GetValue<object>();
                if (boonListGui == null) return;
                var boonsView = Traverse.Create(boonListGui).Field("_boons").GetValue<object>();
                if (boonsView == null) return;
                Traverse.Create(boonsView).Field("_boonManager").SetValue(p2.Boons);
                CoopPlugin.FileLog("HubScreenPatches: Boons swapped to P2's BoonManager.");
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"HubScreenPatches: Boons OnShow error: {ex.Message}");
            }
        }
    }
    [HarmonyPatch(typeof(Screen_Boons), "OnHide")]
    public static class ScreenBoons_OnHide_Patch
    {
        static void Postfix()
        {
            if (!GamepadInputHandler.P2InMenu) return;
            GamepadInputHandler.DisableGamepadUI();
            try
            {
                var p1 = PlayerRegistry.GetPlayer(0);
                if (p1 == null || p1.Boons == null) return;
                var boonScreen = Object.FindObjectOfType<Screen_Boons>();
                if (boonScreen == null) return;
                var boonListGui = Traverse.Create(boonScreen).Field("_boonListGui").GetValue<object>();
                if (boonListGui == null) return;
                var boonsView = Traverse.Create(boonListGui).Field("_boons").GetValue<object>();
                if (boonsView == null) return;
                Traverse.Create(boonsView).Field("_boonManager").SetValue(p1.Boons);
                CoopPlugin.FileLog("HubScreenPatches: Boons restored to P1's BoonManager.");
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"HubScreenPatches: Boons OnHide error: {ex.Message}");
            }
        }
    }
    [HarmonyPatch(typeof(Facade_Lobby), "OpenLibrary")]
    public static class FacadeLobby_OpenLibrary_Patch
    {
        static void Prefix()
        {
            if (!GamepadInputHandler.LastInteractWasP2) return;
            GamepadInputHandler.EnableGamepadUI();
            CoopPlugin.FileLog("HubScreenPatches: Library opened by P2 (shared, gamepad UI enabled).");
        }
    }
    [HarmonyPatch(typeof(Screen_Library), "OnHide")]
    public static class ScreenLibrary_OnHide_Patch
    {
        static void Postfix()
        {
            if (!GamepadInputHandler.P2InMenu) return;
            GamepadInputHandler.DisableGamepadUI();
            CoopPlugin.FileLog("HubScreenPatches: Library closed by P2.");
        }
    }
    [HarmonyPatch(typeof(Screen_Encounters), "PostShow")]
    public static class ScreenEncounters_PostShow_Patch
    {
        static void Postfix()
        {
            if (!GamepadInputHandler.LastInteractWasP2) return;
            GamepadInputHandler.EnableGamepadUI();
            CoopPlugin.FileLog("HubScreenPatches: Encounter screen opened by P2 (gamepad UI enabled).");
        }
    }
    [HarmonyPatch(typeof(Screen_Encounters), "OnHide")]
    public static class ScreenEncounters_OnHide_Patch
    {
        static void Postfix()
        {
            if (!GamepadInputHandler.P2InMenu) return;
            GamepadInputHandler.DisableGamepadUI();
            CoopPlugin.FileLog("HubScreenPatches: Encounter screen closed by P2.");
        }
    }
}