using Death.Run.Core;
using Death.Run.Core.Abilities;
using Death.Run.Core.Entities;
using Death.Run.Behaviours.Players;
using Death.Data;
using Death.App;
using HarmonyLib;
using System;
namespace DeathMustDieCoop
{
    public static class CoopRunCharacterInfo
    {
        public static CharacterInfo Info { get; private set; }
        private static Behaviour_Player _player;
        private static CharacterData _charData;
        private static Profile _profile;
        private static Team _p2Team;
        private static Type _iAttackType;
        private static Type _iDefenseType;
        private static System.Reflection.MethodInfo _tryGetAttack;
        private static System.Reflection.MethodInfo _tryGetDefense;
        private static bool _typesResolved;
        public static CharacterInfo CachedP1Info;
        public static void Setup(Behaviour_Player player, CharacterData charData, Profile profile)
        {
            _player = player;
            _charData = charData;
            _profile = profile;
            _p2Team = player.Entity.Team;
            Info = new CharacterInfo();
            if (!_typesResolved)
            {
                var asm = typeof(Death.Game).Assembly;
                _iAttackType = asm.GetType("Death.Run.Core.Abilities.Actives.IAttack")
                    ?? asm.GetType("Death.Run.Core.Abilities.IAttack");
                _iDefenseType = asm.GetType("Death.Run.Behaviours.Abilities.IDefense");
                var tryGetBase = typeof(AbilityManager).GetMethod("TryGet");
                if (tryGetBase != null)
                {
                    if (_iAttackType != null)
                        _tryGetAttack = tryGetBase.MakeGenericMethod(_iAttackType);
                    if (_iDefenseType != null)
                        _tryGetDefense = tryGetBase.MakeGenericMethod(_iDefenseType);
                }
                _typesResolved = true;
                CoopPlugin.FileLog($"CoopRunCharacterInfo: Types resolved — IAttack={_iAttackType != null}, IDefense={_iDefenseType != null}");
            }
        }
        public static void Regenerate()
        {
            if (_player == null || Info == null) return;
            try
            {
                RuntimeStats weaponStats = null;
                RuntimeStats dashStats = null;
                if (_tryGetAttack != null)
                {
                    var args = new object[] { null };
                    if ((bool)_tryGetAttack.Invoke(_player.Unit.AbilityManager, args) && args[0] != null)
                        weaponStats = Traverse.Create(args[0]).Property("Stats").GetValue<RuntimeStats>();
                }
                if (_tryGetDefense != null)
                {
                    var args = new object[] { null };
                    if ((bool)_tryGetDefense.Invoke(_player.Unit.AbilityManager, args) && args[0] != null)
                        dashStats = Traverse.Create(args[0]).Property("Stats").GetValue<RuntimeStats>();
                }
                Team team = _p2Team;
                Info.Populate(
                    team.StatHierarchy,
                    _player.Unit.AbilityManager,
                    _player.Boons,
                    _profile.TalentsState,
                    _charData,
                    team,
                    team.StatHierarchy.Root,
                    weaponStats,
                    dashStats,
                    _player.Entity.Stats,
                    () => _player != null && _player.Unit.ArmorEnabled
                );
            }
            catch (Exception ex)
            {
                CoopPlugin.FileLog($"CoopRunCharacterInfo: Regenerate error: {ex.Message}");
            }
        }
        public static void Cleanup()
        {
            Info = null;
            _player = null;
            _charData = null;
            _profile = null;
            _p2Team = null;
            CachedP1Info = null;
            CoopPlugin.FileLog("CoopRunCharacterInfo: Cleaned up.");
        }
    }
}