using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace BetterSprayPaint;

public partial class Plugin {

    #if DEBUG
    static HashSet<uint> rpcKeys = new HashSet<uint>();
    #endif

    internal static GameObject? prefabContainer = null;

    internal static void RegisterPrefab<T>(GameObject prefab) where T : MonoBehaviour {
        prefab.AddComponent<T>();
        prefab.hideFlags = HideFlags.HideAndDontSave;
        prefab.transform.SetParent(prefabContainer!.transform);
        prefab.AddComponent<NetworkObject>();
        NetworkManager.Singleton.AddNetworkPrefab(prefab);
        cleanupActions.Add(() => {
            var instances = (MonoBehaviour[])Resources.FindObjectsOfTypeAll<T>();
            foreach (var instance in instances) {
                Destroy(instance.gameObject);
            }
        });
    }

    internal static void RegisterScriptWithExistingPrefab<TNew, TExisting>()
        where TNew : MonoBehaviour
        where TExisting : MonoBehaviour
    {
        foreach (var s in Resources.FindObjectsOfTypeAll<TExisting>()) { s.gameObject.AddComponent<TNew>(); }
        cleanupActions.Add(() => {
            foreach (var s in Resources.FindObjectsOfTypeAll<TExisting>()) { Destroy(s.gameObject.GetComponent<TNew>()); }
        });
    }

    static void RegisterCustomScripts() {
        RegisterPrefab<SessionData>(SessionData.prefab);
        NetworkManager.Singleton.OnServerStarted += SessionData.ServerSpawn;
        cleanupActions.Add(() => { NetworkManager.Singleton.OnServerStarted -= SessionData.ServerSpawn; });
        NetworkManager.Singleton.OnClientStopped += SessionData.Despawn;
        cleanupActions.Add(() => { NetworkManager.Singleton.OnClientStopped -= SessionData.Despawn; });

        RegisterScriptWithExistingPrefab<SprayPaintItemExt, SprayPaintItem>();
        RegisterScriptWithExistingPrefab<PlayerExt, PlayerControllerB>();
    }

    internal static void NetcodeInit() {
        // Inactive GameObject used to hold custom NetworkPrefabs
        // (setting the prefabs themselves to inactive would cause the NetworkObjects to spawn inactive on clients)
        if (prefabContainer != null) { throw new Exception($"Ran {nameof(NetcodeInit)}() more than once"); }
        prefabContainer = new GameObject();
        prefabContainer.SetActive(false);
        prefabContainer.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(prefabContainer);

        RegisterCustomScripts();

        // Initializing NGO code patched by EvaisaDev/UnityNetcodePatcher
        #if DEBUG
        var rpcTable = (Dictionary<uint, NetworkManager.RpcReceiveHandler>)typeof(NetworkManager).GetField("__rpc_func_table").GetValue(null);
        var rpcKeysBefore = rpcTable.Keys.ToHashSet();
        #endif
        var types = Assembly.GetExecutingAssembly().GetTypes();
        foreach (var type in types) {
            var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (var method in methods) {
                var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                if (attributes.Length > 0) {
                        method.Invoke(null, null);
                }
            }
        }
        #if DEBUG
        var rpcKeysAfter = rpcTable.Keys.ToHashSet();
        rpcKeys = rpcKeysAfter.Where((key) => !rpcKeysBefore.Contains(key)).ToHashSet();
        #endif
    }

    static GameObject? FindBuiltinPrefabByScript<T>() where T : MonoBehaviour {
        GameObject? prefab = null;
        foreach (var s in Resources.FindObjectsOfTypeAll<T>()) {
            if (!s.gameObject.scene.IsValid()) {
                prefab = s.gameObject;
                break;
            }
        }
        return prefab;
    }

    static void NetcodeUnload() {
        // Needed in order to hot-reload NGO code patched by EvaisaDev/UnityNetcodePatcher
        #if DEBUG
        foreach (var key in rpcKeys) {
            var rpcTable = (Dictionary<uint, NetworkManager.RpcReceiveHandler>)typeof(NetworkManager).GetField("__rpc_func_table").GetValue(null);
            rpcTable.Remove(key);
        }
        #endif
        foreach (Transform child in prefabContainer!.transform) {
            NetworkManager.Singleton.RemoveNetworkPrefab(child.gameObject);
            Destroy(child.gameObject);
        }
        Destroy(prefabContainer);
    }
}

[HarmonyPatch]
class NetcodeInitPatch {
    [HarmonyPrefix]
    [HarmonyPatch(typeof(MenuManager), "Awake")]
    public static void MenuAwake(PreInitSceneScript __instance) {
        if (Plugin.prefabContainer == null) {
            Plugin.NetcodeInit();
        }
    }
}