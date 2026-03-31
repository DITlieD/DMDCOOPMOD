using HarmonyLib;
using Death.Run.Behaviours.Players;
using Death.Run.Behaviours.Objects;
using Death.Run.Encounters;
using System.Reflection;
namespace DeathMustDieCoop.Patches
{
    public static class EncounterPatch
    {
        public static Behaviour_Player LastInteractingPlayer;
        public static readonly FieldInfo ContextPlayerField =
            typeof(EncounterContext).GetField("Player", BindingFlags.Public | BindingFlags.Instance);
    }
    [HarmonyPatch(typeof(Interactable), "Interact")]
    public static class Interactable_Interact_EncounterCapture_Patch
    {
        static void Prefix(Behaviour_Player player)
        {
            EncounterPatch.LastInteractingPlayer = player;
        }
    }
    [HarmonyPatch(typeof(EncounterChoice), "Trigger")]
    public static class EncounterChoice_Trigger_Patch
    {
        static void Prefix(EncounterContext context)
        {
            var correctPlayer = EncounterPatch.LastInteractingPlayer;
            if (correctPlayer == null || correctPlayer == context.Player)
                return;
            if (correctPlayer.Entity == null || !correctPlayer.Entity.IsAlive)
                return;
            var originalPlayer = context.Player;
            EncounterPatch.ContextPlayerField.SetValue(context, correctPlayer);
            CoopPlugin.FileLog($"EncounterPatch: Swapped context.Player from {originalPlayer?.name} to {correctPlayer.name}");
        }
    }
}