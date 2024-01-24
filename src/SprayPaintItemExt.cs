using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Unity.Netcode;
using System.Collections;
using GameNetcodeStuff;

namespace BetterSprayPaint;

public class SprayPaintItemExt: NetworkBehaviour {
    SprayPaintItem instance => GetComponent<SprayPaintItem>();
    public bool HeldByLocalPlayer => instance.playerHeldBy.IsLocalPlayer();
    public bool InActiveSlot => instance.playerHeldBy != null && instance.playerHeldBy.ItemSlots[instance.playerHeldBy.currentItemSlot] == instance;
    static SessionData sessionData => SessionData.instance!;

    NetworkVariable<bool> _isErasing = new NetworkVariable<bool>(false);
    bool _localIsErasing = false;
    public bool IsErasing {
        set {
            if (_localIsErasing != value) { 
                _localIsErasing = value;
                ToggleErasingServerRpc(value);
            }
        }
        get { return HeldByLocalPlayer ? _localIsErasing : _isErasing.Value; }
    }
    [ServerRpc(RequireOwnership = false)]
    public void ToggleErasingServerRpc(bool active) {
        _isErasing.Value = active;
    }
    public void OnChangeErasing(bool previous, bool current) {
        if (!HeldByLocalPlayer) { UpdateParticles(); }
    }


    public float PaintSize {
        set { instance.playerHeldBy.Ext()!.PaintSize = value; }
        get { return instance.playerHeldBy?.Ext()?.PaintSize ?? 1.0f; }
    }

    public NetworkVariable<float> ShakeMeter = new NetworkVariable<float>(1.0f);
    public NetworkVariable<Color> _currentColor = new NetworkVariable<Color>();
    Color _localCurrentColor = default;
    public Color CurrentColor {
        set { if (_localCurrentColor != value) { _localCurrentColor = value; SetCurrentColorServerRpc(value); } }
        get { return HeldByLocalPlayer ? _localCurrentColor : _currentColor.Value; }
    }
    [ServerRpc(RequireOwnership = false)]
    public void SetCurrentColorServerRpc(Color color) {
        if (sessionData.allowColorChange.Value) {
            _currentColor.Value = color;
        }
    }

    public NetworkVariable<NetworkObjectReference> PlayerHeldBy = new NetworkVariable<NetworkObjectReference>();
    [ServerRpc(RequireOwnership = false)]
    public void SetPlayerHeldByServerRpc(NetworkObjectReference playerRef) {
        if (playerRef.TryGet(out var player) && player.TryGetComponent<PlayerControllerB>(out _)) {
            PlayerHeldBy.Value = playerRef;
        }
    }

    public int sprayPaintMask;
    public Material? baseParticleMaterial;
    public Material? sprayEraseParticleMaterial;
    public Material? baseDecalMaterial;
    public ParticleSystem? sprayCanColorChangeParticle;
    List<Action> cleanupActions = new List<Action>();
    public static Dictionary<Color, WeakReference<Material>> AllDecalMaterials = new();
    public static Dictionary<Color, WeakReference<Material>> AllParticleMaterials = new();
    public List<Color> ColorPalette = new List<Color>();

    public int posmod(int n, int d) {
        return ((n % d) + d) % d;
    }

    public Material DecalMaterialForColor(Color color) {
        Material mat;
        if (AllDecalMaterials.TryGetValue(color, out var matRef)) {
            if (matRef.TryGetTarget(out mat)) {
                if (mat != null && mat.color == color) {
                    return mat;
                }
            }
        }
        mat = new Material(baseDecalMaterial) { color = color };
        AllDecalMaterials[color] = new WeakReference<Material>(mat);
        return mat;
    }

    public Material ParticleMaterialForColor(Color color) {
        Material mat;
        if (AllParticleMaterials.TryGetValue(color, out var matRef)) {
            if (matRef.TryGetTarget(out mat)) {
                return mat;
            }
        }
        mat = new Material(baseParticleMaterial) { color = color };
        AllParticleMaterials[color] = new WeakReference<Material>(mat);
        return mat;
    }

