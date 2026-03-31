using UnityEngine;
using System.Diagnostics;
namespace DeathMustDieCoop
{
    public static class PerfStats
    {
        public static int GridEntityCount;
        public static int GridCellCount;
        public static float GridRebuildMs;
        public static int TargetQueries;
        public static int TargetFastPath;
        public static int TargetGridScan;
        public static int TargetNoGrid;
        public static int FixedClose;
        public static int FixedOffRan;
        public static int FixedOffSkipped;
        public static int BrainFull;
        public static int BrainOffLite;
        public static float FrameTimeMin = float.MaxValue;
        public static float FrameTimeMax;
        private static float _frameTimeSum;
        private static int _frameTimeCount;
        public static double TimeBrainMs;
        public static int TimeBrainCalls;
        public static double TimeAiFixedMs;
        public static int TimeAiFixedCalls;
        public static double TimeSteeringMs;
        public static int TimeSteeringCalls;
        public static double TimePhysFixedMs;
        public static int TimePhysFixedCalls;
        public static double TimeRenderEstMs;
        public static double TimeUpdateMs;
        public static int TimeUpdateCalls;
        private static readonly Stopwatch _swGrid = new Stopwatch();
        private static readonly Stopwatch _swBrain = new Stopwatch();
        private static readonly Stopwatch _swAiFixed = new Stopwatch();
        private static readonly Stopwatch _swSteering = new Stopwatch();
        private static readonly Stopwatch _swPhysFixed = new Stopwatch();
        private static readonly Stopwatch _swUpdate = new Stopwatch();
        public static void StartGridRebuild() { _swGrid.Restart(); }
        public static void EndGridRebuild(int entityCount, int cellCount)
        {
            _swGrid.Stop();
            GridRebuildMs = (float)_swGrid.Elapsed.TotalMilliseconds;
            GridEntityCount = entityCount;
            GridCellCount = cellCount;
        }
        public static void StartBrain() { _swBrain.Restart(); }
        public static void EndBrain()
        {
            _swBrain.Stop();
            TimeBrainMs += _swBrain.Elapsed.TotalMilliseconds;
            TimeBrainCalls++;
        }
        public static void StartAiFixed() { _swAiFixed.Restart(); }
        public static void EndAiFixed()
        {
            _swAiFixed.Stop();
            TimeAiFixedMs += _swAiFixed.Elapsed.TotalMilliseconds;
            TimeAiFixedCalls++;
        }
        public static void StartSteering() { _swSteering.Restart(); }
        public static void EndSteering()
        {
            _swSteering.Stop();
            TimeSteeringMs += _swSteering.Elapsed.TotalMilliseconds;
            TimeSteeringCalls++;
        }
        public static void StartPhysFixed() { _swPhysFixed.Restart(); }
        public static void EndPhysFixed()
        {
            _swPhysFixed.Stop();
            TimePhysFixedMs += _swPhysFixed.Elapsed.TotalMilliseconds;
            TimePhysFixedCalls++;
        }
        public static void StartUpdate() { _swUpdate.Restart(); }
        public static void EndUpdate()
        {
            _swUpdate.Stop();
            TimeUpdateMs += _swUpdate.Elapsed.TotalMilliseconds;
            TimeUpdateCalls++;
        }
        public static void RecordFrameTime()
        {
            float dt = Time.deltaTime;
            if (dt < FrameTimeMin) FrameTimeMin = dt;
            if (dt > FrameTimeMax) FrameTimeMax = dt;
            _frameTimeSum += dt;
            _frameTimeCount++;
        }
        public static void DumpAndReset(float intervalSeconds)
        {
            float avgMs = _frameTimeCount > 0 ? (_frameTimeSum / _frameTimeCount) * 1000f : 0f;
            float minMs = FrameTimeMin < float.MaxValue ? FrameTimeMin * 1000f : 0f;
            float maxMs = FrameTimeMax * 1000f;
            float avgFps = avgMs > 0f ? 1000f / avgMs : 0f;
            int totalTarget = TargetFastPath + TargetGridScan + TargetNoGrid;
            int totalFixed = FixedClose + FixedOffRan + FixedOffSkipped;
            int fixedSaved = FixedOffSkipped;
            int totalBrain = BrainFull + BrainOffLite;
            int brainSaved = BrainOffLite;
            double totalMeasured = TimeUpdateMs + TimePhysFixedMs;
            double totalFrameMs = _frameTimeSum * 1000.0;
            TimeRenderEstMs = totalFrameMs - totalMeasured;
            if (TimeRenderEstMs < 0) TimeRenderEstMs = 0;
            double brainPerFrame = _frameTimeCount > 0 ? TimeBrainMs / _frameTimeCount : 0;
            double aiFixedPerFrame = _frameTimeCount > 0 ? TimeAiFixedMs / _frameTimeCount : 0;
            double steeringPerFrame = _frameTimeCount > 0 ? TimeSteeringMs / _frameTimeCount : 0;
            double updatePerFrame = _frameTimeCount > 0 ? TimeUpdateMs / _frameTimeCount : 0;
            double physFixedPerFrame = _frameTimeCount > 0 ? TimePhysFixedMs / _frameTimeCount : 0;
            double renderPerFrame = _frameTimeCount > 0 ? TimeRenderEstMs / _frameTimeCount : 0;
            CoopPlugin.FileLog(
                $"PERF[{intervalSeconds:F0}s]: " +
                $"fps={avgFps:F0} dt={avgMs:F1}/{minMs:F1}/{maxMs:F1}ms(avg/min/max) frames={_frameTimeCount} | " +
                $"grid: {GridEntityCount}ent {GridCellCount}cells {GridRebuildMs:F2}ms | " +
                $"target: {totalTarget}q fast={TargetFastPath} scan={TargetGridScan} nogrid={TargetNoGrid} | " +
                $"fixed: {totalFixed} on={FixedClose} offR={FixedOffRan} offS={FixedOffSkipped} saved={fixedSaved} | " +
                $"brain: {totalBrain} on={BrainFull} offLite={BrainOffLite} saved={brainSaved}"
            );
            CoopPlugin.FileLog(
                $"  TIME(ms/frame): " +
                $"total={avgMs:F2} | " +
                $"update={updatePerFrame:F2} (brain={brainPerFrame:F2}) | " +
                $"fixedAll={physFixedPerFrame:F2} (aiFixed={aiFixedPerFrame:F2} steering={steeringPerFrame:F2}) | " +
                $"other={renderPerFrame:F2} (render+particles+late+misc)"
            );
            CoopPlugin.FileLog(
                $"  TIME(total ms): " +
                $"update={TimeUpdateMs:F0} ({TimeUpdateCalls}calls) | " +
                $"brain={TimeBrainMs:F0} ({TimeBrainCalls}calls) | " +
                $"fixedAll={TimePhysFixedMs:F0} ({TimePhysFixedCalls}calls) | " +
                $"aiFixed={TimeAiFixedMs:F0} ({TimeAiFixedCalls}calls) | " +
                $"steering={TimeSteeringMs:F0} ({TimeSteeringCalls}calls) | " +
                $"other={TimeRenderEstMs:F0}"
            );
            GridEntityCount = 0;
            GridCellCount = 0;
            GridRebuildMs = 0f;
            TargetQueries = 0;
            TargetFastPath = 0;
            TargetGridScan = 0;
            TargetNoGrid = 0;
            FixedClose = 0;
            FixedOffRan = 0;
            FixedOffSkipped = 0;
            BrainFull = 0;
            BrainOffLite = 0;
            FrameTimeMin = float.MaxValue;
            FrameTimeMax = 0f;
            _frameTimeSum = 0f;
            _frameTimeCount = 0;
            TimeBrainMs = 0;
            TimeBrainCalls = 0;
            TimeAiFixedMs = 0;
            TimeAiFixedCalls = 0;
            TimeSteeringMs = 0;
            TimeSteeringCalls = 0;
            TimePhysFixedMs = 0;
            TimePhysFixedCalls = 0;
            TimeUpdateMs = 0;
            TimeUpdateCalls = 0;
            TimeRenderEstMs = 0;
        }
    }
}