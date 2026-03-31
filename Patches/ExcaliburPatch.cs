using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Claw.Core;
using Claw.Core.Types;
using Death;
using Death.App;
using Death.Data;
using Death.Items;
using Death.Run.Core;
using Death.Run.Core.Abilities;
using Death.Run.Core.Statuses;
using Death.Run.Behaviours;
using Death.Run.Behaviours.Players;
using Death.Run.Behaviours.Entities;
using Death.Run.Behaviours.Events;
using Death.Run.Behaviours.Abilities;
using TMPro;
namespace DeathMustDieCoop
{
    public static class ExcaliburPatch
    {
        private const string EXCALIBUR_CODE = "Excalibur";
        private const string EXCALIBUR_SUBTYPE = "Excalibur_subtype";
        private const string WARRIOR_CODE = "Warrior";
        private const string LADY_BOSS_CODE = "LadyB";
        private static bool _hasExcaliburEquipped = false;
        private static HashSet<Behaviour_Player> _excaliburPlayers = new HashSet<Behaviour_Player>(
            new UnityObjectComparer<Behaviour_Player>());
        private class UnityObjectComparer<T> : IEqualityComparer<T> where T : UnityEngine.Object
        {
            public bool Equals(T x, T y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x == null || y == null) return false;
                return x.GetInstanceID() == y.GetInstanceID();
            }
            public int GetHashCode(T obj)
            {
                if (obj == null) return 0;
                return obj.GetInstanceID();
            }
        }
        private static bool _abilitySetupDone = false;
        private static bool _dataFixupDone = false;
        private static Dictionary<Behaviour_Player, List<SpriteRenderer>> _tintedRenderers =
            new Dictionary<Behaviour_Player, List<SpriteRenderer>>();
        private static readonly Color SABER_BLONDE = new Color(0.95f, 0.85f, 0.45f, 1f);
        private static void Log(string msg)
        {
            CoopPlugin.FileLog($"[Excalibur] {msg}");
        }
        private static void FixItemClass(object item)
        {
            if (item == null) return;
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var classField = item.GetType().GetField("<Class>k__BackingField", flags);
            if (classField != null)
            {
                var curClass = classField.GetValue(item);
                if (curClass != null && curClass.ToString() != "w2hs")
                {
                    classField.SetValue(item, ItemClass.FromCode("w2hs"));
                    Log("FixItemClass: Fixed Class to w2hs (safety net)");
                }
            }
            var subtypeField = item.GetType().GetField("<SubtypeCode>k__BackingField", flags);
            if (subtypeField != null)
            {
                string curSubtype = (string)subtypeField.GetValue(item);
                if (curSubtype != "HackAndSlash_subtype")
                {
                    subtypeField.SetValue(item, "HackAndSlash_subtype");
                    Log("FixItemClass: Fixed SubtypeCode to HackAndSlash_subtype (safety net)");
                }
            }
        }
        public static void DebugGiveExcalibur(Behaviour_Player targetPlayer = null)
        {
            try
            {
                PatchExcaliburItemData.FixupExcaliburData();
                UniqueItemTemplate excalTemplate;
                if (!Database.ItemUniques.TryGet(EXCALIBUR_CODE, out excalTemplate))
                {
                    Log("DEBUG GIVE: Excalibur not found in UniqueItemsTable!");
                    return;
                }
                Log($"DEBUG GIVE: Found Excalibur template. ItemClass={excalTemplate.ItemClass.Code}, " +
                    $"SpecChar={excalTemplate.SpecificCharacter.Value}, " +
                    $"Rarity={excalTemplate.Item.Rarity}, Tier={excalTemplate.Item.Tier}");
                var excalItem = excalTemplate.Item.Clone();
                FixItemClass(excalItem);
                Log($"DEBUG GIVE: Cloned item. Code={excalItem.Code}, Class={excalItem.Class.Code}, " +
                    $"Type={excalItem.Type}, IsUnique={excalItem.IsUnique}");
                foreach (var affix in excalItem.Affixes)
                {
                    Log($"DEBUG GIVE:   Affix: {affix.Code} Lv{affix.Levels}");
                }
                bool isP2 = targetPlayer != null;
                Profile profile;
                Equipment equipment;
                if (isP2 && CoopP2Profile.Instance != null)
                {
                    profile = CoopP2Profile.Instance;
                    string charCode = targetPlayer.Data.Code.ToString();
                    equipment = profile.GetLoadoutsFor(CharacterCode.FromString(charCode))
                        .GetSelectedLoadout();
                    Log($"DEBUG GIVE: Targeting P2 ({charCode}), using CoopP2Profile");
                }
                else
                {
                    profile = Game.ActiveProfile;
                    string charCode = Player.Exists ? Player.Instance.Data.Code.ToString() : "Unknown";
                    equipment = profile.GetActiveEquipment();
                    Log($"DEBUG GIVE: Targeting P1 ({charCode}), using ActiveProfile");
                }
                if (equipment == null)
                {
                    Log("DEBUG GIVE: Equipment is null!");
                    return;
                }
                var weaponSlot = equipment.GetSlot(ItemType.Weapon);
                Log($"DEBUG GIVE: Weapon slot — IsEmpty={weaponSlot.IsEmpty}, " +
                    $"Current={weaponSlot.Item?.Code ?? "none"}");
                weaponSlot.Set(excalItem);
                Log($"DEBUG GIVE: Excalibur placed in weapon slot! " +
                    $"Verify: slot.Item={weaponSlot.Item?.Code ?? "null"}, " +
                    $"IsEmpty={weaponSlot.IsEmpty}");
                _hasExcaliburEquipped = true;
                var excalOwner = isP2 ? targetPlayer : Player.Instance;
                if (excalOwner != null) _excaliburPlayers.Add(excalOwner);
                Log($"DEBUG GIVE: _hasExcaliburEquipped set to true for {excalOwner?.name}.");
                if (isP2 && targetPlayer != null)
                {
                    try
                    {
                        var tracker = Traverse.Create(targetPlayer).Field("_equipmentAbilityTracker").GetValue();
                        if (tracker != null)
                        {
                            Traverse.Create(tracker).Method("OnEquipmentChanged", new object[] { excalItem, null }).GetValue();
                            Log("DEBUG GIVE: Triggered EquipmentAbilityTracker.OnEquipmentChanged for P2");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"DEBUG GIVE: Could not refresh equipment abilities (non-fatal): {ex.Message}");
                    }
                }
                try
                {
                    var excalDrop = excalTemplate.Item.Clone();
                    FixItemClass(excalDrop);
                    Vector2 dropPos = Player.Position + new Vector2(1f, 0f);
                    var spawner = Traverse.Create(typeof(SingletonBehaviour<ItemSpawner>))
                        .Property("Instance").GetValue<ItemSpawner>();
                    if (spawner != null)
                    {
                        spawner.DropItem(dropPos, excalDrop);
                        Log("DEBUG GIVE: Also dropped Excalibur on ground near player.");
                    }
                }
                catch (Exception ex)
                {
                    Log($"DEBUG GIVE: Ground drop failed (non-fatal): {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Log($"DEBUG GIVE ERROR: {ex}");
            }
        }
        public static void DebugDumpState()
        {
            Log("=== EXCALIBUR DEBUG DUMP ===");
            Log($"  _hasExcaliburEquipped = {_hasExcaliburEquipped}");
            Log($"  _tintedRenderers.Count = {_tintedRenderers.Count}");
            Log($"  Player.Exists = {Player.Exists}");
            if (Player.Exists)
            {
                Log($"  Player.Instance.Data.Code = {Player.Instance.Data.Code}");
                var entity = Player.Instance.Entity;
                if (entity != null)
                {
                    Log($"  Entity alive = {entity.IsAlive}");
                    Log($"  Entity position = {entity.transform.position}");
                    var facing = Traverse.Create(entity).Property("Facing").GetValue<object>();
                    Log($"  Entity facing = {facing}");
                }
            }
            try
            {
                UniqueItemTemplate tmpl;
                if (Database.ItemUniques.TryGet(EXCALIBUR_CODE, out tmpl))
                {
                    Log($"  DB Excalibur: ItemClass={tmpl.ItemClass.Code}, " +
                        $"SpecChar={tmpl.SpecificCharacter.Value}, " +
                        $"Hidden={tmpl.IsHidden}");
                }
                else
                {
                    Log("  DB Excalibur: NOT FOUND in UniqueItemsTable");
                }
            }
            catch (Exception ex)
            {
                Log($"  DB check error: {ex.Message}");
            }
            try
            {
                var excalAffix = Database.ItemAffixes.Get(AffixCode.FromString(EXCALIBUR_CODE));
                if (excalAffix != null)
                {
                    Log($"  Excalibur affix: SkillSlot={excalAffix.SkillSlot}, IsSpecial={excalAffix.IsSpecial}, " +
                        $"Abilities.Length={excalAffix.Abilities.Length}");
                }
            }
            catch (Exception ex)
            {
                Log($"  Affix check error: {ex.Message}");
            }
            if (CoopP2Profile.Instance != null)
            {
                try
                {
                    var p2Equip = CoopP2Profile.Instance.GetActiveEquipment();
                    if (p2Equip != null)
                    {
                        var wSlot = p2Equip.GetSlot(ItemType.Weapon);
                        Log($"  P2 weapon slot: {wSlot.Item?.Code ?? "empty"}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"  P2 equip check error: {ex.Message}");
                }
            }
            try
            {
                var p1Equip = Game.ActiveProfile.GetActiveEquipment();
                if (p1Equip != null)
                {
                    var wSlot = p1Equip.GetSlot(ItemType.Weapon);
                    Log($"  P1 weapon slot: {wSlot.Item?.Code ?? "empty"}");
                }
            }
            catch (Exception ex)
            {
                Log($"  P1 equip check error: {ex.Message}");
            }
            if (Player.Exists)
            {
                var entity = Player.Instance.Entity;
                if (entity != null)
                {
                    var renderers = entity.GetComponentsInChildren<SpriteRenderer>(true);
                    Log($"  Player SpriteRenderers ({renderers.Length}):");
                    foreach (var sr in renderers)
                    {
                        Log($"    [{sr.gameObject.name}] color={sr.color} enabled={sr.enabled} " +
                            $"sortOrder={sr.sortingOrder}");
                    }
                }
            }
            Log("=== END DEBUG DUMP ===");
        }
        [HarmonyPatch]
        static class PatchDatabaseInit
        {
            static MethodBase TargetMethod()
            {
                var type = AccessTools.TypeByName("Death.Data.Database");
                if (type == null)
                {
                    Log("WARN: Database type not found for init hook");
                    return null;
                }
                return AccessTools.Method(type, "Init", new Type[] { typeof(bool) });
            }
            static void Postfix()
            {
                Log("Database.Init postfix — running Excalibur fixup");
                PatchExcaliburItemData.FixupExcaliburData();
            }
        }
        static class PatchExcaliburItemData
        {
            public static void FixupExcaliburData()
            {
                if (_dataFixupDone) return; 
                try
                {
                    var uniques = Database.ItemUniques;
                    if (uniques == null)
                    {
                        Log("Database.ItemUniques is null, cannot fixup.");
                        return;
                    }
                    UniqueItemTemplate excalTemplate = null;
                    foreach (var tmpl in uniques.All)
                    {
                        if (tmpl.Item.Code == EXCALIBUR_CODE)
                        {
                            excalTemplate = tmpl;
                            break;
                        }
                    }
                    if (excalTemplate == null)
                    {
                        Log("Excalibur not found in UniqueItemsTable.");
                        return;
                    }
                    ItemClass w2hs = ItemClass.FromCode("w2hs");
                    CharacterCode warriorCode = CharacterCode.FromString(WARRIOR_CODE);
                    var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                    Traverse.Create(excalTemplate).Field("ItemClass").SetValue(w2hs);
                    var specCharField = typeof(UniqueItemTemplate).GetField("SpecificCharacter",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (specCharField != null)
                    {
                        var opt = new Optional<CharacterCode>();
                        opt.Enabled = true;
                        opt.Value = warriorCode;
                        specCharField.SetValue(excalTemplate, opt);
                    }
                    var item = excalTemplate.Item;
                    if (item != null)
                    {
                        var classField = item.GetType().GetField("<Class>k__BackingField", flags);
                        if (classField != null)
                            classField.SetValue(item, w2hs);
                        var affixesField = item.GetType().GetField("_affixes", flags);
                        if (affixesField != null)
                        {
                            var affixList = affixesField.GetValue(item) as List<Item.AffixReference>;
                            if (affixList != null)
                            {
                                bool hasArea = false;
                                foreach (var af in affixList)
                                {
                                    if (af.Code.ToString() == "are%") { hasArea = true; break; }
                                }
                                if (!hasArea)
                                {
                                    affixList.Add(new Item.AffixReference(AffixCode.FromString("are%"), 36));
                                    Log("Added are% affix (36 levels ≈ +25% area) to Excalibur template");
                                }
                            }
                        }
                        Log($"Template Item fixed: Class={item.Class.Code}, SubtypeCode={item.SubtypeCode}");
                    }
                    var subtypes = Database.ItemSubtypes;
                    if (subtypes != null)
                    {
                        ItemSubtype excalSubtype;
                        if (subtypes.TryGet(EXCALIBUR_SUBTYPE, out excalSubtype))
                        {
                            Traverse.Create(excalSubtype).Field("ItemClass").SetValue(w2hs);
                            var stSpecChar = typeof(ItemSubtype).GetField("SpecificCharacter",
                                BindingFlags.Public | BindingFlags.Instance);
                            if (stSpecChar != null)
                            {
                                var opt2 = new Optional<CharacterCode>();
                                opt2.Enabled = true;
                                opt2.Value = warriorCode;
                                stSpecChar.SetValue(excalSubtype, opt2);
                            }
                        }
                    }
                    try
                    {
                        var weaponTable = Database.ItemWeapons;
                        var entriesField = weaponTable.GetType().BaseType?.GetField("Entries",
                            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                        if (entriesField == null)
                            entriesField = weaponTable.GetType().GetField("Entries",
                                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                        var entries = entriesField?.GetValue(weaponTable) as System.Collections.IDictionary;
                        if (entries != null)
                        {
                            ItemWeapon gsWeapon = entries.Contains("HackAndSlash_subtype")
                                ? entries["HackAndSlash_subtype"] as ItemWeapon : null;
                            ItemWeapon excalWeapon = entries.Contains(EXCALIBUR_SUBTYPE)
                                ? entries[EXCALIBUR_SUBTYPE] as ItemWeapon : null;
                            if (gsWeapon != null && excalWeapon != null)
                            {
                                var builder = new Stats.Builder();
                                foreach (StatId sid in StatIdUtils.Enumerate())
                                {
                                    builder.Set(sid, gsWeapon.Stats.Get(sid));
                                    builder.SetScaling(sid, gsWeapon.Stats.GetScaling(sid));
                                }
                                builder.Set(StatId.BaseAttackTime, EXCALIBUR_ATTACK_TIME);
                                Stats newStats = builder.ToStats();
                                var statsField = typeof(ItemWeapon).GetField("Stats");
                                if (statsField != null)
                                {
                                    statsField.SetValue(excalWeapon, newStats);
                                    Log($"Excalibur weapon stats: copied greatsword + BaseAttackTime={EXCALIBUR_ATTACK_TIME}");
                                }
                            }
                            else
                            {
                                Log($"WARN: weapon entries - gs={gsWeapon != null}, excal={excalWeapon != null}");
                            }
                        }
                        else
                        {
                            Log("WARN: Could not access ItemWeaponTable entries");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"WARN: Weapon stats modification failed: {ex.Message}");
                    }
                    Log($"Excalibur data fixup complete! ItemClass=w2hs, SpecChar=Warrior");
                    SetupExcaliburStrikeAbility();
                    _dataFixupDone = true;
                }
                catch (Exception ex)
                {
                    Log($"ERROR in FixupExcaliburData: {ex}");
                }
            }
        }
        private static void SetupExcaliburStrikeAbility()
        {
            try
            {
                ItemAffix excalAffix = null;
                try { excalAffix = Database.ItemAffixes.Get(AffixCode.FromString(EXCALIBUR_CODE)); }
                catch { }
                if (excalAffix == null)
                {
                    Log("Excalibur affix not found in ItemAffixes table");
                    return;
                }
                if (_abilitySetupDone)
                {
                    Log("Excalibur Strike ability already set up, skipping");
                    return;
                }
                var abilitiesField = typeof(ItemAffix).GetField("Abilities");
                if (abilitiesField != null)
                    abilitiesField.SetValue(excalAffix, Array.Empty<ItemAffix.Ability>());
                var slotField = typeof(ItemAffix).GetField("SkillSlot");
                if (slotField != null)
                    slotField.SetValue(excalAffix, SkillSlot.Strike);
                _abilitySetupDone = true;
                Log($"Excalibur affix: SkillSlot=Strike, no game ability (postfix handles smite)");
            }
            catch (Exception ex)
            {
                Log($"ERROR in SetupExcaliburStrikeAbility: {ex}");
            }
        }
        private const float LADY_DROP_CHANCE = 0.10f;
        private static string _excalFlagPath;
        private static string GetExcalFlagPath()
        {
            if (_excalFlagPath == null)
                _excalFlagPath = System.IO.Path.Combine(CoopPlugin.ModDir, "excalibur_obtained.flag");
            return _excalFlagPath;
        }
        public static bool HasExcaliburBeenObtained()
        {
            return System.IO.File.Exists(GetExcalFlagPath());
        }
        private static void MarkExcaliburObtained()
        {
            try
            {
                System.IO.File.WriteAllText(GetExcalFlagPath(), $"Obtained {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                Log("Excalibur flag saved — will not drop again.");
            }
            catch (Exception ex) { Log($"WARN: Failed to save Excalibur flag: {ex.Message}"); }
        }
        public static void ResetExcaliburObtainedFlag()
        {
            try
            {
                if (System.IO.File.Exists(GetExcalFlagPath()))
                    System.IO.File.Delete(GetExcalFlagPath());
                Log("Excalibur obtained flag RESET — can drop again.");
            }
            catch (Exception ex) { Log($"WARN: Failed to reset flag: {ex.Message}"); }
        }
        private static bool AnyPlayerIsWarrior()
        {
            foreach (var bp in PlayerRegistry.Players)
            {
                if (bp == null || bp.Data == null) continue;
                if (bp.Data.Code.ToString() == WARRIOR_CODE) return true;
            }
            if (Player.Exists && Player.Instance != null && Player.Instance.Data != null)
            {
                if (Player.Instance.Data.Code.ToString() == WARRIOR_CODE) return true;
            }
            return false;
        }
        public static bool TryDropExcalibur(Vector2 dropPos, bool forceChance = false)
        {
            if (HasExcaliburBeenObtained())
            {
                Log("Excalibur already obtained (flag file exists). No drop.");
                return false;
            }
            if (!AnyPlayerIsWarrior())
            {
                Log("No Warrior (Skadi) in run. Excalibur cannot drop.");
                return false;
            }
            if (!forceChance)
            {
                float roll = UnityEngine.Random.value;
                if (roll > LADY_DROP_CHANCE)
                {
                    Log($"Excalibur drop roll failed: {roll:F3} > {LADY_DROP_CHANCE:F2}");
                    return false;
                }
                Log($"Excalibur drop roll PASSED: {roll:F3} <= {LADY_DROP_CHANCE:F2}");
            }
            else
            {
                Log("Excalibur drop forced (debug).");
            }
            try
            {
                foreach (var item in Game.ActiveProfile.GetAllExistingItems())
                {
                    if (item.Code == EXCALIBUR_CODE)
                    {
                        Log("Player already has Excalibur in inventory, skipping drop.");
                        MarkExcaliburObtained(); 
                        return false;
                    }
                }
            }
            catch (Exception ex) { Log($"WARN: Inventory check failed: {ex.Message}"); }
            UniqueItemTemplate excalTemplate;
            if (!Database.ItemUniques.TryGet(EXCALIBUR_CODE, out excalTemplate))
            {
                Log("ERROR: Excalibur not in UniqueItemsTable!");
                return false;
            }
            var excalItem = excalTemplate.Item.Clone();
            FixItemClass(excalItem);
            try
            {
                SingletonBehaviour<ItemSpawner>.Instance.DropItem(dropPos, excalItem);
                MarkExcaliburObtained();
                Log($"Excalibur DROPPED at ({dropPos.x:F1}, {dropPos.y:F1})!");
                return true;
            }
            catch (Exception ex)
            {
                Log($"ERROR dropping Excalibur item: {ex}");
                return false;
            }
        }
        [HarmonyPatch]
        static class PatchLadyDrop
        {
            static MethodBase TargetMethod()
            {
                var type = AccessTools.TypeByName("Death.Run.Systems.System_LootDropper");
                if (type == null)
                {
                    Log("WARN: System_LootDropper type not found");
                    return null;
                }
                return AccessTools.Method(type, "OnMonsterDied");
            }
            static void Postfix(object __instance, object ev)
            {
                try
                {
                    PatchExcaliburItemData.FixupExcaliburData();
                    var monster = Traverse.Create(ev).Field("Monster").GetValue()
                               ?? Traverse.Create(ev).Property("Monster").GetValue();
                    if (monster == null) return;
                    var data = Traverse.Create(monster).Property("Data").GetValue();
                    if (data == null) return;
                    string code = Traverse.Create(data).Property("Code").GetValue<string>();
                    if (code != LADY_BOSS_CODE) return;
                    Log($"Lady of the Lake killed! Attempting Excalibur drop...");
                    Vector2 dropPos = Player.Exists ? Player.Position : Vector2.zero;
                    var monsterComp = monster as Component;
                    if (monsterComp != null)
                        dropPos = (Vector2)monsterComp.transform.position;
                    TryDropExcalibur(dropPos);
                }
                catch (Exception ex)
                {
                    Log($"ERROR in LadyDrop postfix: {ex}");
                }
            }
        }
        [HarmonyPatch]
        static class PatchWeaponScale
        {
            static MethodBase TargetMethod()
            {
                var type = AccessTools.TypeByName("Death.Run.Behaviours.Abilities.Actives.Attack_Melee");
                if (type == null)
                {
                    Log("WARN: Attack_Melee type not found");
                    return null;
                }
                return AccessTools.Method(type, "SpawnWeapon");
            }
            static void Postfix(object __instance, ref Entity weapon)
            {
                try
                {
                    if (!_hasExcaliburEquipped) return;
                    if (weapon == null) return;
                    var entity = Traverse.Create(__instance).Property("Entity").GetValue<Entity>();
                    if (entity == null) return;
                    if (entity.GetComponent<Behaviour_Player>() == null) return;
                    Vector3 s = weapon.transform.localScale;
                    weapon.transform.localScale = new Vector3(s.x * 1.5f, s.y * 1.5f, s.z);
                }
                catch (Exception ex)
                {
                    Log($"ERROR in WeaponScale postfix: {ex}");
                }
            }
        }
        private static int _equipCheckFrame = 0;
        public static void CheckExcaliburEquipState()
        {
            _equipCheckFrame++;
            if (_equipCheckFrame % 30 != 0 && _excaliburPlayers.Count > 0) return;
            var allPlayers = PlayerRegistry.Players;
            HashSet<Behaviour_Player> newSet = new HashSet<Behaviour_Player>(
                new UnityObjectComparer<Behaviour_Player>());
            foreach (var bp in allPlayers)
            {
                if (bp == null || bp.Entity == null) continue;
                string charCode = bp.Data?.Code.ToString();
                if (charCode != WARRIOR_CODE) continue;
                if (PlayerHasExcalibur(bp))
                    newSet.Add(bp);
            }
            if (Player.Exists && Player.Instance != null && !allPlayers.Any(p => p == Player.Instance))
            {
                var bp = Player.Instance;
                string charCode = bp.Data?.Code.ToString();
                if (charCode == WARRIOR_CODE && PlayerHasExcalibur(bp))
                    newSet.Add(bp);
            }
            try
            {
                var lobbyType = AccessTools.TypeByName("Death.TimesRealm.Facade_Lobby");
                if (lobbyType != null)
                {
                    object lobby = Traverse.Create(lobbyType).Property("Instance").GetValue();
                    if (lobby != null)
                    {
                        var previewer = Traverse.Create(lobby).Field("_charSheetPreview").GetValue();
                        if (previewer != null)
                        {
                            var previewPlayer = Traverse.Create(previewer)
                                .Field("_playerInstance").GetValue<Behaviour_Player>();
                            if (previewPlayer != null && previewPlayer.Entity != null
                                && previewPlayer.Entity.IsInitialized
                                && !newSet.Contains(previewPlayer))
                            {
                                string charCode = previewPlayer.Data?.Code.ToString();
                                if (charCode == WARRIOR_CODE && PlayerHasExcalibur(previewPlayer))
                                    newSet.Add(previewPlayer);
                            }
                        }
                    }
                }
            }
            catch { }
            bool changed = newSet.Count != _excaliburPlayers.Count;
            if (!changed)
            {
                foreach (var bp in newSet)
                {
                    if (!_excaliburPlayers.Contains(bp)) { changed = true; break; }
                }
            }
            if (changed)
            {
                foreach (var bp in newSet)
                {
                    if (!_excaliburPlayers.Contains(bp))
                    {
                        ApplySaberHairColor(bp);
                        ApplyExcaliburAttackTime(bp);
                        Log($"Excalibur equipped on {bp.name} — Saber blonde hair + 0.80 attack time applied!");
                    }
                }
                foreach (var bp in _excaliburPlayers)
                {
                    if (bp != null && !newSet.Contains(bp))
                    {
                        RevertHairColor(bp);
                        Log($"Excalibur unequipped on {bp.name} — hair color reverted.");
                    }
                }
                _excaliburPlayers = new HashSet<Behaviour_Player>(
                    newSet, new UnityObjectComparer<Behaviour_Player>());
            }
            _hasExcaliburEquipped = _excaliburPlayers.Count > 0;
        }
        private static bool PlayerHasExcalibur(Behaviour_Player bPlayer)
        {
            var equipment = Traverse.Create(bPlayer).Field("_equipment").GetValue();
            if (equipment == null)
                equipment = Traverse.Create(bPlayer).Property("Equipment").GetValue();
            if (equipment == null) return false;
            var slotsField = Traverse.Create(equipment).Field("_slots").GetValue();
            if (slotsField is IEnumerable enumSlots)
            {
                foreach (var slot in enumSlots)
                {
                    var itemObj = Traverse.Create(slot).Property("Item").GetValue();
                    if (itemObj == null) continue;
                    string ic = Traverse.Create(itemObj).Property("Code").GetValue<string>()
                             ?? Traverse.Create(itemObj).Field("Code").GetValue<string>();
                    if (ic == EXCALIBUR_CODE) return true;
                }
            }
            return false;
        }
        private const float EXCALIBUR_ATTACK_TIME = 0.80f;
        private static bool TrySetAttackTimeOnPlayer(Behaviour_Player bPlayer)
        {
            if (bPlayer == null || bPlayer.Unit == null || bPlayer.Unit.AbilityManager == null)
                return false;
            var abilityMgr = bPlayer.Unit.AbilityManager;
            IAbility weaponAbility = null;
            foreach (IAbility ability in abilityMgr)
            {
                if (ability == null) continue;
                foreach (var iface in ability.GetType().GetInterfaces())
                {
                    if (iface.Name == "IAttack")
                    {
                        weaponAbility = ability;
                        break;
                    }
                }
                if (weaponAbility != null) break;
            }
            if (weaponAbility == null) return false;
            var runtimeStats = weaponAbility.Stats;
            if (runtimeStats == null) return false;
            float oldVal = runtimeStats.GetBase(StatId.BaseAttackTime);
            if (Mathf.Approximately(oldVal, EXCALIBUR_ATTACK_TIME)) return true; 
            var oldBase = runtimeStats.Base;
            var builder = new Stats.Builder();
            foreach (StatId sid in StatIdUtils.Enumerate())
            {
                builder.Set(sid, oldBase.Get(sid));
                builder.SetScaling(sid, oldBase.GetScaling(sid));
            }
            builder.Set(StatId.BaseAttackTime, EXCALIBUR_ATTACK_TIME);
            runtimeStats.Base = builder.ToStats();
            Log($"Excalibur attack time: {oldVal:F2} → {EXCALIBUR_ATTACK_TIME:F2} on {bPlayer.name} (cloned stats)");
            return true;
        }
        private static void ApplyExcaliburAttackTime(Behaviour_Player bPlayer)
        {
            try
            {
                if (TrySetAttackTimeOnPlayer(bPlayer)) return;
            }
            catch (Exception ex)
            {
                Log($"ApplyExcaliburAttackTime ERROR: {ex.Message}");
            }
        }
        private static void ApplySaberHairColor(Behaviour_Player bPlayer)
        {
            if (bPlayer == null || bPlayer.Entity == null) return;
            var entity = bPlayer.Entity;
            var renderers = entity.GetComponentsInChildren<SpriteRenderer>(true);
            var list = new List<SpriteRenderer>();
            foreach (var sr in renderers)
            {
                string name = sr.gameObject.name.ToLower();
                if (name.Contains("hair") || name.Contains("head") || name.Contains("bangs"))
                {
                    sr.color = SABER_BLONDE;
                    list.Add(sr);
                    Log($"Tinted sprite '{sr.gameObject.name}' to Saber blonde");
                }
            }
            if (list.Count == 0)
            {
                Log("No named hair sprites found. Applying hue overlay.");
                var block = new MaterialPropertyBlock();
                foreach (var sr in renderers)
                {
                    sr.GetPropertyBlock(block);
                    block.SetColor("_OverlayColor", SABER_BLONDE);
                    block.SetFloat("_Overlay", 0.3f);
                    sr.SetPropertyBlock(block);
                    list.Add(sr);
                }
            }
            _tintedRenderers[bPlayer] = list;
        }
        private static void RevertHairColor(Behaviour_Player bPlayer)
        {
            if (!_tintedRenderers.TryGetValue(bPlayer, out var list)) return;
            foreach (var sr in list)
            {
                if (sr == null) continue;
                sr.color = Color.white;
                var block = new MaterialPropertyBlock();
                sr.GetPropertyBlock(block);
                block.SetFloat("_Overlay", 0f);
                sr.SetPropertyBlock(block);
            }
            _tintedRenderers.Remove(bPlayer);
        }
        private static float _smiteCooldownTimer = 0f;
        private static float _rollWindowTimer = 0f; 
        private static int _smiteProcCount = 0;
        public static void UpdateSmiteCooldown()
        {
            if (_smiteCooldownTimer > 0f)
                _smiteCooldownTimer -= Time.deltaTime;
            if (_rollWindowTimer > 0f)
                _rollWindowTimer -= Time.deltaTime;
        }
        private static bool _statsLogged = false;
        private static Behaviour_Player FindExcaliburAttacker(IDamageDealer dealer)
        {
            if (!dealer.IsPrimaryAttack) return null;
            Entity dealerEntity = dealer as Entity;
            if (dealerEntity == null) return null;
            Entity creatorEntity = dealerEntity;
            for (int i = 0; i < 5; i++) 
            {
                var handle = creatorEntity.Creator;
                if (!handle.IsValid) break;
                creatorEntity = handle.Entity;
            }
            foreach (var bp in _excaliburPlayers)
            {
                if (bp == null || bp.Entity == null) continue;
                if (creatorEntity == bp.Entity) return bp;
                if (dealerEntity == bp.Entity) return bp;
            }
            return null;
        }
        private static Behaviour_Player FindLiveExcaliburPlayer()
        {
            foreach (var bp in _excaliburPlayers)
            {
                if (bp != null && bp.Entity != null && bp.Entity.IsInitialized)
                    return bp;
            }
            foreach (var bp in PlayerRegistry.Players)
            {
                if (bp == null || bp.Entity == null || !bp.Entity.IsInitialized) continue;
                string charCode = bp.Data?.Code.ToString();
                if (charCode == WARRIOR_CODE && PlayerHasExcalibur(bp))
                    return bp;
            }
            if (Player.Exists && Player.Instance != null && Player.Instance.Entity != null
                && Player.Instance.Entity.IsInitialized)
            {
                string charCode = Player.Instance.Data?.Code.ToString();
                if (charCode == WARRIOR_CODE && PlayerHasExcalibur(Player.Instance))
                    return Player.Instance;
            }
            try
            {
                var lobbyType = AccessTools.TypeByName("Death.TimesRealm.Facade_Lobby");
                if (lobbyType != null)
                {
                    object lobby = Traverse.Create(lobbyType).Property("Instance").GetValue();
                    if (lobby != null)
                    {
                        var previewer = Traverse.Create(lobby).Field("_charSheetPreview").GetValue();
                        if (previewer != null)
                        {
                            var previewPlayer = Traverse.Create(previewer)
                                .Field("_playerInstance").GetValue<Behaviour_Player>();
                            if (previewPlayer != null && previewPlayer.Entity != null
                                && previewPlayer.Entity.IsInitialized)
                            {
                                string charCode = previewPlayer.Data?.Code.ToString();
                                if (charCode == WARRIOR_CODE && PlayerHasExcalibur(previewPlayer))
                                {
                                    Log($"FindLiveExcaliburPlayer: found hub previewer player");
                                    return previewPlayer;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"FindLiveExcaliburPlayer: hub previewer check failed: {ex.Message}");
            }
            return null;
        }
        private static Death.Run.Core.CharacterInfo GetHubCharacterInfo()
        {
            try
            {
                var lobbyType = AccessTools.TypeByName("Death.TimesRealm.Facade_Lobby");
                if (lobbyType == null) return null;
                object lobby = Traverse.Create(lobbyType).Property("Instance").GetValue();
                if (lobby == null) return null;
                var previewer = Traverse.Create(lobby).Field("_charSheetPreview").GetValue();
                if (previewer == null) return null;
                return Traverse.Create(previewer).Property("CharacterInfo").GetValue<Death.Run.Core.CharacterInfo>();
            }
            catch { return null; }
        }
        private static float GetEffectiveChance(Behaviour_Player bp = null)
        {
            float baseChance = 0.10f;
            Death.Run.Core.CharacterInfo charInfo = GetHubCharacterInfo();
            if (charInfo != null && charInfo.Defensive != null)
            {
                try
                {
                    float chanceMod = charInfo.Defensive.ReadOnlyModifier
                        .GetCombinedModifierChange(StatId.Chance, false);
                    float result = baseChance * (1f + chanceMod);
                    if (!_statsLogged)
                        Log($"Stats (hub CharacterInfo direct): Chance modifier={chanceMod:F3}, effective={result:P1}");
                    return result;
                }
                catch (Exception ex)
                {
                    if (!_statsLogged) Log($"Stats: Hub CharacterInfo Chance read failed: {ex.Message}");
                }
            }
            Entity entity = bp?.Entity;
            if (entity == null && Player.Exists) entity = Player.Instance.Entity;
            if (entity == null) return baseChance;
            try
            {
                float chanceMod = entity.Stats.ReadOnlyModifier
                    .GetCombinedModifierChange(StatId.Chance, false);
                float result = baseChance * (1f + chanceMod);
                if (!_statsLogged)
                    Log($"Stats: Chance modifier={chanceMod:F3}, effective={result:P1}");
                return result;
            }
            catch (Exception ex)
            {
                if (!_statsLogged) Log($"Stats: Chance read FAILED: {ex.Message}");
                return baseChance;
            }
        }
        private static float GetEffectiveCooldown(Behaviour_Player bp = null)
        {
            float baseCooldown = 1.0f;
            Death.Run.Core.CharacterInfo charInfo = GetHubCharacterInfo();
            if (charInfo != null && charInfo.Root != null)
            {
                try
                {
                    float cdMod = charInfo.Root
                        .GetCombinedModifierChange(StatId.CooldownSpeed, true);
                    float result = baseCooldown / Mathf.Max(0.1f, 1f + cdMod);
                    if (!_statsLogged)
                    {
                        Log($"Stats (hub CharacterInfo direct): CooldownSpeed modifier={cdMod:F3}, effective cooldown={result:F2}s");
                        _statsLogged = true;
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    if (!_statsLogged)
                    {
                        Log($"Stats: Hub CharacterInfo CDR read failed: {ex.Message}");
                        _statsLogged = true;
                    }
                }
            }
            Entity entity = bp?.Entity;
            if (entity == null && Player.Exists) entity = Player.Instance.Entity;
            if (entity == null) return baseCooldown;
            try
            {
                var root = entity.Team.StatHierarchy.Root;
                float cdMod = root.GetCombinedModifierChange(StatId.CooldownSpeed, true);
                float result = baseCooldown / Mathf.Max(0.1f, 1f + cdMod);
                if (!_statsLogged)
                {
                    Log($"Stats: CooldownSpeed modifier={cdMod:F3}, effective cooldown={result:F2}s");
                    _statsLogged = true;
                }
                return result;
            }
            catch (Exception ex)
            {
                if (!_statsLogged)
                {
                    Log($"Stats: CooldownSpeed read FAILED: {ex.Message}");
                    _statsLogged = true;
                }
                return baseCooldown;
            }
        }
        [HarmonyPatch]
        static class PatchOnDamageDealt
        {
            static MethodBase TargetMethod()
            {
                var type = AccessTools.TypeByName("Death.Run.Behaviours.Entities.Behaviour_Unit");
                if (type == null)
                {
                    Log("WARN: Behaviour_Unit type not found for smite hook");
                    return null;
                }
                var method = AccessTools.Method(type, "TakeDamage",
                    new Type[] { typeof(Damage), typeof(float) });
                if (method == null)
                {
                    Log("WARN: Behaviour_Unit.TakeDamage method not found, trying all overloads");
                    foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (m.Name != "TakeDamage") continue;
                        var pars = m.GetParameters();
                        Log($"  Found: TakeDamage({string.Join(", ", Array.ConvertAll(pars, p => p.ParameterType.Name))})");
                        if (pars.Length == 2 && pars[0].ParameterType.IsByRef && pars[1].ParameterType.IsByRef)
                        {
                            method = m;
                            Log("  -> Using this overload (2 ref params)");
                            break;
                        }
                    }
                }
                if (method != null) Log($"TakeDamage patch target found: {method.DeclaringType.Name}.{method.Name}");
                return method;
            }
            private static bool _isFiringSmite = false; 
            private static int _diagCount = 0;
            static void Postfix(object __instance, bool __result, ref Damage __0)
            {
                try
                {
                    if (!_hasExcaliburEquipped) return;
                    if (!__result) { if (_diagCount < 3) { _diagCount++; Log($"DIAG TakeDmg: __result=false"); } return; }
                    if (_isFiringSmite) return;
                    var unit = __instance as Behaviour_Unit;
                    if (unit == null || unit.Entity == null) { if (_diagCount < 3) { _diagCount++; Log($"DIAG TakeDmg: unit null"); } return; }
                    if (unit.Entity.TeamId == TeamId.Player) return; 
                    if (unit.Entity.TeamId == TeamId.MapObjects) return;
                    if (__0.Dealer == null) { if (_diagCount < 3) { _diagCount++; Log($"DIAG TakeDmg: Dealer null"); } return; }
                    if (__0.Dealer.TeamId != TeamId.Player) { if (_diagCount < 3) { _diagCount++; Log($"DIAG TakeDmg: Dealer team={__0.Dealer.TeamId}"); } return; }
                    Behaviour_Player attacker = FindExcaliburAttacker(__0.Dealer);
                    if (attacker == null) { if (_diagCount < 3) { _diagCount++; Log($"DIAG TakeDmg: attacker null, dealer={__0.Dealer.GetType().Name}, excPlayers={_excaliburPlayers.Count}"); } return; }
                    if (_smiteCooldownTimer > 0f) return;
                    if (_rollWindowTimer > 0f) return;
                    _rollWindowTimer = 0.4f; 
                    float chance = GetEffectiveChance(attacker);
                    if (UnityEngine.Random.value > chance) return;
                    _isFiringSmite = true;
                    _smiteCooldownTimer = GetEffectiveCooldown(attacker);
                    _smiteProcCount++;
                    try
                    {
                        FireSmiteLine(attacker.Entity);
                    }
                    finally
                    {
                        _isFiringSmite = false;
                    }
                    if (_smiteProcCount <= 10)
                    {
                        Log($"SmiteProc #{_smiteProcCount}: chance={chance:P0}, cd={GetEffectiveCooldown(attacker):F2}s, " +
                            $"target={unit.Entity.name}, attacker={attacker.name}");
                    }
                }
                catch (Exception ex)
                {
                    _isFiringSmite = false;
                    if (_smiteProcCount <= 3)
                        Log($"ERROR in PatchOnDamageDealt: {ex}");
                }
            }
        }
        private static void FireSmiteLine(Entity attacker)
        {
            if (attacker == null) return;
            Vector2 direction = attacker.AimDirection;
            if (direction.sqrMagnitude < 0.01f)
            {
                direction = attacker.Facing == Facing.Left ? Vector2.left : Vector2.right;
            }
            else
            {
                direction = direction.normalized;
            }
            string dirLabel = $"({direction.x:F2},{direction.y:F2})";
            Vector2 origin = (Vector2)attacker.transform.position;
            int hitMask = LayerMask.GetMask("EntityHitboxes");
            float totalLength = 50f;
            int segCount = 5;
            float segLength = totalLength / segCount;
            float startWidth = 1.2f;
            float endWidth = 0.15f;
            int applied = 0;
            HashSet<Entity> hitEntities = new HashSet<Entity>();
            for (int seg = 0; seg < segCount; seg++)
            {
                float t = (float)seg / (segCount - 1); 
                float width = Mathf.Lerp(startWidth, endWidth, t);
                float distOffset = seg * segLength;
                Vector2 segOrigin = origin + direction * distOffset;
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                RaycastHit2D[] hits = Physics2D.BoxCastAll(
                    segOrigin,
                    new Vector2(width, width),
                    angle,
                    direction,
                    segLength,
                    hitMask
                );
                foreach (var hit in hits)
                {
                    if (hit.collider == null) continue;
                    var hitbox = hit.collider.GetComponent<IEntityHitbox>();
                    Entity entity = hitbox != null ? hitbox.AttachedEntity : hit.collider.GetComponentInParent<Entity>();
                    if (entity == null) continue;
                    if (entity == attacker) continue;
                    if (!entity.IsAlive) continue;
                    if (hitEntities.Contains(entity)) continue;
                    if (entity.Team == attacker.Team) continue;
                    hitEntities.Add(entity);
                    ApplySmiteToEntity(attacker, entity);
                    applied++;
                }
            }
            if (applied > 0)
                Log($"Smite line hit {applied} enemies (dir={dirLabel})");
        }
        private static readonly DamageSource SmiteDamageSource =
            new DamageSource("Excalibur Holy Smite", TeamId.Player);
        private static void ApplySmiteToEntity(Entity attacker, Entity target)
        {
            try
            {
                var team = Teams.Get(TeamId.Player);
                IStatusInstance smiteStatus = team.CreateStatus((StatusCode)"smite", SmiteDamageSource);
                target.Statuses.AddTemporary(smiteStatus);
            }
            catch (Exception ex)
            {
                Log($"ERROR applying smite: {ex}");
            }
        }
        [HarmonyPatch]
        static class PatchAffixSpecialShow
        {
            static MethodBase TargetMethod()
            {
                var type = AccessTools.TypeByName("Death.UserInterface.Descriptions.GUI_AffixSpecial");
                if (type == null)
                {
                    Log("WARN: GUI_AffixSpecial type not found for tooltip patch");
                    return null;
                }
                return AccessTools.Method(type, "Show");
            }
            private static TextMeshProUGUI _pendingNameTmp;
            private static TextMeshProUGUI _pendingDescTmp;
            private static Color _pendingNameColor;
            static bool Prefix(object __instance, ItemAffix affix, int levels, TierId tier, object[] statArray)
            {
                try
                {
                    string affixCode = affix.Code.ToString();
                    if (affixCode != EXCALIBUR_CODE) return true;
                    return true; 
                }
                catch (Exception ex)
                {
                    Log($"ERROR in AffixSpecial Show prefix: {ex}");
                    return true;
                }
            }
            static void Postfix(object __instance, ItemAffix affix, int levels, TierId tier, object[] statArray)
            {
                try
                {
                    string affixCode = affix.Code.ToString();
                    if (affixCode != EXCALIBUR_CODE) return;
                    var nameObj = Traverse.Create(__instance).Field("_name").GetValue();
                    TextMeshProUGUI nameTmp = null;
                    if (nameObj != null)
                    {
                        var textProp = nameObj.GetType().GetProperty("Text");
                        if (textProp != null)
                            nameTmp = textProp.GetValue(nameObj) as TextMeshProUGUI;
                        var lsProp = nameObj.GetType().GetProperty("LocalizeString");
                        if (lsProp != null)
                        {
                            var ls = lsProp.GetValue(nameObj) as MonoBehaviour;
                            if (ls != null)
                            {
                                ls.enabled = false;
                                Log("Tooltip: Disabled LocalizeStringEvent on name");
                            }
                        }
                        if (nameTmp != null)
                        {
                            nameTmp.text = "Holy Smite";
                            var colorData = Traverse.Create(__instance).Field("_colorData").GetValue();
                            if (colorData != null)
                            {
                                try
                                {
                                    Color rarityColor = Traverse.Create(colorData)
                                        .Method("GetColorForRarity", new object[] { affix.MinRarity })
                                        .GetValue<Color>();
                                    nameTmp.color = rarityColor;
                                }
                                catch { nameTmp.color = new Color(1f, 0.84f, 0f); }
                            }
                        }
                    }
                    var slotText = Traverse.Create(__instance).Field("_slotText").GetValue<TextMeshProUGUI>();
                    if (slotText != null)
                    {
                        slotText.text = "Strike";
                        slotText.color = new Color(0.9f, 0.3f, 0.3f, 1f);
                        slotText.gameObject.SetActive(true);
                    }
                    var descObj = Traverse.Create(__instance).Field("_description").GetValue();
                    if (descObj != null)
                    {
                        var descLsProp = descObj.GetType().GetProperty("LocalizeString");
                        if (descLsProp != null)
                        {
                            var descLs = descLsProp.GetValue(descObj) as MonoBehaviour;
                            if (descLs != null)
                            {
                                descLs.enabled = false;
                                Log("Tooltip: Disabled LocalizeStringEvent on description");
                            }
                        }
                        var descTextProp = descObj.GetType().GetProperty("Text");
                        if (descTextProp != null)
                        {
                            var descTmp = descTextProp.GetValue(descObj) as TextMeshProUGUI;
                            if (descTmp != null)
                                descTmp.text = "Your attack hits have a chance to smite all enemies in a infinite line";
                        }
                    }
                    var statPool = Traverse.Create(__instance).Field("_statPool").GetValue();
                    if (statPool != null)
                    {
                        var disableAllMethod = statPool.GetType().GetMethod("DisableAll",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (disableAllMethod != null) disableAllMethod.Invoke(statPool, null);
                        var getMethod = statPool.GetType().GetMethod("Get",
                            BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                        if (getMethod != null)
                        {
                            Behaviour_Player excalPlayer = FindLiveExcaliburPlayer();
                            float chance = GetEffectiveChance(excalPlayer);
                            float cooldown = GetEffectiveCooldown(excalPlayer);
                            Log($"Tooltip stats: excalPlayer={excalPlayer?.name ?? "null"}, " +
                                $"entity={excalPlayer?.Entity?.IsInitialized}, chance={chance:P1}, cd={cooldown:F2}s");
                            string g = "#66CC66";
                            AddStatLine(getMethod, statPool,
                                $"Chance: <color={g}>{(chance * 100f):F0}%</color>");
                            AddStatLine(getMethod, statPool,
                                $"Cooldown: <color={g}>{cooldown:F1}s</color>");
                        }
                    }
                    Traverse.Create(__instance).Field("_pendingResize").SetValue(true);
                    _pendingNameTmp = nameTmp;
                    if (descObj != null)
                    {
                        var dtp = descObj.GetType().GetProperty("Text");
                        _pendingDescTmp = dtp?.GetValue(descObj) as TextMeshProUGUI;
                    }
                    if (nameTmp != null)
                        _pendingNameColor = nameTmp.color;
                    if (CoopRuntime.Instance != null)
                        CoopRuntime.Instance.StartCoroutine(DelayedTextFix());
                    Log("Tooltip: Rendered Holy Smite with real stats (postfix)");
                }
                catch (Exception ex)
                {
                    Log($"ERROR in AffixSpecial Show postfix: {ex}");
                }
            }
            private static IEnumerator DelayedTextFix()
            {
                yield return null; 
                if (_pendingNameTmp != null)
                {
                    _pendingNameTmp.text = "Holy Smite";
                    _pendingNameTmp.color = _pendingNameColor;
                }
                if (_pendingDescTmp != null)
                    _pendingDescTmp.text = "Your attack hits have a chance to smite all enemies in a infinite line";
                yield return null; 
                if (_pendingNameTmp != null)
                {
                    _pendingNameTmp.text = "Holy Smite";
                    _pendingNameTmp.color = _pendingNameColor;
                }
                if (_pendingDescTmp != null)
                    _pendingDescTmp.text = "Your attack hits have a chance to smite all enemies in a infinite line";
            }
            private static void AddStatLine(MethodInfo getMethod, object pool, string text)
            {
                var lineObj = getMethod.Invoke(pool, null);
                if (lineObj == null) return;
                var textProp = lineObj.GetType().GetProperty("Text");
                if (textProp != null) textProp.SetValue(lineObj, text);
            }
        }
        public static void OnRunStart()
        {
            PatchExcaliburItemData.FixupExcaliburData();
            _hasExcaliburEquipped = false;
            _excaliburPlayers.Clear();
            _smiteCooldownTimer = 0f;
            _rollWindowTimer = 0f;
            _smiteProcCount = 0;
            _statsLogged = false;
            _tintedRenderers.Clear();
            Log("ExcaliburPatch.OnRunStart — state reset.");
        }
        public static void OnRunEnd()
        {
            foreach (var bp in _excaliburPlayers)
                RevertHairColor(bp);
            _excaliburPlayers.Clear();
            _hasExcaliburEquipped = false;
        }
    }
}