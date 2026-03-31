using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Death.Run.Behaviours.Players;
namespace DeathMustDieCoop
{
    public class CoopPlugin
    {
        public static CoopPlugin Instance { get; private set; }
        public static string ModDir { get; private set; }
        private static string _logPath;
        public static void Init()
        {
            Instance = new CoopPlugin();
            ModDir = System.IO.Path.GetDirectoryName(typeof(CoopPlugin).Assembly.Location);
            _logPath = System.IO.Path.Combine(ModDir, "coop_debug.log");
            try { System.IO.File.WriteAllText(_logPath, ""); } catch { }
            FileLog("=== COOP MOD INIT (Doorstop, no BepInEx) ===");
            FileLog("Deferring ALL Unity work to sceneLoaded...");
            SceneManager.sceneLoaded += OnFirstSceneLoaded;
        }
        private static void OnFirstSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= OnFirstSceneLoaded;
            FileLog($"First scene loaded: {scene.name}. Initializing mod...");
            PlayerRegistry.Init();
            try { CoopP2Save.Load(); }
            catch (System.Exception ex) { FileLog($"CoopP2Save.Load failed: {ex.Message}"); }
            try
            {
                PatchManager.ApplyAll(typeof(CoopPlugin).Assembly);
                FileLog("PatchManager.ApplyAll succeeded.");
            }
            catch (System.Exception ex)
            {
                FileLog($"PatchManager.ApplyAll FAILED: {ex}");
            }
            foreach (var method in PatchManager.PatchedOriginals)
                FileLog($"  Patched: {method.DeclaringType?.FullName}.{method.Name}");
            var go = new GameObject("__CoopMod__");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<CoopRuntime>();
            FileLog("Init complete. All patches applied, CoopRuntime created.");
        }
        public static void FileLog(string msg)
        {
            try
            {
                System.IO.File.AppendAllText(_logPath,
                    $"[{System.DateTime.Now:HH:mm:ss.fff}] {msg}\n");
            }
            catch { }
        }
    }
    public class CoopRuntime : MonoBehaviour
    {
        public static CoopRuntime Instance { get; private set; }
        private int _frame;
        private float _diagTimer;
        private void Awake()
        {
            Instance = this;
            Application.quitting += OnApplicationQuitting;
            CoopPlugin.FileLog("CoopRuntime.Awake on DontDestroyOnLoad GO.");
        }
        private static void OnApplicationQuitting()
        {
            CoopPlugin.FileLog("CoopRuntime: Application.quitting — saving P2 data.");
            CoopP2Profile.SaveToCoopData();
            CoopP2Save.SaveIfDirty();
        }
        public static void EnsureExists()
        {
            if (Instance != null) return;
            var go = new GameObject("__CoopMod__");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<CoopRuntime>();
            CoopPlugin.FileLog("CoopRuntime: Recreated after destruction.");
        }
        private void Start()
        {
            CoopPlugin.FileLog("CoopRuntime.Start called.");
        }
        private void Update()
        {
            _frame++;
            PerfStats.RecordFrameTime();
            ExcaliburPatch.UpdateSmiteCooldown();
            ExcaliburPatch.CheckExcaliburEquipState();
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F9))
            {
                CoopPlugin.FileLog("DEBUG KEY: F9 pressed — giving Excalibur to P2");
                Behaviour_Player p2 = null;
                foreach (var p in PlayerRegistry.Players)
                {
                    bool isPrimary = Traverse.Create(p).Field("_isPrimaryPlayerInstance").GetValue<bool>();
                    if (!isPrimary) { p2 = p; break; }
                }
                if (p2 != null)
                {
                    ExcaliburPatch.DebugGiveExcalibur(p2);
                }
                else
                {
                    CoopPlugin.FileLog("DEBUG KEY: No P2 found, giving to P1 instead");
                    ExcaliburPatch.DebugGiveExcalibur(null);
                }
            }
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F10))
            {
                CoopPlugin.FileLog("DEBUG KEY: F10 pressed — dumping Excalibur state");
                ExcaliburPatch.DebugDumpState();
            }
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F11))
            {
                CoopPlugin.FileLog("DEBUG KEY: F11 pressed — simulating LadyB kill (forced drop)");
                if (Death.Run.Behaviours.Player.Exists)
                {
                    ExcaliburPatch.TryDropExcalibur(Death.Run.Behaviours.Player.Position, forceChance: true);
                }
                else
                {
                    CoopPlugin.FileLog("DEBUG: No player exists, can't drop.");
                }
            }
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F12))
            {
                CoopPlugin.FileLog("DEBUG KEY: F12 pressed — resetting Excalibur obtained flag");
                ExcaliburPatch.ResetExcaliburObtainedFlag();
            }
            _diagTimer -= Time.deltaTime;
            if (_diagTimer > 0f) return;
            _diagTimer = 3f;
            PerfStats.DumpAndReset(3f);
        }
        private void OnDestroy()
        {
            CoopPlugin.FileLog("CoopRuntime.OnDestroy — this should NOT happen!");
        }
    }
    public static class PluginInfo
    {
        public const string PLUGIN_GUID = "com.coop.deathmustdie";
        public const string PLUGIN_NAME = "Death Must Die Co-op";
        public const string PLUGIN_VERSION = "0.1.0";
    }
}