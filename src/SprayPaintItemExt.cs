using System;
using System.Collections.Generic;
using BetterSprayPaint;
using BetterSprayPaint.Ngo;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

public class SprayPaintItemExt: MonoBehaviour {
    public SprayPaintItem instance = null!;
    public SprayPaintItemNetExt net = null!;
    
    public DecalProjector previewDecal = null!;

    List<Action> cleanupActions = new List<Action>();
    
    public bool ItemActive() => net.HeldByLocalPlayer && net.InActiveSlot && Patches.CanUseItem(instance.playerHeldBy);
    
    public void Awake() {
        instance = GetComponent<SprayPaintItem>();
        net = instance.NetExt();

        var go = UnityEngine.Object.Instantiate(instance.sprayPaintPrefab);
        previewDecal = go.GetComponent<DecalProjector>();
        GameObject.DontDestroyOnLoad(go);
        previewDecal.material = new Material(net.baseDecalMaterial);
        previewDecal.enabled = true;
        go.name = "PreviewDecal";
        go.SetActive(true);
        
        var actions = new ActionSubscriptionBuilder(cleanupActions, () => ItemActive());
        actions.Subscribe(
            Plugin.inputActions.SprayPaintEraseModifier,
            onStart: delegate {
                if (SessionData.AllowErasing) { net.IsErasing = true; }
            }
        );

        actions.Subscribe(
            Plugin.inputActions.SprayPaintErase,
            onStart: delegate {
                if (SessionData.AllowErasing) { net.IsErasing = true; }
                instance.UseItemOnClient(true);
            },
            onStop: delegate {
                instance.UseItemOnClient(false);
            }
        );

        actions.Subscribe(
            Plugin.inputActions.SprayPaintNextColor,
            delegate {
                if (!SessionData.AllowColorChange) return;
                var idx = net.ColorPalette.FindIndex((Color color) => color == net.CurrentColor);
                idx = net.posmod(++idx, net.ColorPalette.Count);
                StartCoroutine(net.ChangeColorCoroutine(net.ColorPalette[idx]));
            }
        );
        actions.Subscribe(
            Plugin.inputActions.SprayPaintPreviousColor,
            delegate {
                if (!SessionData.AllowColorChange) return;
                var idx = net.ColorPalette.FindIndex((Color color) => color == net.CurrentColor);
                idx = net.posmod(--idx, net.ColorPalette.Count);
                StartCoroutine(net.ChangeColorCoroutine(net.ColorPalette[idx]));
            }
        );

        actions.Subscribe(
            Plugin.inputActions.SprayPaintColor1,
            delegate {
                if (!SessionData.AllowColorChange) return;
                var idx = net.posmod(0, net.ColorPalette.Count);
                StartCoroutine(net.ChangeColorCoroutine(net.ColorPalette[idx]));
            }
        );
        actions.Subscribe(
            Plugin.inputActions.SprayPaintColor2,
            delegate {
                if (!SessionData.AllowColorChange) return;
                var idx = net.posmod(1, net.ColorPalette.Count);
                StartCoroutine(net.ChangeColorCoroutine(net.ColorPalette[idx]));
            }
        );
        actions.Subscribe(
            Plugin.inputActions.SprayPaintColor3,
            delegate {
                if (!SessionData.AllowColorChange) return;
                var idx = net.posmod(2, net.ColorPalette.Count);
                StartCoroutine(net.ChangeColorCoroutine(net.ColorPalette[idx]));
            }
        );
        actions.Subscribe(
            Plugin.inputActions.SprayPaintColor4,
            delegate {
                if (!SessionData.AllowColorChange) return;
                var idx = net.posmod(3, net.ColorPalette.Count);
                StartCoroutine(net.ChangeColorCoroutine(net.ColorPalette[idx]));
            }
        );

        actions.Subscribe(
            Plugin.inputActions.SprayPaintIncreaseSize,
            (_, _) => StartCoroutine(net.ChangeSizeCoroutine()),
            (_, _, coroutine) => StopCoroutine(coroutine)
        );
        actions.Subscribe(
            Plugin.inputActions.SprayPaintDecreaseSize,
            (_, _) => StartCoroutine(net.ChangeSizeCoroutine()),
            (_, _, coroutine) => StopCoroutine(coroutine)
        );

        actions.Subscribe(
            Plugin.inputActions.SprayPaintSize01,
            delegate {
                net.PaintSize = 0.1f;
            }
        );
        actions.Subscribe(
            Plugin.inputActions.SprayPaintSize1,
            delegate {
                net.PaintSize = 1.0f;
            }
        );
        actions.Subscribe(
            Plugin.inputActions.SprayPaintSize2,
            delegate {
                net.PaintSize = 2.0f;
            }
        );
    }
    
    
    float previewFadeFactor = 1f;
    Vector3 previewOriginalScale = Vector3.oneVector;
    
    public void Update() {
        if (previewDecal == null || previewDecal.gameObject == null || previewDecal.material == null) {
            return;
        }

        var active = false;
        if (ItemActive()) {
            var sprayPos = instance.playerHeldBy.gameplayCamera.transform.position;
            var sprayRot = instance.playerHeldBy.gameplayCamera.transform.forward;
            Ray ray = new Ray(sprayPos, sprayRot);
            if (Patches.RaycastCustom(ray, out var sprayHit, SessionData.Range, net.sprayPaintMask, QueryTriggerInteraction.Collide, instance)) {
                Patches.PositionSprayPaint(instance, previewDecal.gameObject, sprayHit, setColor: false);
                previewOriginalScale = previewDecal.transform.localScale;
                active = true;
            }
        }

        var c = net.CurrentColor;
        var factor = (Mathf.Sin(Time.timeSinceLevelLoad * 6f) + 1f) * 0.2f + 0.2f;
        previewFadeFactor = Utils.Lexp(previewFadeFactor, active ? 1f : 0f, 15f * Time.deltaTime);
        previewDecal.material.color = new Color(
            Mathf.Lerp(c.r, Math.Min(c.r + 0.35f, 1f), factor),
            Mathf.Lerp(c.g, Math.Min(c.g + 0.35f, 1f), factor),
            Mathf.Lerp(c.b, Math.Min(c.b + 0.35f, 1f), factor),
            Mathf.Clamp(Plugin.SprayPreviewOpacity, 0f, 1f) * previewFadeFactor
        );
        previewDecal.transform.localScale = previewOriginalScale * previewFadeFactor;
    }
    
    public void OnDestroy() {
        foreach (var a in cleanupActions) { a(); }
        Destroy(previewDecal);
    }
}