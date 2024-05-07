using BepInEx;
using HarmonyLib;
using BepInEx.Logging;
using System.Reflection;
using System.Collections.Generic;
using System;
using Unity.Netcode;
using UnityEngine.SceneManagement;

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
        SceneManager.sceneLoaded += OnSceneLoaded;
        #if DEBUG
        if (SceneManager.GetActiveScene().name == "MainMenu") {
            NetcodeInit();
        }
        #endif
        ConfigInit();
        harmony.PatchAll(Assembly.GetExecutingAssembly());
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
        if (prefabContainer != null) {
            foreach (var a in sceneChangeActions) {
                a();
            }
        }
    }

    private void OnDestroy() {
        #if DEBUG
        var cleanup = () => {
            NetcodeUnload();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            harmony?.UnpatchSelf();
            foreach (var action in cleanupActions) {
                action();
            }
        };
        if (NetworkManager.Singleton.IsConnectedClient) {
            GameNetworkManager.Instance?.Disconnect();
            Action<bool>? callback = null;
            callback = (bool arg) => {
                cleanup();
                NetworkManager.Singleton.OnClientStopped -= callback;
            };
            NetworkManager.Singleton.OnClientStopped += callback;
        } else {
            cleanup();
        }
        log?.LogInfo($"Unloading {modGUID}");
        #endif
    }
}
