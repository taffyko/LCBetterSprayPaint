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
    
    public void Awake() {
        instance = GetComponent<SprayPaintItem>();
        net = instance.NetExt();

        var go = GameObject.Instantiate(instance.sprayPaintPrefab);
        go.name = "PreviewDecal";
        go.SetActive(true);
        previewDecal = go.GetComponent<DecalProjector>();
        previewDecal.enabled = true;
        
        var actions = new ActionSubscriptionBuilder(cleanupActions, () => net.HeldByLocalPlayer && net.InActiveSlot && Patches.CanUseItem(instance.playerHeldBy));
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
    
    public void Update() {
        if (instance.playerHeldBy != null && instance.playerHeldBy.IsLocalPlayer()) {
            var sprayPos = instance.playerHeldBy.gameplayCamera.transform.position;
            var sprayRot = instance.playerHeldBy.gameplayCamera.transform.forward;
            Ray ray = new Ray(sprayPos, sprayRot);
            if (Patches.RaycastSkipPlayer(ray, out var sprayHit, SessionData.Range, net.sprayPaintMask, QueryTriggerInteraction.Collide, instance)) {
                Patches.PositionSprayPaint(instance, previewDecal.gameObject, sprayHit);
            }
        } else {
            previewDecal.enabled = false;
        }
    }
    
    public void Destroy() {
        foreach (var a in cleanupActions) { a(); }
        Destroy(previewDecal);
    }
}