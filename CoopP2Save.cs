using System;
using System.IO;
using UnityEngine;
namespace DeathMustDieCoop
{
    [Serializable]
    public class CoopInventoryEntry
    {
        public string CharacterCode = "";
        public string Json = "";
    }
    [Serializable]
    public class CoopInventoryData
    {
        public CoopInventoryEntry[] Entries;
    }
    [Serializable]
    public class CoopP2SaveData
    {
        public string SelectedCharacterCode = "Warrior";
        public string SelectedActCode = "";
        public int Gold;
        public string TalentsJson = "";
        public string EquipmentJson = "";      
        public string BackpackJson = "";        
        public string PlayerItemRepoJson = "";  
        public string ShopJson = "";
        public string ShopItemRepoJson = "";    
    }
    public static class CoopP2Save
    {
        private static string _savePath;
        private static CoopP2SaveData _data;
        private static bool _dirty;
        public static CoopP2SaveData Data
        {
            get
            {
                if (_data == null) Load();
                return _data;
            }
        }
        public static string SavePath
        {
            get
            {
                if (_savePath == null)
                    _savePath = Path.Combine(CoopPlugin.ModDir, "coop_p2_save.json");
                return _savePath;
            }
        }
        public static void Load()
        {
            try
            {
                if (File.Exists(SavePath))
                {
                    string json = File.ReadAllText(SavePath);
                    _data = JsonUtility.FromJson<CoopP2SaveData>(json);
                    CoopPlugin.FileLog($"CoopP2Save: Loaded from {SavePath}");
                    CoopPlugin.FileLog($"  Character={_data.SelectedCharacterCode}, Gold={_data.Gold}");
                }
                else
                {
                    _data = new CoopP2SaveData();
                    CoopPlugin.FileLog("CoopP2Save: No save file found, created fresh data.");
                }
            }
            catch (Exception ex)
            {
                CoopPlugin.FileLog($"CoopP2Save: Load failed: {ex.Message}");
                _data = new CoopP2SaveData();
            }
            _dirty = false;
        }
        public static void Save()
        {
            if (_data == null) return;
            try
            {
                string json = JsonUtility.ToJson(_data, true);
                File.WriteAllText(SavePath, json);
                _dirty = false;
                CoopPlugin.FileLog($"CoopP2Save: Saved to {SavePath}");
            }
            catch (Exception ex)
            {
                CoopPlugin.FileLog($"CoopP2Save: Save failed: {ex.Message}");
            }
        }
        public static void MarkDirty()
        {
            _dirty = true;
        }
        public static bool IsDirty => _dirty;
        public static void SaveIfDirty()
        {
            if (_dirty) Save();
        }
        public static void Reset()
        {
            _data = new CoopP2SaveData();
            _dirty = true;
            CoopPlugin.FileLog("CoopP2Save: Data reset.");
        }
    }
}