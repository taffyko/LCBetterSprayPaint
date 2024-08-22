using BepInEx;
using HarmonyLib;
using BepInEx.Logging;
using System.Reflection;
using System.Collections.Generic;
using System;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using BetterSprayPaint.Ngo;
using UnityEngine;

namespace BetterSprayPaint;


[BepInPlugin(modGUID, modName, modVersion)]
[BepInDependency("com.rune580.LethalCompanyInputUtils")]
public partial class Plugin : BaseUnityPlugin {
    public const string modGUID = "taffyko.BetterSprayPaint";
    public const string modName = PluginInfo.PLUGIN_NAME;
    public const string modVersion = PluginInfo.PLUGIN_VERSION;
    
    internal static Harmony harmony = new Harmony(modGUID);
    internal static QuietLogSource log;

    internal static List<Action> cleanupActions = new List<Action>();

    internal static List<Action> sceneChangeActions = new List<Action>();

    static Plugin() {
        log = new QuietLogSource(modName);
        BepInEx.Logging.Logger.Sources.Add(log);
    }

    private void Awake() {
        log.LogInfo($"Loading {modGUID}");
        #if DEBUG
        if (!NetworkManager.Singleton.IsConnectedClient) {
            if (NgoHelper.prefabContainer == null) {
                NgoHelper.NetcodeInit();
            }
        }
        #endif
        ConfigInit();
        harmony.PatchAll(Assembly.GetExecutingAssembly());
    }

    private void OnDestroy() {
        #if DEBUG
        var cleanup = () => {
            if (!NetworkManager.Singleton.IsConnectedClient) {
                NgoHelper.NetcodeUnload();
            }
            harmony?.UnpatchSelf();
            foreach (var action in cleanupActions) {
                action();
            }
            foreach (var obj in FindObjectsOfType<SprayPaintItemExt>()) { Destroy(obj); }
            cleanupActions.Clear();
        };
        cleanup();
        log?.LogInfo($"Unloading {modGUID}");
        #endif
    }
}
