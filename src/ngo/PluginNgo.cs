using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Events;


namespace BetterSprayPaint.Ngo {

static class NgoHelper {
    #if DEBUG
    static HashSet<uint> rpcKeys = new HashSet<uint>();
    #endif

    internal static List<Action> cleanupActions = new List<Action>();

    internal static GameObject? prefabContainer = null;

    internal static void RegisterPrefab<T>(GameObject prefab, string name) where T : MonoBehaviour {
        prefab.AddComponent<T>();
        prefab.hideFlags = HideFlags.HideAndDontSave;
        prefab.transform.SetParent(prefabContainer!.transform);
        var networkObject = prefab.AddComponent<NetworkObject>();
        var hashInput = $"{Plugin.modGUID}.{name}";
        var hash = MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(hashInput));
        networkObject.GlobalObjectIdHash = BitConverter.ToUInt32(hash, 0);
        NetworkManager.Singleton.AddNetworkPrefab(prefab);
        cleanupActions.Add(() => {
            var instances = (MonoBehaviour[])Resources.FindObjectsOfTypeAll<T>();
            foreach (var instance in instances) {
                NetworkManager.Singleton.RemoveNetworkPrefab(prefab);
                GameObject.Destroy(instance.gameObject);
            }
        });
    }

    internal static void AddScriptToInstances<TCustomBehaviour, TNativeBehaviour>(bool updatePrefabs = false, Type? excluded = null)
        where TCustomBehaviour : NetworkBehaviour
        where TNativeBehaviour : NetworkBehaviour
    {
        foreach (var s in Resources.FindObjectsOfTypeAll<TNativeBehaviour>()) {
            if (excluded != null && s.GetType().IsInstanceOfType(excluded)) {
                continue;
            }
            if (s.gameObject.GetComponent<TCustomBehaviour>() != null) {
                continue;
            }
            bool isPrefab = !s.gameObject.scene.IsValid();
            if (isPrefab && updatePrefabs) {
                var networkPrefabs = NetworkManager.Singleton.NetworkConfig.Prefabs;
                var networkObject = s.gameObject.GetComponent<NetworkObject>();
                NetworkPrefab networkPrefab = networkPrefabs.m_Prefabs.Find(p => p.SourcePrefabGlobalObjectIdHash == networkObject.GlobalObjectIdHash);
                foreach (var list in networkPrefabs.NetworkPrefabsLists) {
                    list.Remove(networkPrefab);
                }
                NetworkManager.Singleton.RemoveNetworkPrefab(s.gameObject);
                if (networkPrefab?.SourcePrefabGlobalObjectIdHash != null) {
                    networkPrefabs.NetworkPrefabOverrideLinks.Remove(networkPrefab.SourcePrefabGlobalObjectIdHash);
                }
                if (networkPrefab?.TargetPrefabGlobalObjectIdHash != null) {
                    networkPrefabs.OverrideToNetworkPrefab.Remove(networkPrefab.TargetPrefabGlobalObjectIdHash);
                }
            }
            var component = s.gameObject.AddComponent<TCustomBehaviour>();
            component.SyncWithNetworkObject(s.gameObject.GetComponent<NetworkObject>());
            if (isPrefab && updatePrefabs) {
                NetworkManager.Singleton.AddNetworkPrefab(s.gameObject);
            }
        }
    }

    internal static void RegisterScriptWithExistingPrefab<TCustomBehaviour, TNativeBehaviour>(Type? excluded = null)
        where TCustomBehaviour : NetworkBehaviour
        where TNativeBehaviour : NetworkBehaviour
    {
        // Adding new NetworkBehaviour to prefab is viable when the object is instanced from the prefab at runtime
        AddScriptToInstances<TCustomBehaviour, TNativeBehaviour>(updatePrefabs: true, excluded);
        // Needed if a scene is later loaded that contains pre-existing instances of TNativeBehavior
        UnityAction<Scene, LoadSceneMode> handler = (_, _) => AddScriptToInstances<TCustomBehaviour, TNativeBehaviour>(excluded: excluded);
        SceneManager.sceneLoaded += handler;
        cleanupActions.Add(() => {
            SceneManager.sceneLoaded -= handler;
            foreach (var s in Resources.FindObjectsOfTypeAll<TNativeBehaviour>()) { GameObject.Destroy(s.gameObject.GetComponent<TCustomBehaviour>()); }
        });
        // For other cases, one can try patching Awake() to add the NetworkBehaviour then
        BindToPreExistingObjectByBehaviourPatch<TCustomBehaviour, TNativeBehaviour>(excluded);
    }

