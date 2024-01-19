/*
    HideVAB - A KSP2 mod to allow hiding of the VAB building.

    Copyright (C) 2024 Jacob Burbach (aka JakeInABoat)

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License, version 3, as
    published by the Free Software Foundation.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/
using System.Collections;
using BepInEx;
using HarmonyLib;
using JetBrains.Annotations;
using KSP.Messages;
using KSP.UI.Binding;
using SpaceWarp;
using SpaceWarp.API.Assets;
using SpaceWarp.API.Mods;
using SpaceWarp.API.UI.Appbar;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;


namespace HideVAB;


[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency(SpaceWarpPlugin.ModGuid, SpaceWarpPlugin.ModVer)]
public class HideVABPlugin : BaseSpaceWarpPlugin
{
    // Useful in case some other mod wants to use this mod a dependency
    [PublicAPI] public const string ModGuid = MyPluginInfo.PLUGIN_GUID;
    [PublicAPI] public const string ModName = MyPluginInfo.PLUGIN_NAME;
    [PublicAPI] public const string ModVer = MyPluginInfo.PLUGIN_VERSION;

    // Singleton instance of the plugin class
    [PublicAPI] public static HideVABPlugin Instance { get; set; }

    public static string Path { get; private set; }

    private const string ToolbarOabButtonID = "BTN-HideVABOAB";
    private const string HideVAB_Enclosure_Key = "HideVAB_Enclosure";
    private const float CameraBoundsScaleFactor = 20.0f;

    public bool IsActive { get; private set; } = false;

    private Vector3 m_savedCameraLimitColliderScale = Vector3.one;
    private GameObject m_hideVABEnclosure = null;

    private GameObject[] m_vabObjectsToHide = null;
    private string[] m_vabObjectsToHideNames = {
        "OAB(Clone)/ENVSpawner/Env-Terrestrial(Clone)/UI-Editor_Interior/UI-Editor_Interior_AmbientObjects",
        "OAB(Clone)/ENVSpawner/Env-Terrestrial(Clone)/UI-Editor_Interior/CUTSCENES",
        "OAB(Clone)/ENVSpawner/Env-Terrestrial(Clone)/UI-Editor_Interior/ShadowCaster",
        "OAB(Clone)/ENVSpawner/Env-Terrestrial(Clone)/UI-Editor_Interior/Model/Exterior",
        "OAB(Clone)/ENVSpawner/Env-Terrestrial(Clone)/UI-Editor_Interior/Model/Interior/VAB_Interior_Windows01",
        "OAB(Clone)/ENVSpawner/Env-Terrestrial(Clone)/UI-Editor_Interior/Model/Interior/VAB_Interior",
        "OAB(Clone)/ENVSpawner/Env-Terrestrial(Clone)/UI-Editor_Interior/Model/Interior/VAB_InteriorFloor",
        "OAB(Clone)/ENVSpawner/Env-Terrestrial(Clone)/UI-Editor_Interior/Model/Interior/VAB_Lights/VAB_Lights_Point",
    };


    public override void OnPreInitialized()
    {
        base.OnPreInitialized();
        Path = PluginFolderPath;
    }

    public override void OnInitialized()
    {
        base.OnInitialized();

        Instance = this;
        IsActive = false;

        // Register OAB AppBar Button
        Appbar.RegisterOABAppButton(
            ModName,
            ToolbarOabButtonID,
            AssetManager.GetAsset<Texture2D>($"{ModGuid}/images/icon.png"),
            OnOABAppButtonToggled);

        // Register all Harmony patches in the project
        Harmony.CreateAndPatchAll(typeof(HideVABPlugin).Assembly);
    }

    public override void OnPostInitialized()
    {
        base.OnPostInitialized();

        Messages.Subscribe<OABLoadedMessage>(OnOABLoaded);
        Messages.Subscribe<GameStateChangedMessage>(OnGameStateChanged);
    }

    private void OnOABAppButtonToggled(bool active)
    {
        IsActive = active;

        KSP.OAB.ObjectAssemblyBuilder builder = KSP.Game.GameManager.Instance.Game.OAB.Current;

        if (IsActive && builder.CameraManager.CameraOrthoMode()) {
            // If activated while in ortho/blueprint mode then need to
            // reset visibility of objects game has already hidden and
            // then hide only ours.
            foreach (var go in builder.CurrentEnvironment.ObjectsToHideInOrthoMode) {
                go?.SetActive(true);
            }
            foreach (var go in m_vabObjectsToHide) {
                go?.SetActive(false);
            }
        }
        else if (!IsActive && builder.CameraManager.CameraOrthoMode()) {
            // If de-activated while in ortho/blueprint mode then need to
            // reset visibility of our objects and hide those the game would
            // normally hide.
            foreach (var go in m_vabObjectsToHide) {
                go?.SetActive(true);
            }
            foreach (var go in builder.CurrentEnvironment.ObjectsToHideInOrthoMode) {
                go?.SetActive(false);
            }
        }
        else {
            // Otherwise just toggle visibility of our desired vab objects.
            foreach (GameObject go in m_vabObjectsToHide) {
                go?.SetActive(!IsActive);
            }
        }

        // Toggle visibility of enclosure.
        m_hideVABEnclosure?.SetActive(IsActive);

        if (IsActive) {
            // Camera movement is restrained by a (mesh) collider. Save its default scale and then
            // scale it up by CameraBoundsScaleFactor to allow camera to increase freedom of movement.
            m_savedCameraLimitColliderScale = builder.BuilderAssets.cameraPositionLimitCollider.transform.localScale;
            builder.BuilderAssets.cameraPositionLimitCollider.transform.localScale *= CameraBoundsScaleFactor;
            builder.CameraManager.Camera.farClipPlane += 5000f;
        }
        else {
            // Restore original collider scale.
            builder.BuilderAssets.cameraPositionLimitCollider.transform.localScale = m_savedCameraLimitColliderScale;
            builder.CameraManager.Camera.farClipPlane -= 5000f;
        }

        GameObject.Find(ToolbarOabButtonID)?.GetComponent<UIValue_WriteBool_Toggle>()?.SetValue(IsActive);

    }

    private void OnOABLoaded(MessageCenterMessage message)
    {
        m_vabObjectsToHide = new GameObject[m_vabObjectsToHideNames.Length];
        for (int i = 0; i < m_vabObjectsToHideNames.Length; ++i) {
            m_vabObjectsToHide[i] = GameObject.Find(m_vabObjectsToHideNames[i]);
        }

        if (m_hideVABEnclosure == null) {
            AsyncOperationHandle<GameObject> handle = Addressables.LoadAssetAsync<GameObject>(HideVAB_Enclosure_Key);
            handle.Completed += OnEnclosureLoadingCompleted;
        }
    }

    private void OnGameStateChanged(MessageCenterMessage message)
    {
        GameStateChangedMessage stateChangedMessage = message as GameStateChangedMessage;
        if (stateChangedMessage.PreviousState == KSP.Game.GameState.VehicleAssemblyBuilder) {
            IsActive = false;
            m_vabObjectsToHide = null;
            m_hideVABEnclosure.SetActive(false);
        }
    }

    private void OnEnclosureLoadingCompleted(AsyncOperationHandle<GameObject> handle)
    {
        if (handle.Status == AsyncOperationStatus.Succeeded) {
            Debug.Log("HideVAB_Enclosure loaded");
            m_hideVABEnclosure = Instantiate(handle.Result);
            m_hideVABEnclosure.SetActive(false);
        }
        else {
            Debug.Log("HideVAB_Enclosure failed to load");
        }
    }

    public void OnBlueprintModeChanged(bool enabled)
    {
        Logger.LogInfo("OnBlueprintModeChanged: enabled=" + enabled.ToString());
        StartCoroutine(_OnBlueprintModeChanged(enabled));
    }

    private IEnumerator _OnBlueprintModeChanged(bool enabled)
    {
        KSP.OAB.ObjectAssemblyBuilder builder = KSP.Game.GameManager.Instance.Game.OAB.Current;

        // Camera state gets a little off when switching between blueprint mode. Re-toggling ortho
        // over a couple frames fixes it up...

        yield return new WaitForUpdate();
        builder.CameraManager.SetOrthoMode(!enabled);

        yield return new WaitForUpdate();
        builder.CameraManager.SetOrthoMode(enabled);
    }
}


[HarmonyPatch(
    typeof(KSP.OAB.ObjectAssemblyBuilder),
    nameof(KSP.OAB.ObjectAssemblyBuilder.ShowStructureObjects))]
class Patch__ObjectAssemblyBuilder_ShowStructureObjects
{
    static bool Prefix(bool show)
    {
        // If HideVAB is active then we are handling visibility, so just
        // skip the original method altogether.
        if (HideVABPlugin.Instance && HideVABPlugin.Instance.IsActive) {
            return false;
        }

        // Otherwise let the original method do its thing.
        return true;
    }
}

[HarmonyPatch(
    typeof(KSP.OAB.OABAudioEventManager),
    nameof(KSP.OAB.OABAudioEventManager.onVABEnterBlueprintMode))]
class Patch_OABAudioEventManager_onVABEnterBlueprintMode
{
    static void Postfix()
    {
        HideVABPlugin.Instance?.OnBlueprintModeChanged(true);
    }
}

[HarmonyPatch(
    typeof(KSP.OAB.OABAudioEventManager),
    nameof(KSP.OAB.OABAudioEventManager.onVABExitBlueprintMode))]
class Patch_OABAudioEventManager_onVABExitBlueprintMode
{
    static void Postfix()
    {
        HideVABPlugin.Instance?.OnBlueprintModeChanged(false);
    }
}
