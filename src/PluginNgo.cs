using System;
using System.Collections.Generic;
using System.ComponentModel;
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
                NetworkManager.Singleton.RemoveNetworkPrefab(prefab);
                Destroy(instance.gameObject);
            }
        });
    }

    internal static void RegisterScriptWithExistingPrefab<TCustomBehaviour, TNativeBehaviour>()
        where TCustomBehaviour : NetworkBehaviour
        where TNativeBehaviour : NetworkBehaviour
    {
        // Adding new NetworkBehaviour to prefab is viable when the object is instanced from the prefab at runtime
        foreach (var s in Resources.FindObjectsOfTypeAll<TNativeBehaviour>()) {
            if (!s.gameObject.scene.IsValid()) {
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
            if (!s.gameObject.scene.IsValid()) {
                NetworkManager.Singleton.AddNetworkPrefab(s.gameObject);
            }

        }
        cleanupActions.Add(() => {
            foreach (var s in Resources.FindObjectsOfTypeAll<TNativeBehaviour>()) { Destroy(s.gameObject.GetComponent<TCustomBehaviour>()); }
        });
        // For other cases, one can try patching Awake() to add the NetworkBehaviour then
        BindToPreExistingObjectByBehaviourPatch<TCustomBehaviour, TNativeBehaviour>();
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

    private static List<(Type custom, Type native)> BehaviourBindings { get; } = new List<(Type, Type)>();
    public static void BindToPreExistingObjectByBehaviourPatch<TCustomBehaviour, TNativeBehaviour>()
        where TCustomBehaviour : NetworkBehaviour where TNativeBehaviour : NetworkBehaviour
    {
        var custom = typeof(TCustomBehaviour);
        var native = typeof(TNativeBehaviour);
        BehaviourBindings.Add((custom, native));
        
        MethodBase method = AccessTools.Method(native, "Awake");
        if (method != null) {
            var hMethod = new HarmonyMethod(typeof(Plugin), nameof(BindBehaviourOnAwake));
            harmony.Patch(method, postfix: hMethod);
        }
    }
    internal static void BindBehaviourOnAwake(NetworkBehaviour __instance, MethodBase __originalMethod) {
        var type = __instance.GetType();
        var items = BehaviourBindings.Where(obj => obj.native == __originalMethod.DeclaringType || type == obj.native);
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

interface INetVar : IDisposable {
    void Synchronize();
}

class NetVar<T> : INetVar where T : IEquatable<T> {
    readonly public NetworkVariable<T> networkVariable;

    bool deferPending = true;
    T deferredValue;

    // Always in sync with networkVariable.Value
    // The only time this will ever hold a different value is when this client is the owner
    // and the value has just been set and not yet made a round-trip back from the server
    private T localValue {
        set {
            if (isGlued) { setGlued!(value); }
            _localValue = value;
        }
        get {
            return _localValue;
        }
    }

    private T _localValue;

    // Getter/setter to synchronize value with an external local variable
    readonly bool isGlued;
    readonly Action<T>? setGlued;
    readonly Func<T>? getGlued;

    readonly Func<T, T>? validate;
    readonly Func<bool> inControl;
    readonly Action<T> SetOnServer;
    readonly NetworkVariable<T>.OnValueChangedDelegate? onChange;
    public NetVar(
        out NetworkVariable<T> networkVariable,
        Action<T> SetOnServer,
        Func<bool> inControl,
        T initialValue = default!,
        NetworkVariable<T>.OnValueChangedDelegate? onChange = null,
        Action<T>? setGlued = null,
        Func<T>? getGlued = null,
        Func<T, T>? validate = null
    ) {
        isGlued = setGlued != null && getGlued != null;
        this.setGlued = setGlued;
        this.getGlued = getGlued;

        this.validate = validate;
        this.onChange = onChange;
        this.SetOnServer = SetOnServer;
        this.inControl = inControl;

        this.networkVariable = new NetworkVariable<T>(initialValue);
        networkVariable = this.networkVariable;
        _localValue = initialValue;
        localValue = initialValue;
        deferredValue = initialValue;

        networkVariable.OnValueChanged += (prevValue, currentValue) => {
            if (!EqualityComparer<T>.Default.Equals(localValue, currentValue)) {
                Synchronize();
                OnChange(prevValue, currentValue);
            }
        };
    }

    public void ServerSet(T value) {
        if (validate != null) { value = validate(value); }
        networkVariable.Value = value;
    }

    public void SetDeferred(T value) {
        deferredValue = value;
    }

    void OnChange(T prevValue, T currentValue) {
        if (onChange != null) { onChange(prevValue, currentValue); }
    }

    public T Value {
        set {
            if (inControl()) {
                deferPending = false;
                if (validate != null) { value = validate(value); }
                if (!EqualityComparer<T>.Default.Equals(localValue, value)) {
                    T prevValue = localValue;
                    localValue = value;
                    SetOnServer(value);
                    OnChange(prevValue, value);
                }
            }
        }
        get {
            Synchronize();
            return localValue;
        }
    }

    public void UpdateDeferred() {
        if (inControl()) {
            if (deferPending) {
                Value = deferredValue;
            }
        }
        deferPending = false;
    }

    // Should be called every tick
    public void Synchronize() {
        if (!inControl()) {
            localValue = networkVariable.Value;
            deferPending = false;
        } else if (isGlued) {
            Value = getGlued!();
        } else {
            Value = localValue; // trigger validation constraints
        }
    }

    public void Dispose()
    {
        networkVariable.Dispose();
    }
}


[HarmonyPatch]
class NetcodeInitPatch {
    static bool loaded = false;
    [HarmonyPrefix]
    [HarmonyPatch(typeof(MenuManager), "Update")]
    public static void MenuUpdate(PreInitSceneScript __instance) {
        if (!loaded && NetworkManager.Singleton != null) {
            if (Plugin.prefabContainer == null) {
                Plugin.NetcodeInit();
            }
            loaded = true;
        }
    }
}