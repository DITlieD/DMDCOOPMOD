using HarmonyLib;
using Death.Run.Behaviours;
using Death.Run.Behaviours.Entities;
using Death.Run.Behaviours.Events;
using Death.Run.Behaviours.Players;
using Death.Run.Core;
using Death.Run.Core.Abilities;
using Death.Run.Core.Entities;
using Death.Run.Systems;
using Death.Run.UserInterface.Results;
using Death.Data;
using Death.ResourceManagement;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
namespace DeathMustDieCoop.Patches
{
    public static class CoopP2Analytics
    {
        public static readonly Dictionary<string, float> DamagePerSource = new Dictionary<string, float>();
        public static readonly Dictionary<string, float> TimeBoonGained = new Dictionary<string, float>();
        public static float TotalTime;
        public static void Reset()
        {
            DamagePerSource.Clear();
            TimeBoonGained.Clear();
            TotalTime = 0f;
        }
        public static void TrackDamage(string identifier, float amount)
        {
            if (DamagePerSource.ContainsKey(identifier))
                DamagePerSource[identifier] += amount;
            else
                DamagePerSource.Add(identifier, amount);
        }
        public static RunAnalytics GenerateForP2(RunAnalytics p1Analytics)
        {
            var p2 = PlayerRegistry.GetPlayer(1);
            if (p2 == null) return p1Analytics;
            var entries = new List<RunAnalytics.Ability>();
            var charData = p2.Data;
            DamagePerSource.TryGetValue("PrimaryAttack", out float primaryDmg);
            entries.Add(new RunAnalytics.Ability
            {
                Code = charData.StartingWeaponCode,
                Type = RunAnalytics.AbilityType.Boon,
                TotalDamage = primaryDmg,
                TimeGained = 0f,
                Dps = CalcDps(primaryDmg, 0f),
                UpgradeLevel = 1,
                Rarity = BoonRarity.Novice
            });
            foreach (var boon in p2.Boons)
            {
                if (DamagePerSource.TryGetValue(boon.Code, out float dmg) && dmg > 0f)
                {
                    TimeBoonGained.TryGetValue(boon.Code, out float timeGained);
                    entries.Add(new RunAnalytics.Ability
                    {
                        Code = boon.Code,
                        Type = RunAnalytics.AbilityType.Boon,
                        TotalDamage = dmg,
                        TimeGained = timeGained,
                        Dps = CalcDps(dmg, timeGained),
                        UpgradeLevel = boon.UpgradeLevel,
                        Rarity = boon.Base.Rarity
                    });
                }
            }
            foreach (var talent in p2.Talents)
            {
                if (DamagePerSource.TryGetValue(talent.Code, out float dmg) && dmg > 0f)
                {
                    entries.Add(new RunAnalytics.Ability
                    {
                        Code = talent.Code,
                        Type = RunAnalytics.AbilityType.Talent,
                        TotalDamage = dmg,
                        Dps = CalcDps(dmg, 0f)
                    });
                }
            }
            var seenAffixes = new HashSet<string>();
            foreach (var item in p2.EquippedItems)
            {
                foreach (var affix in item.Affixes)
                {
                    string code = affix.Code.ToString();
                    if (DamagePerSource.TryGetValue(code, out float dmg) && dmg > 0f && !seenAffixes.Contains(code))
                    {
                        entries.Add(new RunAnalytics.Ability
                        {
                            Code = code,
                            Type = RunAnalytics.AbilityType.Item,
                            TotalDamage = dmg,
                            Dps = CalcDps(dmg, 0f)
                        });
                        seenAffixes.Add(code);
                    }
                }
            }
            entries.Sort((a, b) =>
            {
                int c = b.Dps.CompareTo(a.Dps);
                if (c == 0) c = b.TotalDamage.CompareTo(a.TotalDamage);
                if (c == 0) c = b.Rarity.CompareTo(a.Rarity);
                return c;
            });
            float totalDmg = 0f, totalDps = 0f;
            foreach (var e in entries) { totalDmg += e.TotalDamage; totalDps += e.Dps; }
            entries.Add(new RunAnalytics.Ability
            {
                Code = "analytics_aggregate",
                Type = RunAnalytics.AbilityType.Aggregate,
                TotalDamage = totalDmg,
                Dps = totalDps
            });
            var p2Analytics = new RunAnalytics
            {
                Outcome = p1Analytics.Outcome,
                RunTime = p1Analytics.RunTime,
                OverTime = p1Analytics.OverTime,
                TotalTime = p1Analytics.TotalTime,
                ReachedEndOfTimer = p1Analytics.ReachedEndOfTimer,
                LevelReached = p2.XpTracker != null ? p2.XpTracker.CurLevel : p1Analytics.LevelReached,
                EnemiesKilled = p1Analytics.EnemiesKilled,
                GoldCollected = p1Analytics.GoldCollected,
                XpGained = p1Analytics.XpGained,
                SuccessfulRuns = p1Analytics.SuccessfulRuns,
                CurrentStreak = p1Analytics.CurrentStreak,
                BestStreak = p1Analytics.BestStreak,
                AbilityDamageEntries = entries,
                Boons = p2.Boons.Where(b => !b.IsTemporary).ToList()
            };
            CoopPlugin.FileLog($"ResultsPatch: Generated P2 analytics — {entries.Count} ability entries, {p2Analytics.Boons.Count()} boons");
            return p2Analytics;
        }
        private static float CalcDps(float damage, float timeGained)
        {
            return DamageRules.CalculateDps(damage, TotalTime - timeGained);
        }
    }
    [HarmonyPatch(typeof(System_Analytics), "OnTookDamage")]
    public static class Analytics_OnTookDamage_P2Split
    {
        static void Postfix(object __instance, Event_TookDamage ev)
        {
            if (PlayerRegistry.Count < 2) return;
            if (ev.Dealer == null) return;
            if (ev.Entity.Type != EntityType.Monster || ev.Entity.TeamId != TeamId.Monsters) return;
            var damageSource = ev.Dealer.DamageSource;
            if (damageSource == null || !Teams.IsPlayersAlly(damageSource.TeamId)) return;
            float amount = ev.Damage.Amount - ev.ExcessDamage;
            if (float.IsNaN(amount) || amount <= 0f) return;
            string identifier = damageSource.DamageIdentifier;
            Entity dealerEntity = null;
            if (ev.Dealer is IAbility ability)
                dealerEntity = ability.Entity;
            else if (damageSource is IAbility abilitySource)
                dealerEntity = abilitySource.Entity;
            if (dealerEntity == null) return;
            var p2 = PlayerRegistry.GetPlayer(1);
            if (p2 == null || p2.Entity == null) return;
            if (dealerEntity == p2.Entity)
            {
                CoopP2Analytics.TrackDamage(identifier, amount);
                var mainDict = Traverse.Create(__instance)
                    .Field("_damagePerSource")
                    .GetValue<Dictionary<string, float>>();
                if (mainDict != null && mainDict.ContainsKey(identifier))
                {
                    mainDict[identifier] -= amount;
                    if (mainDict[identifier] <= 0f)
                        mainDict.Remove(identifier);
                }
            }
        }
    }
    [HarmonyPatch(typeof(System_Analytics), "OnBoonGained")]
    public static class Analytics_OnBoonGained_P2
    {
        static void Postfix(Event_BoonGained ev)
        {
            if (PlayerRegistry.Count < 2) return;
            if (CoopRewardState.IsP2Turn)
            {
                if (!CoopP2Analytics.TimeBoonGained.ContainsKey(ev.Boon.Code))
                {
                    var timeTracker = Object.FindObjectOfType<System_TimeTracker>();
                    float time = timeTracker != null
                        ? Traverse.Create(timeTracker).Property("TotalTimeSec").GetValue<float>()
                        : 0f;
                    CoopP2Analytics.TimeBoonGained.Add(ev.Boon.Code, time);
                }
            }
        }
    }
    [HarmonyPatch(typeof(System_Analytics), "OnPostInit")]
    public static class Analytics_OnPostInit_Reset
    {
        static void Postfix()
        {
            CoopP2Analytics.Reset();
            CoopPlugin.FileLog("ResultsPatch: P2 analytics reset for new run.");
        }
    }
    [HarmonyPatch(typeof(System_Analytics), "GenerateFinal")]
    public static class Analytics_GenerateFinal_CaptureTime
    {
        static void Prefix(object __instance)
        {
            if (PlayerRegistry.Count < 2) return;
            try
            {
                var timeTracker = Traverse.Create(__instance)
                    .Field("_timeTracker").GetValue<System_TimeTracker>();
                if (timeTracker != null)
                    CoopP2Analytics.TotalTime = Traverse.Create(timeTracker)
                        .Property("TotalTimeSec").GetValue<float>();
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"ResultsPatch: Failed to capture TotalTime: {ex.Message}");
            }
        }
    }
    [HarmonyPatch(typeof(GUI_Results), "ShowResultsAsync")]
    public static class GUIResults_ShowResults_Patch
    {
        public static RunAnalytics P1Analytics;
        public static RunOptions StoredRunOptions;
        public static bool ShowingP2;
        static void Prefix(RunAnalytics results, RunOptions runOptions)
        {
            if (PlayerRegistry.Count < 2) return;
            P1Analytics = results;
            StoredRunOptions = runOptions;
            ShowingP2 = false;
            CoopPlugin.FileLog("ResultsPatch: Stored P1 analytics for two-page results.");
        }
    }
    [HarmonyPatch(typeof(GUI_Results), "Complete")]
    public static class GUIResults_Complete_Patch
    {
        private static FieldInfo _summaryTextField;
        private static FieldInfo _damageTableField;
        private static FieldInfo _boonsField;
        private static FieldInfo _portraitField;
        private static FieldInfo _enemyKillsField;
        private static FieldInfo _levelReachedField;
        private static FieldInfo _goldEarnedField;
        private static FieldInfo _timeTextField;
        private static FieldInfo _timeOpenedField;
        private static FieldInfo _totalClearsField;
        private static FieldInfo _currentStreakField;
        private static FieldInfo _bestStreakField;
        private static MethodInfo _setInfoMethod;
        static bool Prefix(GUI_Results __instance)
        {
            if (PlayerRegistry.Count < 2) return true;
            if (GUIResults_ShowResults_Patch.P1Analytics == null) return true;
            if (!GUIResults_ShowResults_Patch.ShowingP2)
            {
                GUIResults_ShowResults_Patch.ShowingP2 = true;
                SwapToP2(__instance);
                return false; 
            }
            else
            {
                GUIResults_ShowResults_Patch.ShowingP2 = false;
                GUIResults_ShowResults_Patch.P1Analytics = null;
                return true;
            }
        }
        private static void SwapToP2(GUI_Results gui)
        {
            try
            {
                EnsureFields();
                var p1Analytics = GUIResults_ShowResults_Patch.P1Analytics;
                var runOptions = GUIResults_ShowResults_Patch.StoredRunOptions;
                var p2Analytics = CoopP2Analytics.GenerateForP2(p1Analytics);
                _setInfoMethod.Invoke(gui, new object[] { p2Analytics, runOptions });
                var p2 = PlayerRegistry.GetPlayer(1);
                if (p2 != null)
                {
                    var portrait = (Image)_portraitField.GetValue(gui);
                    if (portrait != null)
                    {
                        try
                        {
                            var sprite = ResourceManager.Load<Sprite>(p2.Data.PortraitPath);
                            if (sprite != null)
                                portrait.sprite = sprite;
                        }
                        catch (System.Exception ex)
                        {
                            CoopPlugin.FileLog($"ResultsPatch: P2 portrait load failed: {ex.Message}");
                        }
                    }
                }
                var summaryText = _summaryTextField.GetValue(gui);
                if (summaryText != null)
                {
                    var tmp = summaryText as TMPro.TextMeshProUGUI;
                    if (tmp != null)
                        tmp.text = "PLAYER 2 — " + tmp.text;
                }
                _timeOpenedField.SetValue(gui, Time.unscaledTime);
                CoopPlugin.FileLog("ResultsPatch: Swapped to P2 results page.");
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"ResultsPatch: SwapToP2 FAILED: {ex}");
                GUIResults_ShowResults_Patch.ShowingP2 = false;
                GUIResults_ShowResults_Patch.P1Analytics = null;
            }
        }
        private static void EnsureFields()
        {
            if (_setInfoMethod != null) return;
            var t = typeof(GUI_Results);
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            _summaryTextField = t.GetField("_summaryText", flags);
            _damageTableField = t.GetField("_damageTable", flags);
            _boonsField = t.GetField("_boons", flags);
            _portraitField = t.GetField("_portrait", flags);
            _enemyKillsField = t.GetField("_enemyKillsText", flags);
            _levelReachedField = t.GetField("_levelReachedText", flags);
            _goldEarnedField = t.GetField("_goldEarnedText", flags);
            _timeTextField = t.GetField("_timeText", flags);
            _timeOpenedField = t.GetField("_timeOpened", flags);
            _totalClearsField = t.GetField("_totalClearsText", flags);
            _currentStreakField = t.GetField("_currentStreakText", flags);
            _bestStreakField = t.GetField("_bestStreakText", flags);
            _setInfoMethod = t.GetMethod("SetInformation", flags);
            CoopPlugin.FileLog($"ResultsPatch: Reflection resolved — SetInformation={_setInfoMethod != null}, summary={_summaryTextField != null}, portrait={_portraitField != null}, timeOpened={_timeOpenedField != null}");
        }
    }
    [HarmonyPatch(typeof(GUI_Results), "SetInformation")]
    public static class GUIResults_SetInfo_P1Label
    {
        static void Postfix(GUI_Results __instance)
        {
            if (PlayerRegistry.Count < 2) return;
            if (GUIResults_ShowResults_Patch.ShowingP2) return;
            try
            {
                var field = typeof(GUI_Results).GetField("_summaryText",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    var tmp = field.GetValue(__instance) as TMPro.TextMeshProUGUI;
                    if (tmp != null)
                        tmp.text = "PLAYER 1 — " + tmp.text;
                }
            }
            catch { }
        }
    }
}