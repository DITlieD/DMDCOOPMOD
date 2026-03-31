using System.Collections.Generic;
using System.Linq;
using Death.Run.Behaviours.Players;
using UnityEngine;
namespace DeathMustDieCoop
{
    public static class PlayerRegistry
    {
        private static readonly List<Behaviour_Player> _players = new List<Behaviour_Player>();
        public static bool SpawningP2;
        public static IReadOnlyList<Behaviour_Player> Players => _players;
        public static int Count => _players.Count;
        public static bool AnyAlive => _players.Any(p => p != null && p.Entity != null && p.Entity.IsAlive);
        public static void Init()
        {
            _players.Clear();
            CoopPlugin.FileLog("PlayerRegistry initialized.");
        }
        public static void Register(Behaviour_Player player)
        {
            int stale = _players.RemoveAll(p => p == null);
            if (stale > 0)
                CoopPlugin.FileLog($"PlayerRegistry: cleaned {stale} stale entries.");
            if (!_players.Contains(player))
            {
                _players.Add(player);
                CoopPlugin.FileLog($"PlayerRegistry: registered {player.name} (#{_players.Count})");
            }
        }
        public static bool IsRegistered(Behaviour_Player player)
        {
            return _players.Contains(player);
        }
        public static void Unregister(Behaviour_Player player)
        {
            _players.Remove(player);
            CoopPlugin.FileLog($"PlayerRegistry: unregistered player. Count={_players.Count}");
        }
        public static Behaviour_Player GetNearest(Vector2 position)
        {
            Behaviour_Player nearest = null;
            float bestDist = float.MaxValue;
            foreach (var p in _players)
            {
                if (p == null || p.Entity == null || !p.Entity.IsAlive) continue;
                float dist = ((Vector2)p.transform.position - position).sqrMagnitude;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    nearest = p;
                }
            }
            return nearest;
        }
        public static Behaviour_Player GetPlayer(int index)
        {
            if (index >= 0 && index < _players.Count)
                return _players[index];
            return null;
        }
    }
}