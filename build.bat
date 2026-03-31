@echo off
setlocal

set CSC="C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe"
set GAME=D:\.Death Must Die
set GAMEDIR=C:\Program Files (x86)\Steam\steamapps\common\Death Must Die
set MANAGED=%GAMEDIR%\Death Must Die_Data\Managed
set MODDIR=%GAMEDIR%\CoopMod
set OUT=%MODDIR%\DeathMustDieCoop.dll
set SRC=%GAME%\COOP

echo building Doorstop

%CSC% ^
  -target:library ^
  -out:"%OUT%" ^
  -langversion:9.0 ^
  -optimize ^
  -nowarn:CS0168,CS0219 ^
  -reference:"%MANAGED%\Death.dll" ^
  -reference:"%MANAGED%\Death.Achievements.dll" ^
  -reference:"%MANAGED%\Death.Utils.dll" ^
  -reference:"%MANAGED%\Death.ResourceManagement.dll" ^
  -reference:"%MANAGED%\Claw.Core.dll" ^
  -reference:"%MANAGED%\Claw.App.dll" ^
  -reference:"%MANAGED%\Claw.UserInterface.dll" ^
  -reference:"%MANAGED%\Assembly-CSharp.dll" ^
  -reference:"%MANAGED%\Unity.InputSystem.dll" ^
  -reference:"%MANAGED%\Cinemachine.dll" ^
  -reference:"%MANAGED%\UnityEngine.dll" ^
  -reference:"%MANAGED%\UnityEngine.CoreModule.dll" ^
  -reference:"%MANAGED%\UnityEngine.AnimationModule.dll" ^
  -reference:"%MANAGED%\UnityEngine.Physics2DModule.dll" ^
  -reference:"%MANAGED%\UnityEngine.ParticleSystemModule.dll" ^
  -reference:"%MANAGED%\UnityEngine.InputLegacyModule.dll" ^
  -reference:"%MANAGED%\UnityEngine.IMGUIModule.dll" ^
  -reference:"%MANAGED%\UnityEngine.TextRenderingModule.dll" ^
  -reference:"%MANAGED%\UnityEngine.UI.dll" ^
  -reference:"%MANAGED%\UnityEngine.UIModule.dll" ^
  -reference:"%MANAGED%\UnityEngine.JSONSerializeModule.dll" ^
  -reference:"%MANAGED%\Unity.TextMeshPro.dll" ^
  -reference:"%MANAGED%\UniTask.dll" ^
  -reference:"%MANAGED%\netstandard.dll" ^
  -reference:"%MANAGED%\mscorlib.dll" ^
  "%SRC%\NativeDetour.cs" ^
  "%SRC%\HarmonyShim.cs" ^
  "%SRC%\PatchManager.cs" ^
  "%SRC%\Plugin.cs" ^
  "%SRC%\PlayerRegistry.cs" ^
  "%SRC%\GamepadInputHandler.cs" ^
  "%SRC%\Patches\PlayerPatches.cs" ^
  "%SRC%\Patches\SpawnPatch.cs" ^
  "%SRC%\Patches\InputPatch.cs" ^
  "%SRC%\Patches\CameraPatch.cs" ^
  "%SRC%\Patches\AiTargetingPatch.cs" ^
  "%SRC%\Patches\HudPatch.cs" ^
  "%SRC%\Patches\RewardPatch.cs" ^
  "%SRC%\Patches\DeathPatch.cs" ^
  "%SRC%\CoopXpBar.cs" ^
  "%SRC%\CoopP2Save.cs" ^
  "%SRC%\Patches\SavePatch.cs" ^
  "%SRC%\CoopP2Profile.cs" ^
  "%SRC%\Patches\LobbyPatch.cs" ^
  "%SRC%\Patches\ShopPatch.cs" ^
  "%SRC%\Patches\HubScreenPatches.cs" ^
  "%SRC%\CoopRunCharacterInfo.cs" ^
  "%SRC%\Patches\PlayerInstancePatches.cs" ^
  "%SRC%\Patches\ResultsPatch.cs" ^
  "%SRC%\Patches\ExcaliburPatch.cs" ^
  "%SRC%\Patches\LootPatch.cs" ^
  "%SRC%\Patches\LightPatch.cs" ^
  "%SRC%\Patches\VisualClarityPatch.cs" ^
  "%SRC%\Patches\TalentPointPatch.cs" ^
  "%SRC%\Patches\SummonPatch.cs" ^
  "%SRC%\Patches\MinimapPatch.cs" ^
  "%SRC%\Patches\PowerUpDropPatch.cs" ^
  "%SRC%\Patches\BossArenaPatch.cs" ^
  "%SRC%\Patches\EncounterPatch.cs" ^
  "%SRC%\Patches\FudgeStatPatch.cs" ^
  "%SRC%\PerfStats.cs" ^
  "%SRC%\PerfSpatialGrid.cs" ^
  "%SRC%\Patches\PerfPatches.cs" ^
  "%SRC%\Patches\RenderPerfPatch.cs" ^
  "%SRC%\Patches\NewCharacterHookPatch.cs"

if %ERRORLEVEL% EQU 0 (
    echo.
    echo BUILD SUCCESSFUL.
) else (
    echo.
    echo BUILD FAILED
)

endlocal
