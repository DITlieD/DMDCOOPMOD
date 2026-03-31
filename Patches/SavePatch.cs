using HarmonyLib;
using Death.App;
namespace DeathMustDieCoop.Patches
{
    [HarmonyPatch(typeof(ProfileManager), "SaveActiveProfileAsync")]
    public static class ProfileManager_Save_Patch
    {
        static void Postfix()
        {
            CoopP2Profile.SaveToCoopData();
            CoopP2Save.SaveIfDirty();
        }
    }
}