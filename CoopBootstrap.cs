using System;
using System.IO;
using System.Reflection;
namespace Doorstop
{
    public class Entrypoint
    {
        public static void Start()
        {
            string bootstrapDllPath = typeof(Entrypoint).Assembly.Location;
            string modDir = Path.GetDirectoryName(bootstrapDllPath);
            string logPath = Path.Combine(modDir, "bootstrap.log");
            try
            {
                File.WriteAllText(logPath, $"[{DateTime.Now}] Coop Bootstrap Start() entered.\n");
                File.AppendAllText(logPath, $"  modDir={modDir}\n");
                string[] dependencies = { "Mono.Cecil.dll", "MonoMod.RuntimeDetour.dll", "MonoMod.Utils.dll" };
                foreach (var dep in dependencies)
                {
                    string depPath = Path.Combine(modDir, dep);
                    if (File.Exists(depPath))
                    {
                        Assembly.LoadFrom(depPath);
                        File.AppendAllText(logPath, $"  Loaded dependency: {dep}\n");
                    }
                }
                string mainDllPath = Path.Combine(modDir, "DeathMustDieCoop.dll");
                if (!File.Exists(mainDllPath))
                {
                    File.AppendAllText(logPath, $"  ERROR: Could not find main DLL at {mainDllPath}\n");
                    return;
                }
                var modAsm = Assembly.LoadFrom(mainDllPath);
                File.AppendAllText(logPath, "  Main Mod DLL loaded.\n");
                var initType = modAsm.GetType("DeathMustDieCoop.CoopPlugin");
                if (initType == null)
                {
                    File.AppendAllText(logPath, "  ERROR: Could not find type DeathMustDieCoop.CoopPlugin\n");
                    return;
                }
                var initMethod = initType.GetMethod("Init", BindingFlags.Public | BindingFlags.Static);
                if (initMethod == null)
                {
                    File.AppendAllText(logPath, "  ERROR: Could not find Init() method in CoopPlugin\n");
                    return;
                }
                initMethod.Invoke(null, null);
                File.AppendAllText(logPath, "  CoopPlugin.Init() completed successfully.\n");
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(logPath, $"  FAILED: {ex}\n"); } catch { }
            }
        }
    }
}