    static void RegisterCustomScripts() {
        RegisterPrefab<SessionData>(SessionData.prefab, nameof(SessionData));
        NetworkManager.Singleton.OnServerStarted += SessionData.ServerSpawn;
        cleanupActions.Add(() => { NetworkManager.Singleton.OnServerStarted -= SessionData.ServerSpawn; });
        NetworkManager.Singleton.OnClientStopped += SessionData.Despawn;
        cleanupActions.Add(() => { NetworkManager.Singleton.OnClientStopped -= SessionData.Despawn; });

        RegisterScriptWithExistingPrefab<SprayPaintItemNetExt, SprayPaintItem>();
        RegisterScriptWithExistingPrefab<PlayerNetExt, PlayerControllerB>();
    }

    private static List<(Type custom, Type native, Type? excluded)> BehaviourBindings { get; } = new List<(Type, Type, Type?)>();
    public static void BindToPreExistingObjectByBehaviourPatch<TCustomBehaviour, TNativeBehaviour>(Type? excluded = null)
        where TCustomBehaviour : NetworkBehaviour where TNativeBehaviour : NetworkBehaviour
    {
        var custom = typeof(TCustomBehaviour);
        var native = typeof(TNativeBehaviour);
        BehaviourBindings.Add((custom, native, excluded));

        MethodInfo? methodInfo = null;
        try {
            methodInfo = native.GetMethod("Awake", BindingFlags.Instance);
        } catch (Exception) {}
        if (methodInfo != null) {
            MethodBase method = AccessTools.Method(native, "Awake");
            var hMethod = new HarmonyMethod(typeof(NgoHelper), nameof(BindBehaviourOnAwake));
            Plugin.harmony.Patch(method, postfix: hMethod);
        }
    }
    internal static void BindBehaviourOnAwake(NetworkBehaviour __instance, MethodBase __originalMethod) {
        var type = __instance.GetType();
        var items = BehaviourBindings.Where(obj =>
            (obj.native == __originalMethod.DeclaringType || type == obj.native)
            && (obj.excluded == null || !type.IsInstanceOfType(obj.excluded))
        );
        foreach (var it in items) {
            if (!__instance.gameObject.TryGetComponent(it.custom, out _)) {
                (__instance.gameObject.AddComponent(it.custom) as NetworkBehaviour)!.SyncWithNetworkObject(__instance.gameObject.GetComponent<NetworkObject>());
            }
        }
    }

    internal static void NetcodeInit() {
        // Inactive GameObject used to hold custom NetworkPrefabs
        // (setting the prefabs themselves to inactive would cause the NetworkObjects to spawn inactive on clients)
        if (prefabContainer != null) { throw new Exception($"Ran {nameof(NetcodeInit)}() more than once"); }
        prefabContainer = new GameObject();
        prefabContainer.SetActive(false);
        prefabContainer.hideFlags = HideFlags.HideAndDontSave;
        GameObject.DontDestroyOnLoad(prefabContainer);

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

    public static GameObject? FindBuiltinPrefabByScript<T>() where T : MonoBehaviour {
        GameObject? prefab = null;
        foreach (var s in Resources.FindObjectsOfTypeAll<T>()) {
            if (!s.gameObject.scene.IsValid()) {
                prefab = s.gameObject;
                break;
            }
        }
        return prefab;
    }

    public static void NetcodeUnload() {
        // Needed in order to hot-reload NGO code patched by EvaisaDev/UnityNetcodePatcher
        Plugin.log.LogInfo($"Unloading NGO for {Plugin.modName}");
        if (NetworkManager.Singleton?.isActiveAndEnabled == true) {
            #if DEBUG
            foreach (var key in rpcKeys) {
                var rpcTable = (Dictionary<uint, NetworkManager.RpcReceiveHandler>)typeof(NetworkManager).GetField("__rpc_func_table").GetValue(null);
                rpcTable.Remove(key);
            }
            #endif
            if (prefabContainer != null) {
                foreach (Transform child in prefabContainer!.transform) {
                    NetworkManager.Singleton.RemoveNetworkPrefab(child.gameObject);
                    GameObject.Destroy(child.gameObject);
                }
                GameObject.Destroy(prefabContainer);
            }
        }
        foreach (var action in cleanupActions) {
            action();
        }
        cleanupActions.Clear();
    }

}

[HarmonyPatch]
class NetcodeInitPatch {
    static bool loaded = false;
    // NOTE: Does not activate when loaded via ScriptEngine
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameNetworkManager), "Start")]
    public static void GameNetworkManagerStart(GameNetworkManager __instance) {
        if (!loaded && NetworkManager.Singleton != null) {
            if (NgoHelper.prefabContainer == null) {
                NgoHelper.NetcodeInit();
            }
            loaded = true;
        }
    }
}

}