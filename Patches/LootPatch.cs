using HarmonyLib;
using Death.App;
using Death.Items;
using Death.Data;
using Death.Run.Core;
namespace DeathMustDieCoop.Patches
{
    [HarmonyPatch(typeof(Profile), "GenerateItemContext")]
    public static class CoopLootContext_Patch
    {
        private static bool _logged;
        static void Postfix(ItemGenerator.Context __result)
        {
            if (PlayerRegistry.Count < 2) return;
            CharacterCode p1Char = CharacterCode.None;
            CharacterCode p2Char = CharacterCode.None;
            foreach (var p in PlayerRegistry.Players)
            {
                if (p == null) continue;
                var data = Traverse.Create(p).Field("_data").GetValue<CharacterData>();
                if (data == null) continue;
                bool isPrimary = Traverse.Create(p).Field("_isPrimaryPlayerInstance").GetValue<bool>();
                if (isPrimary)
                    p1Char = data.Code;
                else
                    p2Char = data.Code;
            }
            if (p1Char == CharacterCode.None || p2Char == CharacterCode.None)
                return;
            try
            {
                __result.AllowedCharacters.Clear();
                __result.AllowedItemClasses.Clear();
                __result.AddCharacter(Database.Characters.Get(p1Char));
                __result.AddCharacter(Database.Characters.Get(p2Char));
                if (!_logged)
                {
                    _logged = true;
                    CoopPlugin.FileLog($"LootPatch: context restricted to {p1Char}+{p2Char} " +
                        $"(chars={__result.AllowedCharacters.Count}, classes={__result.AllowedItemClasses.Count})");
                }
            }
            catch (System.Exception ex)
            {
                CoopPlugin.FileLog($"LootPatch: error — {ex.Message}");
            }
        }
        public static void Reset()
        {
            _logged = false;
        }
    }
}