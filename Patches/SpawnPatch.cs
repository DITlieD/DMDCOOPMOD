using HarmonyLib;
using Death.Run.Systems;
using Death.Run.Behaviours;
using Death.Run.Behaviours.Entities;
using Death.Run.Behaviours.Objects;
using Death.Run.Behaviours.Players;
using Death.Run.Behaviours.Abilities;
using Death.Run.Core;
using Death.Run.Core.Entities;
using Death.Run.Behaviours.Events;
using Death.ResourceManagement;
using Death.Data;
using UnityEngine;
using System.Reflection;
namespace DeathMustDieCoop.Patches
{
    [HarmonyPatch]
    public static class SystemPlayerManager_Init_Patch
    {
        static MethodBase TargetMethod()
        {
            var method = typeof(System_PlayerManager).GetMethod("Init",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new System.Type[] { typeof(Facade_Run) },
                null);
            if (method != null)
                CoopPlugin.FileLog($"SpawnPatch: TargetMethod resolved Init on {method.DeclaringType.Name}");
            else
                CoopPlugin.FileLog("SpawnPatch: ERROR — Could NOT resolve Init(Facade_Run)!");
            return method;
        }
        static void Postfix(object __instance)
        {
            CoopPlugin.FileLog($"SpawnPatch: Init postfix on {__instance?.GetType()?.Name}");
            if (!(__instance is System_PlayerManager mgr))
            {
                CoopPlugin.FileLog("SpawnPatch: Not PlayerManager, skip.");
                return;
            }
            CoopPlugin.FileLog("SpawnPatch: IS System_PlayerManager — spawning P2...");
            CoopRuntime.EnsureExists();
            CoopAggroTracker.Clear();
            try
            {
                var trav = Traverse.Create(mgr);
                var p1 = trav.Field("_player").GetValue<Behaviour_Player>();
                if (p1 == null)
                {
                    CoopPlugin.FileLog("SpawnPatch: P1 is null!");
                    return;
                }
                CoopPlugin.FileLog($"SpawnPatch: P1 at {p1.transform.position}");
                var p2CharCode = CoopP2Save.Data.SelectedCharacterCode;
                CharacterData charData;
                if (!string.IsNullOrEmpty(p2CharCode))
                {
                    try
                    {
                        charData = Database.Characters.Get(p2CharCode);
                        CoopPlugin.FileLog($"SpawnPatch: P2 using saved character: {p2CharCode}");
                    }
                    catch
                    {
                        charData = mgr.CharacterData;
                        CoopPlugin.FileLog($"SpawnPatch: P2 saved character '{p2CharCode}' invalid, cloning P1");
                    }
                }
                else
                {
                    charData = mgr.CharacterData;
                    CoopPlugin.FileLog($"SpawnPatch: P2 cloning P1 character (no P2 save)");
                }
                CoopPlugin.FileLog($"SpawnPatch: EntityPath={charData?.EntityPath}");
                Entity entityPrefab = ResourceManager.Load<Entity>(charData.EntityPath);
                Team playerTeam = Teams.Get(TeamId.Player);
                var p2Team = new Team(TeamId.Player, playerTeam.GlobalStats);
                Vector2 spawnPos = (Vector2)p1.transform.position + new Vector2(1.5f, 0f);
                var damageSource = new DamageSource("Coop-P2", TeamId.Player);
                PlayerRegistry.SpawningP2 = true;
                Entity p2Entity = Object.Instantiate(entityPrefab, spawnPos, Quaternion.identity);
                p2Entity.SetDestroyOnReclaim();
                var p2Behaviour = p2Entity.GetComponent<Behaviour_Player>();
                if (p2Behaviour != null)
                    Traverse.Create(p2Behaviour).Field("_isPrimaryPlayerInstance").SetValue(false);
                if (p1 != null)
                {
                    Player.Init(p1);
                    CoopPlugin.FileLog("SpawnPatch: Restored Player.Instance to P1.");
                }
                p2Entity.Init(damageSource, charData.BaseStats, p2Team.StatHierarchy.GetPlayerGroup(), p2Team);
                PlayerRegistry.SpawningP2 = false;
                var p2 = p2Behaviour;
                if (p2 == null)
                {
                    CoopPlugin.FileLog("SpawnPatch: P2 entity missing Behaviour_Player!");
                    return;
                }
                CoopPlugin.FileLog("SpawnPatch: P2 entity created, configuring...");
                var p2Profile = CoopP2Profile.Instance;
                if (p2Profile != null)
                {
                    p2.Prepare(charData, p2Profile.GetActiveEquipment(), p2Profile.Backpack);
                    CoopPlugin.FileLog("SpawnPatch: P2 using own equipment from CoopP2Profile.");
                }
                else
                {
                    p2.Prepare(charData, Death.Game.ActiveProfile.GetActiveEquipment(), Death.Game.ActiveProfile.Backpack);
                    CoopPlugin.FileLog("SpawnPatch: WARNING — CoopP2Profile null, using P1 equipment.");
                }
                int maxLevel = 200;
                try
                {
                    var rules = Traverse.Create(mgr).Property("Rules").GetValue();
                    if (rules != null)
                        maxLevel = Traverse.Create(rules).Field("MaxLevel").GetValue<int>();
                }
                catch { }
                if (maxLevel <= 0) maxLevel = 200;
                CoopPlugin.FileLog($"SpawnPatch: P2 maxLevel={maxLevel}");
                p2.XpTracker.Init(maxLevel, Database.XpPerLevel);
                var dashData = Database.Weapon.Get(charData.StartingDashCode);
                var dashAbility = dashData.CreateAbility(p2.Unit, p2.Unit.Team.StatHierarchy.GetPlayerDash());
                p2.Unit.AbilityManager.Add(dashAbility);
                var primeAbility = new Ability_PrimeTransfer.Data()
                    .CreateInstance(p2.Unit, Stats.Empty, p2.Unit.Team.StatHierarchy.GetPrimeGroup());
                p2.Unit.AbilityManager.Add(primeAbility);
                CoopRunCharacterInfo.Setup(p2, charData, p2Profile ?? Death.Game.ActiveProfile);
                var p2TalentLoadout = p2Profile != null
                    ? p2Profile.TalentsState.GetForCharacter(charData.Code)
                    : null;
                p2.Init(p2TalentLoadout, CoopRunCharacterInfo.Regenerate);
                CoopRunCharacterInfo.Regenerate();
                CoopPlugin.FileLog("SpawnPatch: P2 Init complete (equipment + talents + CharacterInfo).");
                var p2GoldCollector = p2Entity.GetComponent<Behaviour_GoldCollector>();
                if (p2GoldCollector != null)
                {
                    p2GoldCollector.Init(gold =>
                    {
                        Death.Game.ActiveProfile.Gold += gold.Amount;
                        if (CoopP2Profile.Instance != null)
                            CoopP2Profile.Instance.Gold += gold.Amount;
                    });
                    CoopPlugin.FileLog("SpawnPatch: P2 GoldCollector initialized.");
                }
                p2Entity.gameObject.AddComponent<GamepadInputHandler>();
                p2.XpTracker.OnLevelsGainedEv += CoopRewardState.OnP2LevelUp;
                var mgrRef = mgr; 
                p2.Unit.OnDiedEv.AddListener(ev =>
                {
                    CoopPlugin.FileLog("SpawnPatch: P2 died! Firing Event_PlayerDied.");
                    Traverse.Create(mgrRef).Method("OnPlayerDeath",
                        new System.Type[] { typeof(Event_UnitDied) })
                        .GetValue(ev);
                });
                try
                {
                    var options = Traverse.Create(mgr).Property("Options").GetValue<object>();
                    var realmData = Traverse.Create(options).Field("RealmData").GetValue<object>();
                    var lightFab = Traverse.Create(realmData).Property("PlayerLightFab").GetValue<MonoBehaviour>();
                    CoopPlugin.FileLog($"SpawnPatch: Light lookup — options={options != null}, realm={realmData != null}, fab={lightFab != null}");
                    if (lightFab != null)
                    {
                        var p2LightGo = Object.Instantiate(lightFab.gameObject);
                        var p2Light = p2LightGo.GetComponent<PlayerLight>();
                        if (p2Light != null)
                        {
                            p2Light.Init(p2.transform);
                            LightPatch_ScaleUp.ScaleLight(p2Light);
                            CoopPlugin.FileLog("SpawnPatch: P2 light added.");
                        }
                    }
                }
                catch (System.Exception ex2)
                {
                    CoopPlugin.FileLog($"SpawnPatch: Light setup failed (non-fatal): {ex2.Message}");
                }
                LightPatch_ScaleUp.ScaleAllLights();
                CoopCameraController.SetupForRun();
                CoopHud.SetupForP2(p2);
                VisualClarityController.Setup();
                CoopPlugin.FileLog($"SpawnPatch: P2 spawned at {spawnPos}!");
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"SpawnPatch: FAILED: {ex}");
            }
        }
    }
    [HarmonyPatch(typeof(System_PlayerManager), "OnInit")]
    public static class SystemPlayerManager_OnInit_Patch
    {
        static void Postfix(System_PlayerManager __instance)
        {
            CoopPlugin.FileLog("SpawnPatch: OnInit backup postfix fired.");
        }
    }
    [HarmonyPatch(typeof(System_PlayerManager), "OnCleanup")]
    public static class SystemPlayerManager_Cleanup_Patch
    {
        static void Postfix()
        {
            CoopRunCharacterInfo.Cleanup();
            VisualClarityController.Cleanup();
            GamepadInputHandler.LastInteractWasP2 = false;
            GamepadInputHandler.P2InMenu = false;
        }
    }
}