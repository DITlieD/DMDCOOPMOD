using UnityEngine;
using Death.Run.Core;
using Death.Run.Core.Entities;
using System.Collections.Generic;
namespace DeathMustDieCoop
{
    public class PerfSpatialGrid
    {
        public static PerfSpatialGrid Instance { get; private set; }
        private const float CellSize = 6f;
        private const float InvCellSize = 1f / CellSize;
        private readonly Dictionary<long, List<Entity>> _grid = new Dictionary<long, List<Entity>>(128);
        private readonly List<List<Entity>> _activeLists = new List<List<Entity>>(64);
        private readonly List<List<Entity>> _listPool = new List<List<Entity>>(64);
        private readonly List<Entity> _queryBuffer = new List<Entity>(32);
        private int _lastRebuildFrame = -1;
        public static void EnsureExists()
        {
            if (Instance == null)
                Instance = new PerfSpatialGrid();
        }
        public static void Destroy()
        {
            Instance = null;
        }
        public void Rebuild()
        {
            int frame = Time.frameCount;
            if (frame == _lastRebuildFrame) return;
            _lastRebuildFrame = frame;
            PerfStats.StartGridRebuild();
            for (int i = 0; i < _activeLists.Count; i++)
            {
                _activeLists[i].Clear();
                _listPool.Add(_activeLists[i]);
            }
            _activeLists.Clear();
            _grid.Clear();
            var monsters = EntityManager.Monsters;
            int count = monsters.Count;
            int alive = 0;
            for (int i = 0; i < count; i++)
            {
                var entity = monsters[i];
                if (entity == null || entity.IsDead) continue;
                alive++;
                Vector2 pos = entity.Position;
                long key = CellKey(pos.x, pos.y);
                if (!_grid.TryGetValue(key, out var list))
                {
                    list = _listPool.Count > 0 ? PopList() : new List<Entity>(8);
                    _grid[key] = list;
                    _activeLists.Add(list);
                }
                list.Add(entity);
            }
            PerfStats.EndGridRebuild(alive, _grid.Count);
        }
        public List<Entity> QueryNearby(Vector2 center, float radius)
        {
            _queryBuffer.Clear();
            int minX = FloorToInt((center.x - radius) * InvCellSize);
            int maxX = FloorToInt((center.x + radius) * InvCellSize);
            int minY = FloorToInt((center.y - radius) * InvCellSize);
            int maxY = FloorToInt((center.y + radius) * InvCellSize);
            float radiusSqr = radius * radius;
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    long key = ((long)x << 32) | (uint)y;
                    if (_grid.TryGetValue(key, out var list))
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            var e = list[i];
                            float dx = e.Position.x - center.x;
                            float dy = e.Position.y - center.y;
                            if (dx * dx + dy * dy <= radiusSqr)
                                _queryBuffer.Add(e);
                        }
                    }
                }
            }
            return _queryBuffer;
        }
        private List<Entity> PopList()
        {
            int last = _listPool.Count - 1;
            var list = _listPool[last];
            _listPool.RemoveAt(last);
            return list;
        }
        private static long CellKey(float px, float py)
        {
            int x = FloorToInt(px * InvCellSize);
            int y = FloorToInt(py * InvCellSize);
            return ((long)x << 32) | (uint)y;
        }
        private static int FloorToInt(float v)
        {
            int i = (int)v;
            return (v < i) ? i - 1 : i;
        }
    }
}