using System.Linq;
using System.Reflection;
using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace BetterSprayPaint.Ngo;
// Per-player synchronized state
public class PlayerNetExt : NetworkBehaviour {
    public PlayerControllerB instance => GetComponent<PlayerControllerB>();


    internal NetworkVariable<float> paintSize; internal NetVar<float> PaintSize;
    [ServerRpc(RequireOwnership = false)] void SetPaintSizeServerRpc(float value) => PaintSize.ServerSet(value);

    INetVar[] netVars = [];

    PlayerNetExt() {
        PaintSize = new(out paintSize, SetPaintSizeServerRpc, () => instance.IsLocalPlayer(),
            validate: value => Mathf.Clamp(value, 0.1f, SessionData.MaxSize),
            initialValue: 1.0f);
        netVars = INetVar.GetAllNetVars(this);
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