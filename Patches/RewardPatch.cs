using HarmonyLib;
using Claw.Core.Chaos;
using Death.Run.Behaviours;
using Death.Run.Behaviours.Entities;
using Death.Run.Behaviours.Players;
using Death.Run.Systems;
using Death.Run.Systems.Rewards;
using Death.Run.UserInterface.Rewards;
using Death.Run.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
namespace DeathMustDieCoop.Patches
{
    public static class CoopRewardState
    {
        public static int P2PendingRewards;
        public static bool IsP2Turn;
        public static bool SharingXp;
        private static System_Rewards _cachedRewards;
        private static int _xpShareLogCounter;
        public static RewardGenerator P1RewardGenerator;
        public static RewardGenerator P2RewardGenerator;
        public static System_Rewards CachedRewards
        {
            get
            {
                if (_cachedRewards == null)
                    _cachedRewards = Object.FindObjectOfType<System_Rewards>();
                return _cachedRewards;
            }
        }
        public static void ShareXpToPlayer(Behaviour_XpTracker tracker, float amount)
        {
            Traverse.Create(tracker).Method("GainRawXp", new System.Type[] { typeof(float), typeof(bool) })
                .GetValue(amount, false);
        }
        public static void OnP2LevelUp(int count)
        {
            P2PendingRewards += count;
            CoopPlugin.FileLog($"RewardPatch: P2 leveled up! P2Pending={P2PendingRewards}");
            var runtime = CoopRuntime.Instance;
            if (runtime != null)
                runtime.StartCoroutine(DeferredP2FlowCheck());
            else
                CoopPlugin.FileLog("RewardPatch: ERROR — CoopRuntime.Instance is null, cannot defer P2 flow!");
        }
        private static IEnumerator DeferredP2FlowCheck()
        {
            yield return null; 
            if (P2PendingRewards <= 0) yield break;
            if (CachedRewards == null) yield break;
            var trav = Traverse.Create(CachedRewards);
            bool givingRewards = trav.Field("_givingBoonRewards").GetValue<bool>();
            bool screenActive = trav.Field("_rewardsScreenActive").GetValue<bool>();
            if (!givingRewards && !screenActive)
            {
                int pending = P2PendingRewards;
                P2PendingRewards = 0;
                IsP2Turn = true;
                CoopPlugin.FileLog($"RewardPatch: P2 deferred flow start ({pending} blessings). Registry count={PlayerRegistry.Count}, [0]={PlayerRegistry.GetPlayer(0)?.name}, [1]={PlayerRegistry.GetPlayer(1)?.name}");
                SwapToPlayer(CachedRewards, PlayerRegistry.GetPlayer(1));
                SetGamepadForBlessingTurn(isP2Turn: true);
                CachedRewards.GiveFreeLevels(pending);
            }
            else
            {
                CoopPlugin.FileLog($"RewardPatch: P2 deferred check — flow active, will chain via EndRewards.");
            }
        }
        public static Behaviour_Player ActiveRewardPlayer;
        public static void SwapToPlayer(System_Rewards rewards, Behaviour_Player player)
        {
            if (player == null || rewards == null) return;
            ActiveRewardPlayer = player;
            var rewardsTrav = Traverse.Create(rewards);
            var playerMgr = rewardsTrav.Field("_playerManager").GetValue<System_PlayerManager>();
            if (playerMgr != null)
            {
                Traverse.Create(playerMgr).Field("_player").SetValue(player);
                var verify = Traverse.Create(playerMgr).Field("_player").GetValue<Behaviour_Player>();
                CoopPlugin.FileLog($"RewardPatch: _player swap verify: set={player.name}, read-back={verify?.name ?? "NULL"}, match={verify == player}");
            }
            SwapRewardGenerator(rewardsTrav, player);
            SwapBoonSideView(rewardsTrav, player);
            CoopPlugin.FileLog($"RewardPatch: Swapped reward context to {player.name}");
        }
        private static void SwapRewardGenerator(Traverse rewardsTrav, Behaviour_Player player)
        {
            try
            {
                bool isP2 = PlayerRegistry.GetPlayer(1) == player;
                if (P1RewardGenerator == null)
                {
                    P1RewardGenerator = rewardsTrav.Field("_rewardGenerator").GetValue<RewardGenerator>();
                    CoopPlugin.FileLog("RewardPatch: Captured P1's RewardGenerator.");
                }
                if (isP2 && P2RewardGenerator == null)
                {
                    var rewardsInstance = rewardsTrav.Field("_rewardGenerator").GetValue<RewardGenerator>();
                    EnsureP2RewardGenerator(rewardsInstance, player);
                }
                var targetGen = isP2 ? P2RewardGenerator : P1RewardGenerator;
                if (targetGen != null)
                {
                    rewardsTrav.Field("_rewardGenerator").SetValue(targetGen);
                    CoopPlugin.FileLog($"RewardPatch: Swapped RewardGenerator to {(isP2 ? "P2" : "P1")}'s instance (chosenGods={targetGen.ChosenGods.Count()}, godLimit={targetGen.GodLimit})");
                }
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"RewardPatch: SwapRewardGenerator error: {ex.Message}");
            }
        }
        private static void EnsureP2RewardGenerator(RewardGenerator currentGen, Behaviour_Player p2)
        {
            if (P2RewardGenerator != null) return;
            try
            {
                var p1Gen = P1RewardGenerator;
                if (p1Gen == null)
                {
                    CoopPlugin.FileLog("RewardPatch: Cannot create P2 generator — P1 generator not captured yet.");
                    return;
                }
                var rng = NoiseRng.SeedFromUnityRng();
                int upgradesOnLevelUp = Traverse.Create(p1Gen).Field("_upgradesOnLevelUp").GetValue<int>();
                int godLimit = p1Gen.GodLimit;
                var cachedRewards = CachedRewards;
                if (cachedRewards == null)
                {
                    CoopPlugin.FileLog("RewardPatch: CachedRewards is null, cannot create P2 generator.");
                    return;
                }
                var unlockedGods = Traverse.Create(cachedRewards).Method("GetUnlockedGods").GetValue<IEnumerable<IReadOnlyGod>>();
                var godsList = new List<IReadOnlyGod>(unlockedGods);
                P2RewardGenerator = new RewardGenerator(
                    rng,
                    p2.Boons,
                    upgradesOnLevelUp,
                    godsList,
                    Teams.GodWeights,
                    godLimit
                );
                CoopPlugin.FileLog($"RewardPatch: Created P2 RewardGenerator — gods={godsList.Count}, godLimit={godLimit}, upgradesOnLevelUp={upgradesOnLevelUp}");
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"RewardPatch: EnsureP2RewardGenerator error: {ex}");
            }
        }
        private static void SwapBoonSideView(Traverse rewardsTrav, Behaviour_Player player)
        {
            try
            {
                var rewardUi = rewardsTrav.Field("_rewardUi").GetValue();
                if (rewardUi == null)
                {
                    CoopPlugin.FileLog("RewardPatch: _rewardUi is null, cannot swap boon side view.");
                    return;
                }
                var boonListGui = Traverse.Create(rewardUi).Field("_boonListGui").GetValue();
                if (boonListGui == null)
                {
                    CoopPlugin.FileLog("RewardPatch: _boonListGui is null.");
                    return;
                }
                var boonsView = Traverse.Create(boonListGui).Field("_boons").GetValue();
                if (boonsView == null)
                {
                    CoopPlugin.FileLog("RewardPatch: _boons (GUI_BoonSideView) is null.");
                    return;
                }
                Traverse.Create(boonsView).Field("_boonManager").SetValue(player.Boons);
                CoopPlugin.FileLog($"RewardPatch: Boon side view swapped to {player.name}'s BoonManager.");
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"RewardPatch: SwapBoonSideView error: {ex.Message}");
            }
        }
        public static void SetGamepadForBlessingTurn(bool isP2Turn)
        {
            var gp = Gamepad.current;
            if (gp == null) return;
            if (isP2Turn)
            {
                InputSystem.EnableDevice(gp);
                CoopPlugin.FileLog("RewardPatch: Gamepad ENABLED for P2 blessing turn.");
            }
            else
            {
                InputSystem.DisableDevice(gp);
                CoopPlugin.FileLog("RewardPatch: Gamepad DISABLED for P1 blessing turn.");
            }
        }
        public static void LogXpShare(string source, string target, float amount)
        {
            _xpShareLogCounter++;
            if (_xpShareLogCounter <= 5 || _xpShareLogCounter % 50 == 0)
                CoopPlugin.FileLog($"RewardPatch: XP share #{_xpShareLogCounter} {source}->{target} amount={amount:F1}");
        }
        public static void Reset()
        {
            P2PendingRewards = 0;
            IsP2Turn = false;
            SharingXp = false;
            _cachedRewards = null;
            _xpShareLogCounter = 0;
            ActiveRewardPlayer = null;
            P1RewardGenerator = null;
            P2RewardGenerator = null;
            ScreenRewardSelect_OnShow_Patch.Reset();
            CoopFudgeStats.Reset();
        }
    }
    [HarmonyPatch(typeof(Behaviour_XpTracker), "GainRawXp")]
    public static class XpTracker_GainRawXp_Patch
    {
        static void Postfix(Behaviour_XpTracker __instance, float amount)
        {
            if (PlayerRegistry.Count < 2) return;
            if (CoopRewardState.SharingXp) return;
            if (amount <= 0f) return;
            CoopRewardState.SharingXp = true;
            try
            {
                var sourcePlayer = __instance.GetComponent<Behaviour_Player>();
                if (sourcePlayer != null)
                {
                    float shared = amount;
                    for (int i = 0; i < PlayerRegistry.Players.Count; i++)
                    {
                        var p = PlayerRegistry.Players[i];
                        if (p != null && p != sourcePlayer && p.XpTracker != null
                            && p.Entity != null && p.Entity.IsAlive)
                        {
                            CoopRewardState.LogXpShare(sourcePlayer.name, p.name, shared);
                            CoopRewardState.ShareXpToPlayer(p.XpTracker, shared);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"RewardPatch: XP share EXCEPTION: {ex}");
            }
            finally
            {
                CoopRewardState.SharingXp = false;
            }
        }
    }
    [HarmonyPatch(typeof(System_Rewards), "BeginRewards")]
    public static class SystemRewards_BeginRewards_Patch
    {
        static void Postfix()
        {
            if (PlayerRegistry.Count < 2) return;
            if (CoopRewardState.ActiveRewardPlayer == null)
                CoopRewardState.ActiveRewardPlayer = PlayerRegistry.GetPlayer(0);
            CoopRewardState.SetGamepadForBlessingTurn(isP2Turn: CoopRewardState.IsP2Turn);
        }
    }
    [HarmonyPatch(typeof(System_Rewards), "Visit", new System.Type[] { typeof(LevelUpRewards.NewBoon) })]
    public static class SystemRewards_VisitNewBoon_Patch
    {
        static void Prefix(System_Rewards __instance, LevelUpRewards.NewBoon newBoon)
        {
            if (PlayerRegistry.Count < 2) return;
            if (CoopRewardState.ActiveRewardPlayer == null) return;
            var target = CoopRewardState.ActiveRewardPlayer;
            var rewardsTrav = Traverse.Create(__instance);
            var playerMgr = rewardsTrav.Field("_playerManager").GetValue<System_PlayerManager>();
            if (playerMgr == null) return;
            var current = Traverse.Create(playerMgr).Field("_player").GetValue<Behaviour_Player>();
            if (current != target)
            {
                CoopPlugin.FileLog($"RewardPatch VISIT FIX: NewBoon — _player was {current?.name}, forcing to {target.name}");
                Traverse.Create(playerMgr).Field("_player").SetValue(target);
            }
            CoopPlugin.FileLog($"RewardPatch: Visit(NewBoon) boon={newBoon.Boon.Code} -> {target.name}.Boons");
        }
    }
    [HarmonyPatch(typeof(System_Rewards), "Visit", new System.Type[] { typeof(LevelUpRewards.LevelUpgrade) })]
    public static class SystemRewards_VisitLevelUp_Patch
    {
        static void Prefix(System_Rewards __instance, LevelUpRewards.LevelUpgrade upgrade)
        {
            if (PlayerRegistry.Count < 2) return;
            if (CoopRewardState.ActiveRewardPlayer == null) return;
            var target = CoopRewardState.ActiveRewardPlayer;
            var rewardsTrav = Traverse.Create(__instance);
            var playerMgr = rewardsTrav.Field("_playerManager").GetValue<System_PlayerManager>();
            if (playerMgr == null) return;
            var current = Traverse.Create(playerMgr).Field("_player").GetValue<Behaviour_Player>();
            if (current != target)
            {
                CoopPlugin.FileLog($"RewardPatch VISIT FIX: LevelUpgrade — _player was {current?.name}, forcing to {target.name}");
                Traverse.Create(playerMgr).Field("_player").SetValue(target);
            }
            CoopPlugin.FileLog($"RewardPatch: Visit(LevelUpgrade) boon={upgrade.Boon.Code} -> {target.name}.Boons");
        }
    }
    [HarmonyPatch(typeof(System_Rewards), "Visit", new System.Type[] { typeof(LevelUpRewards.RarityUpgrade) })]
    public static class SystemRewards_VisitRarityUp_Patch
    {
        static void Prefix(System_Rewards __instance, LevelUpRewards.RarityUpgrade rarityUpgrade)
        {
            if (PlayerRegistry.Count < 2) return;
            if (CoopRewardState.ActiveRewardPlayer == null) return;
            var target = CoopRewardState.ActiveRewardPlayer;
            var rewardsTrav = Traverse.Create(__instance);
            var playerMgr = rewardsTrav.Field("_playerManager").GetValue<System_PlayerManager>();
            if (playerMgr == null) return;
            var current = Traverse.Create(playerMgr).Field("_player").GetValue<Behaviour_Player>();
            if (current != target)
            {
                CoopPlugin.FileLog($"RewardPatch VISIT FIX: RarityUpgrade — _player was {current?.name}, forcing to {target.name}");
                Traverse.Create(playerMgr).Field("_player").SetValue(target);
            }
            CoopPlugin.FileLog($"RewardPatch: Visit(RarityUpgrade) boon={rarityUpgrade.Boon.Code} -> {target.name}.Boons");
        }
    }
    [HarmonyPatch(typeof(System_Rewards), "EndRewards")]
    public static class SystemRewards_EndRewards_Patch
    {
        static void Postfix(System_Rewards __instance)
        {
            if (PlayerRegistry.Count < 2) return;
            var gp = Gamepad.current;
            if (gp != null && !gp.enabled)
            {
                InputSystem.EnableDevice(gp);
                CoopPlugin.FileLog("RewardPatch: Gamepad re-enabled after blessing turn.");
            }
            if (CoopRewardState.IsP2Turn)
            {
                CoopPlugin.FileLog("RewardPatch: P2 blessings done. Swapping back to P1.");
                CoopRewardState.IsP2Turn = false;
                CoopRewardState.SwapToPlayer(__instance, PlayerRegistry.GetPlayer(0));
                return;
            }
            if (CoopRewardState.P2PendingRewards > 0)
            {
                int count = CoopRewardState.P2PendingRewards;
                CoopRewardState.P2PendingRewards = 0;
                CoopRewardState.IsP2Turn = true;
                var p2 = PlayerRegistry.GetPlayer(1);
                if (p2 != null && p2.Entity != null && p2.Entity.IsAlive)
                {
                    CoopPlugin.FileLog($"RewardPatch: P1 done. Starting P2 turn ({count} blessings).");
                    CoopRewardState.SwapToPlayer(__instance, p2);
                    __instance.GiveFreeLevels(count);
                }
                else
                {
                    CoopPlugin.FileLog("RewardPatch: P2 dead, skipping blessings.");
                    CoopRewardState.IsP2Turn = false;
                }
            }
        }
    }
    [HarmonyPatch(typeof(System_Rewards), "GiveFreeLevels")]
    public static class SystemRewards_GiveFreeLevels_Patch
    {
        static void Prefix(System_Rewards __instance, int count)
        {
            if (PlayerRegistry.Count < 2) return;
            var trav = Traverse.Create(__instance);
            var playerMgr = trav.Field("_playerManager").GetValue<System_PlayerManager>();
            string playerName = "?";
            if (playerMgr != null)
            {
                var p = Traverse.Create(playerMgr).Field("_player").GetValue<Behaviour_Player>();
                if (p != null) playerName = p.name;
            }
            int remaining = trav.Field("_remainingRewards").GetValue<int>();
            bool giving = trav.Field("_givingBoonRewards").GetValue<bool>();
            CoopPlugin.FileLog($"RewardPatch DIAG: GiveFreeLevels({count}) context={playerName}, remaining={remaining}, givingBoons={giving}, IsP2Turn={CoopRewardState.IsP2Turn}");
        }
    }
    [HarmonyPatch(typeof(Screen_RewardSelect), "OnShow")]
    public static class ScreenRewardSelect_OnShow_Patch
    {
        private static GameObject _indicatorObj;
        private static TextMeshProUGUI _indicatorTmp;
        static void Postfix(Screen_RewardSelect __instance)
        {
            if (PlayerRegistry.Count < 2) return;
            try
            {
                int playerIndex = CoopRewardState.IsP2Turn ? 1 : 0;
                var player = PlayerRegistry.GetPlayer(playerIndex);
                string charName = "???";
                if (player != null && player.Data != null)
                    charName = player.Data.Code.ToString();
                string label = $"Player {playerIndex + 1} - {charName}";
                if (_indicatorTmp != null && _indicatorObj != null)
                {
                    _indicatorTmp.text = label;
                    _indicatorObj.SetActive(true);
                    CoopPlugin.FileLog($"RewardPatch: Blessing indicator updated: {label}");
                    return;
                }
                var boonListGui = Traverse.Create(__instance).Field("_boonListGui").GetValue<MonoBehaviour>();
                if (boonListGui == null)
                {
                    CoopPlugin.FileLog("RewardPatch: _boonListGui is null, cannot create indicator.");
                    return;
                }
                TextMeshProUGUI existingTmp = null;
                foreach (var tmp in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
                {
                    if (tmp.font != null)
                    {
                        existingTmp = tmp;
                        break;
                    }
                }
                if (existingTmp == null)
                {
                    CoopPlugin.FileLog("RewardPatch: No TMP found to clone font from.");
                    return;
                }
                _indicatorObj = new GameObject("CoopPlayerIndicator");
                _indicatorObj.transform.SetParent(boonListGui.transform, false);
                _indicatorTmp = _indicatorObj.AddComponent<TextMeshProUGUI>();
                _indicatorTmp.font = existingTmp.font;
                _indicatorTmp.fontSize = 28f;
                _indicatorTmp.alignment = TextAlignmentOptions.Center;
                _indicatorTmp.color = new Color(1f, 0.85f, 0.4f, 1f); 
                _indicatorTmp.text = label;
                _indicatorTmp.enableWordWrapping = false;
                _indicatorTmp.overflowMode = TextOverflowModes.Overflow;
                var rect = _indicatorObj.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(0f, 10f);
                rect.sizeDelta = new Vector2(0f, 40f);
                CoopPlugin.FileLog($"RewardPatch: Blessing indicator created: {label}");
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"RewardPatch: Indicator error: {ex.Message}");
            }
        }
        public static void Reset()
        {
            _indicatorObj = null;
            _indicatorTmp = null;
        }
    }
    [HarmonyPatch(typeof(Behaviour_XpTracker), "GainRawXp")]
    public static class XpTracker_GainRawXp_DiagPatch
    {
        private static int _logCounter;
        private static System.Collections.Generic.Dictionary<int, int> _prevLevels = new System.Collections.Generic.Dictionary<int, int>();
        static void Prefix(Behaviour_XpTracker __instance, float amount)
        {
            if (PlayerRegistry.Count < 2) return;
            if (amount <= 0f) return;
            int id = __instance.GetInstanceID();
            if (!_prevLevels.ContainsKey(id))
                _prevLevels[id] = __instance.CurLevel;
        }
        static void Postfix(Behaviour_XpTracker __instance, float amount)
        {
            if (PlayerRegistry.Count < 2) return;
            if (amount <= 0f) return;
            int id = __instance.GetInstanceID();
            int prevLevel = _prevLevels.ContainsKey(id) ? _prevLevels[id] : -1;
            int curLevel = __instance.CurLevel;
            if (curLevel != prevLevel && prevLevel >= 0)
            {
                var player = __instance.GetComponent<Behaviour_Player>();
                string name = player != null ? player.name : "unknown";
                bool isPrimary = player != null && Traverse.Create(player).Field("_isPrimaryPlayerInstance").GetValue<bool>();
                CoopPlugin.FileLog($"RewardPatch LEVELUP: {name} (primary={isPrimary}) {prevLevel}->{curLevel}, curXp={__instance.CurXp:F1}, xpForNext={__instance.XpForNextLevel:F1}, sharing={CoopRewardState.SharingXp}");
            }
            _prevLevels[id] = curLevel;
            _logCounter++;
            if (_logCounter <= 20)
            {
                var player = __instance.GetComponent<Behaviour_Player>();
                string name = player != null ? player.name : "unknown";
                bool isPrimary = player != null && Traverse.Create(player).Field("_isPrimaryPlayerInstance").GetValue<bool>();
                CoopPlugin.FileLog($"RewardPatch DIAG: GainRawXp on {name} (primary={isPrimary}), amount={amount:F2}, curXp={__instance.CurXp:F1}, xpForNext={__instance.XpForNextLevel:F1}, level={curLevel}, sharing={CoopRewardState.SharingXp}");
            }
        }
    }
}