    public override void OnNetworkSpawn() {
        base.OnNetworkSpawn();
        sprayPaintMask = Traverse.Create(instance).Field("sprayPaintMask").GetValue<int>();
        var sprayCanMatsIndex = Traverse.Create(instance).Field<int>("sprayCanMatsIndex").Value;
        baseParticleMaterial = instance.particleMats[sprayCanMatsIndex];
        baseDecalMaterial = instance.sprayCanMats[sprayCanMatsIndex];
        if (IsServer) {
            var rand = new System.Random();
            var mat = instance.sprayCanMats[rand.Next(instance.sprayCanMats.Length)];
            CurrentColor = mat.color;
        }
        sprayEraseParticleMaterial = new Material(baseParticleMaterial);
        sprayEraseParticleMaterial.color = new Color(0.5f, 0.5f, 0.5f, 1f);
        ColorPalette = instance.sprayCanMats.Select((Material mat) => mat.color).ToList();
        foreach (var mat in instance.sprayCanMats) {
            AllDecalMaterials[mat.color] = new WeakReference<Material>(mat);
        }
        foreach (var mat in instance.particleMats) {
            AllParticleMaterials[mat.color] = new WeakReference<Material>(mat);
        }

        var go = Instantiate(instance.sprayCanNeedsShakingParticle.gameObject);
        go.name = "ColorChangeParticle";
        sprayCanColorChangeParticle = go.GetComponent<ParticleSystem>();
        sprayCanColorChangeParticle.transform.SetParent(instance.transform, false);
        var main = sprayCanColorChangeParticle.main;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startSpeedMultiplier = 2f;
        main.startLifetimeMultiplier = 0.1f;
        var emission = sprayCanColorChangeParticle.emission;
        emission.rateOverTimeMultiplier = 100f;
        var shape = sprayCanColorChangeParticle.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;

        var actions = new ActionSubscriptionBuilder(cleanupActions, () => HeldByLocalPlayer && Patches.CanUseItem(instance.playerHeldBy));
        actions.Subscribe(
            Plugin.inputActions.SprayPaintEraseModifier,
            onStart: delegate {
                if (Patches.AllowErasing) { IsErasing = true; }
            }
        );

        actions.Subscribe(
            Plugin.inputActions.SprayPaintErase,
            onStart: delegate {
                if (Patches.AllowErasing) { IsErasing = true; }
                instance.UseItemOnClient(true);
            },
            onStop: delegate {
                instance.UseItemOnClient(false);
            }
        );

        actions.Subscribe(
            Plugin.inputActions.SprayPaintNextColor,
            delegate {
                var idx = ColorPalette.FindIndex((Color color) => color == CurrentColor);
                idx = posmod(++idx, ColorPalette.Count);
                StartCoroutine(ChangeColorCoroutine(ColorPalette[idx]));
            }
        );
        actions.Subscribe(
            Plugin.inputActions.SprayPaintPreviousColor,
            delegate {
                var idx = ColorPalette.FindIndex((Color color) => color == CurrentColor);
                idx = posmod(--idx, ColorPalette.Count);
                StartCoroutine(ChangeColorCoroutine(ColorPalette[idx]));
            }
        );

        actions.Subscribe(
            Plugin.inputActions.SprayPaintIncreaseSize,
            (_, _) => StartCoroutine(ChangeSizeCoroutine()),
            (_, _, coroutine) => StopCoroutine(coroutine)
        );
        actions.Subscribe(
            Plugin.inputActions.SprayPaintDecreaseSize,
            (_, _) => StartCoroutine(ChangeSizeCoroutine()),
            (_, _, coroutine) => StopCoroutine(coroutine)
        );

        _isErasing.OnValueChanged += OnChangeErasing;
    }

    IEnumerator ChangeColorCoroutine(Color color) {
        if (sessionData.allowColorChange.Value) {
            CurrentColor = color;
            UpdateParticles();
            if (Traverse.Create(instance).Field("sprayCanTank").GetValue<float>() > 0f) {
                sprayCanColorChangeParticle!.Play();
                yield return new WaitForSeconds(0.1f);
                sprayCanColorChangeParticle!.Stop();
            }
        }
    }

