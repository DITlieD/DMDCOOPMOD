using Death.App;
using Death.Achievements;
using Death.Unlockables;
using Death.Data;
using Death.Items;
using Death.Run.Core;
using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
namespace DeathMustDieCoop
{
    public static class CoopP2Profile
    {
        public static Profile Instance { get; private set; }
        public static void Create(Profile p1Profile)
        {
            if (Instance != null)
            {
                CoopPlugin.FileLog("CoopP2Profile: Already exists, skipping creation.");
                return;
            }
            try
            {
                Instance = new Profile(p1Profile.Achievements, p1Profile.Unlocks);
                string p2Char = CoopP2Save.Data.SelectedCharacterCode;
                if (string.IsNullOrEmpty(p2Char))
                {
                    p2Char = "Warrior";
                    CoopP2Save.Data.SelectedCharacterCode = p2Char;
                    CoopP2Save.MarkDirty();
                }
                Instance.Progression.SelectedCharacterCode = p2Char;
                CoopPlugin.FileLog($"CoopP2Profile: Set character to {p2Char}");
                Instance.Gold = CoopP2Save.Data.Gold > 0 ? CoopP2Save.Data.Gold : 1000;
                if (!string.IsNullOrEmpty(CoopP2Save.Data.TalentsJson))
                {
                    Instance.TalentsState.LoadStateFromJson(CoopP2Save.Data.TalentsJson);
                    CoopPlugin.FileLog("CoopP2Profile: Loaded talents from save.");
                }
                if (!string.IsNullOrEmpty(CoopP2Save.Data.PlayerItemRepoJson))
                {
                    try
                    {
                        var repoState = JsonUtility.FromJson<ItemRepository.SaveState>(
                            CoopP2Save.Data.PlayerItemRepoJson);
                        if (repoState != null)
                        {
                            Instance.PlayerItemRepo.LoadState(repoState);
                            CoopPlugin.FileLog("CoopP2Profile: Loaded PlayerItemRepo from save.");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        CoopPlugin.FileLog($"CoopP2Profile: PlayerItemRepo load failed (non-fatal): {ex.Message}");
                    }
                }
                if (!string.IsNullOrEmpty(CoopP2Save.Data.ShopItemRepoJson))
                {
                    try
                    {
                        var repoState = JsonUtility.FromJson<ItemRepository.SaveState>(
                            CoopP2Save.Data.ShopItemRepoJson);
                        if (repoState != null)
                        {
                            Instance.ShopItemRepo.LoadState(repoState);
                            CoopPlugin.FileLog("CoopP2Profile: Loaded ShopItemRepo from save.");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        CoopPlugin.FileLog($"CoopP2Profile: ShopItemRepo load failed (non-fatal): {ex.Message}");
                    }
                }
                if (!string.IsNullOrEmpty(CoopP2Save.Data.EquipmentJson))
                {
                    try
                    {
                        string[] lines = CoopP2Save.Data.EquipmentJson.Split(
                            new[] { "\n" }, System.StringSplitOptions.RemoveEmptyEntries);
                        int loaded = 0;
                        foreach (string line in lines)
                        {
                            int sepIdx = line.IndexOf("|||");
                            if (sepIdx < 0) continue;
                            string charCodeStr = line.Substring(0, sepIdx);
                            string json = line.Substring(sepIdx + 3);
                            if (string.IsNullOrEmpty(charCodeStr) || string.IsNullOrEmpty(json))
                                continue;
                            try
                            {
                                var charCode = CharacterCode.FromString(charCodeStr);
                                var loadouts = Instance.GetLoadoutsFor(charCode);
                                if (loadouts != null)
                                {
                                    loadouts.LoadStateFromJson(json,
                                        Instance.PlayerItemRepo, null);
                                    loaded++;
                                }
                            }
                            catch (System.Exception charEx)
                            {
                                CoopPlugin.FileLog($"CoopP2Profile: Equipment load for {charCodeStr} failed: {charEx.Message}");
                            }
                        }
                        CoopPlugin.FileLog($"CoopP2Profile: Loaded equipment from save ({loaded}/{lines.Length} characters).");
                    }
                    catch (System.Exception ex)
                    {
                        CoopPlugin.FileLog($"CoopP2Profile: Equipment load failed (non-fatal): {ex.Message}");
                    }
                }
                if (!string.IsNullOrEmpty(CoopP2Save.Data.BackpackJson))
                {
                    try
                    {
                        var backpackState = JsonUtility.FromJson<ItemGrid.SaveState>(
                            CoopP2Save.Data.BackpackJson);
                        if (backpackState != null && backpackState.IsValid)
                        {
                            Instance.Backpack.LoadState(backpackState);
                            CoopPlugin.FileLog("CoopP2Profile: Loaded backpack from save.");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        CoopPlugin.FileLog($"CoopP2Profile: Backpack load failed (non-fatal): {ex.Message}");
                    }
                }
                if (!string.IsNullOrEmpty(CoopP2Save.Data.ShopJson))
                {
                    Instance.ShopData.LoadStateFromJson(CoopP2Save.Data.ShopJson);
                    CoopPlugin.FileLog("CoopP2Profile: Loaded shop from save.");
                }
                if (Instance.ShopData.Stock.CheckIsEmpty())
                {
                    try
                    {
                        DeathMustDieCoop.Patches.CoopShopHelper.RegenP2ShopWithP1Stats();
                        CoopPlugin.FileLog($"CoopP2Profile: Generated starter shop stock ({Instance.ShopData.Stock.SlotCount} slots).");
                    }
                    catch (System.Exception shopEx)
                    {
                        CoopPlugin.FileLog($"CoopP2Profile: Shop generation failed (non-fatal): {shopEx.Message}");
                    }
                }
                DeathMustDieCoop.Patches.HubScreenUtils.SyncLoadoutUnlocks(p1Profile, Instance);
                CoopPlugin.FileLog($"CoopP2Profile: Created. Gold={Instance.Gold}");
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"CoopP2Profile: FAILED to create: {ex}");
                Instance = null;
            }
        }
        public static void SaveToCoopData()
        {
            if (Instance == null) return;
            try
            {
                CoopP2Save.Data.Gold = Instance.Gold;
                CoopP2Save.Data.TalentsJson = Instance.TalentsState.SerializeStateToJson();
                CoopP2Save.Data.ShopJson = Instance.ShopData.SerializeStateToJson();
                CoopP2Save.Data.PlayerItemRepoJson = JsonUtility.ToJson(
                    Instance.PlayerItemRepo.GenerateSaveState());
                CoopP2Save.Data.ShopItemRepoJson = JsonUtility.ToJson(
                    Instance.ShopItemRepo.GenerateSaveState());
                int backpackItems = 0;
                for (int i = 0; i < Instance.Backpack.SlotCount; i++)
                {
                    if (!Instance.Backpack.GetSlot(i % 5, i / 5).IsEmpty)
                        backpackItems++;
                }
                CoopP2Save.Data.BackpackJson = JsonUtility.ToJson(
                    Instance.Backpack.GenerateSaveState());
                SaveEquipment();
                CoopP2Save.MarkDirty();
                int repoCount = 0;
                try
                {
                    var entries = Traverse.Create(Instance.PlayerItemRepo).Field("_idToEntry").GetValue<object>();
                    if (entries != null)
                    {
                        var countProp = entries.GetType().GetProperty("Count");
                        if (countProp != null)
                            repoCount = (int)countProp.GetValue(entries);
                    }
                }
                catch { }
                CoopPlugin.FileLog($"CoopP2Profile: Saved state. Gold={Instance.Gold}, backpackItems={backpackItems}, repoItems={repoCount}, equipJson={CoopP2Save.Data.EquipmentJson?.Length ?? 0}chars");
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"CoopP2Profile: Save error: {ex.Message}");
            }
        }
        private const string EQUIP_FIELD_SEP = "|||";
        private const string EQUIP_ENTRY_SEP = "\n";
        private static void SaveEquipment()
        {
            try
            {
                var profileType = Instance.GetType();
                var field = profileType.GetField("_characterLoadouts",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field == null)
                {
                    CoopPlugin.FileLog("CoopP2Profile: _characterLoadouts field not found!");
                    return;
                }
                var dict = field.GetValue(Instance) as Dictionary<CharacterCode, CharacterLoadouts>;
                if (dict == null)
                {
                    CoopPlugin.FileLog("CoopP2Profile: _characterLoadouts is null or cast failed!");
                    return;
                }
                var lines = new List<string>();
                foreach (var kvp in dict)
                {
                    string charCode = kvp.Key.ToString();
                    string json = kvp.Value.SerializeStateToJson(Instance.PlayerItemRepo);
                    lines.Add(charCode + EQUIP_FIELD_SEP + json);
                }
                CoopP2Save.Data.EquipmentJson = string.Join(EQUIP_ENTRY_SEP, lines.ToArray());
                string selChar = Instance.Progression.SelectedCharacterCode;
                if (!string.IsNullOrEmpty(selChar) && dict.TryGetValue(
                    CharacterCode.FromString(selChar), out var selLoadouts))
                {
                    int filled = 0, total = 0;
                    foreach (var slot in selLoadouts.GetSelectedLoadout())
                    {
                        total++;
                        if (!slot.IsEmpty) filled++;
                    }
                    CoopPlugin.FileLog($"CoopP2Profile: SaveEquipment — {selChar}: {filled}/{total} slots, {dict.Count} chars, equipJson={CoopP2Save.Data.EquipmentJson.Length}chars");
                }
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"CoopP2Profile: Equipment save error: {ex.Message}\n{ex.StackTrace}");
            }
        }
        public static void Cleanup()
        {
            if (Instance != null)
            {
                SaveToCoopData();
                CoopPlugin.FileLog("CoopP2Profile: Saved (instance kept alive for run).");
            }
        }
    }
}