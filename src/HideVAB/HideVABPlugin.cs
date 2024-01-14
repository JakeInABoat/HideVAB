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
using BepInEx;
using HarmonyLib;
using JetBrains.Annotations;
using KSP.UI.Binding;
using SpaceWarp;
using SpaceWarp.API.Assets;
using SpaceWarp.API.Mods;
using SpaceWarp.API.UI.Appbar;
using UnityEngine;


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

    // AppBar button IDs
    private const string ToolbarOabButtonID = "BTN-HideVABOAB";

    private const float CameraBoundsScaleFactor = 20.0f;

    public bool IsActive => m_isActive;
    private bool m_isActive = false;

    private Vector3 m_savedCameraLimitColliderScale = Vector3.one;

    /// <summary>
    /// Runs when the mod is first initialized.
    /// </summary>
    public override void OnInitialized()
    {
        base.OnInitialized();

        Instance = this;

        // Register OAB AppBar Button
        Appbar.RegisterOABAppButton(
            ModName,
            ToolbarOabButtonID,
            AssetManager.GetAsset<Texture2D>($"{ModGuid}/images/icon.png"),
            isActive =>
            {
                m_isActive = isActive;

                KSP.OAB.ObjectAssemblyBuilder builder = KSP.Game.GameManager.Instance.Game.OAB.Current;

                foreach(GameObject obj in builder.CurrentEnvironment.ObjectsToHideInOrthoMode) {
                    obj?.SetActive(!m_isActive);
                }

                if (m_isActive) {
                    // Camera movement is restrained by a (mesh) collider. Save its default scale and then
                    // scale it up by CameraBoundsScaleFactor to allow camera to increase freedom of movement.
                    m_savedCameraLimitColliderScale = builder.BuilderAssets.cameraPositionLimitCollider.transform.localScale;
                    builder.BuilderAssets.cameraPositionLimitCollider.transform.localScale *= CameraBoundsScaleFactor;
                }
                else {
                    // Restore original collider scale.
                    builder.BuilderAssets.cameraPositionLimitCollider.transform.localScale = m_savedCameraLimitColliderScale;
                }

                GameObject.Find(ToolbarOabButtonID)?.GetComponent<UIValue_WriteBool_Toggle>()?.SetValue(m_isActive);
            }
        );

        // Register all Harmony patches in the project
        Harmony.CreateAndPatchAll(typeof(HideVABPlugin).Assembly);
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