    IEnumerator ChangeSizeCoroutine() {
        bool increase = Plugin.inputActions.SprayPaintIncreaseSize!.IsPressed();
        bool decrease = Plugin.inputActions.SprayPaintDecreaseSize!.IsPressed();
        PaintSize = PaintSize + (increase ? .1f : -.1f);
        yield return new WaitForSeconds(0.1f);
        while (HeldByLocalPlayer && (increase || decrease)) {
            increase = Plugin.inputActions.SprayPaintIncreaseSize!.IsPressed();
            decrease = Plugin.inputActions.SprayPaintDecreaseSize!.IsPressed();
            PaintSize = PaintSize + (increase ? .1f : -.1f);
            yield return new WaitForSeconds(0.025f);
        }
    }

    public void UpdateParticles() {
        var particleShape = instance.sprayParticle.shape;
        var particleMain = instance.sprayParticle.main;
        var particleSize = Mathf.Min(1.5f, PaintSize);
        particleMain.startSizeMultiplier = 0.5f * particleSize; // size of particles
        particleShape.scale = new Vector3(particleSize, particleSize, particleSize); // size of particle emission area
        var colorMat = ParticleMaterialForColor(CurrentColor);
        sprayCanColorChangeParticle!.GetComponent<ParticleSystemRenderer>().material = colorMat;
        if (Patches.AllowErasing && IsErasing) {
            var mat = sprayEraseParticleMaterial;
            instance.sprayCanNeedsShakingParticle.GetComponent<ParticleSystemRenderer>().material = mat;
            instance.sprayParticle.GetComponent<ParticleSystemRenderer>().material = mat;
            particleShape.angle = 5f;
            particleMain.startSpeed = 50f;
            particleMain.startLifetime = 0.1f;
        } else {
            instance.sprayCanNeedsShakingParticle.GetComponent<ParticleSystemRenderer>().material = colorMat;
            instance.sprayParticle.GetComponent<ParticleSystemRenderer>().material = colorMat;
            particleMain.startSpeed = 100f;
            particleMain.startLifetime = 0.05f;
            particleShape.angle = 0f;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _isErasing.OnValueChanged -= OnChangeErasing;
        foreach (var action in cleanupActions) {
            action();
        }
    }

    public void Update() {
        if (HeldByLocalPlayer && InActiveSlot) {
            UpdateTooltips();
        }
        if (!HeldByLocalPlayer && _localCurrentColor != _currentColor.Value) {
            _localCurrentColor = _currentColor.Value;
        }
        if (!HeldByLocalPlayer && instance.playerHeldBy != null) {
            UpdateParticles();
        }
    }

    public void UpdateTooltips() {
        // Right now just using this to display PaintSize,
        // down-the-line might work on a more general solution for adding more than 4 tooltip lines
        if (HUDManager.Instance.controlTipLines.Length >= 4) {
            HUDManager.Instance.controlTipLines[3].text = $"Size : {PaintSize:0.0}";
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void UpdateShakeMeterServerRpc(float shakeMeter) {
        ShakeMeter.Value = shakeMeter;
    }

    [ServerRpc(RequireOwnership = false)]
    public void EraseServerRpc(Vector3 sprayPos, ServerRpcParams rpc = default) {
        if (sessionData.allowErasing.Value) {
            EraseClientRpc(sprayPos);
        }
    }
    [ClientRpc]
    public void EraseClientRpc(Vector3 sprayPos) {
        if (sessionData.allowErasing.Value) {
            if (!HeldByLocalPlayer) {
                Patches.EraseSprayPaintAtPoint(instance, sprayPos);
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void SprayServerRpc(Vector3 sprayPos, Vector3 sprayRot, ServerRpcParams rpc = default) {
        SprayClientRpc(sprayPos, sprayRot);
    }
    [ClientRpc]
    public void SprayClientRpc(Vector3 sprayPos, Vector3 sprayRot) {
        if (!HeldByLocalPlayer) {
            Patches.AddSprayPaintLocal(instance, sprayPos, sprayRot);
        }
    }
}