using System.Linq;
using System.Reflection;
using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace BetterSprayPaint;
// Per-player synchronized state
public class PlayerExt : NetworkBehaviour {
    public PlayerControllerB instance => GetComponent<PlayerControllerB>();


    internal NetworkVariable<float> paintSize; internal NetVar<float> PaintSize;
    [ServerRpc(RequireOwnership = false)] void SetPaintSizeServerRpc(float value) => PaintSize.ServerSet(value);

    INetVar[] netVars = [];

    PlayerExt() {
        PaintSize = new(out paintSize, SetPaintSizeServerRpc, () => instance.IsLocalPlayer(),
            validate: value => Mathf.Clamp(value, 0.1f, SessionData.MaxSize),
            initialValue: 1.0f);
        netVars = typeof(PlayerExt).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(field => typeof(INetVar).IsAssignableFrom(field.FieldType))
            .Select(field => (INetVar)field.GetValue(this)).ToArray();
    }

    public override void OnNetworkSpawn() {
        base.OnNetworkSpawn();
    }
    void Update() {
        foreach (var netVar in netVars) { netVar.Synchronize(); }
    }
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        foreach (var netVar in netVars) { netVar.Dispose(); }
    }
}