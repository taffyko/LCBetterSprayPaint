using System;
using System.Collections.Generic;
using System.Reflection;
using LethalCompanyInputUtils;
using LethalCompanyInputUtils.Api;
using UnityEngine.InputSystem;

namespace BetterSprayPaint;

public class PluginInputActions : LcInputActions {

    [InputAction("<Keyboard>/e", Name = "Spray Paint Erase Modifier", ActionId = "SprayPaintEraseModifier", GamepadPath = "<Gamepad>/dpad/up")]
    public InputAction? SprayPaintEraseModifier { get; set; }

    [InputAction("", Name = "Spray Paint Erase", ActionId = "SprayPaintErase")]
    public InputAction? SprayPaintErase { get; set; }

    [InputAction("<Keyboard>/t", Name = "Spray Paint Next Color", ActionId = "SprayPaintNextColor")]
    public InputAction? SprayPaintNextColor { get; set; }
    [InputAction("", Name = "Spray Paint Previous Color", ActionId = "SprayPaintPreviousColor")]
    public InputAction? SprayPaintPreviousColor { get; set; }
    [InputAction("", Name = "Spray Paint Color 1", ActionId = "SprayPaintColor1")]
    public InputAction? SprayPaintColor1 { get; set; }
    [InputAction("", Name = "Spray Paint Color 2", ActionId = "SprayPaintColor2")]
    public InputAction? SprayPaintColor2 { get; set; }
    [InputAction("", Name = "Spray Paint Color 3", ActionId = "SprayPaintColor3")]
    public InputAction? SprayPaintColor3 { get; set; }
    [InputAction("", Name = "Spray Paint Color 4", ActionId = "SprayPaintColor4")]
    public InputAction? SprayPaintColor4 { get; set; }

    [InputAction("<Keyboard>/equals", Name = "Spray Paint Increase Size", ActionId = "SprayPaintIncreaseSize")]
    public InputAction? SprayPaintIncreaseSize { get; set; }
    [InputAction("<Keyboard>/minus", Name = "Spray Paint Decrease Size", ActionId = "SprayPaintDecreaseSize")]
    public InputAction? SprayPaintDecreaseSize { get; set; }
    [InputAction("", Name = "Spray Paint Set Size 0.1", ActionId = "SprayPaintSize01")]
    public InputAction? SprayPaintSize01 { get; set; }
    [InputAction("", Name = "Spray Paint Set Size 1.0", ActionId = "SprayPaintSize1")]
    public InputAction? SprayPaintSize1 { get; set; }
    [InputAction("", Name = "Spray Paint Set Size 2.0", ActionId = "SprayPaintSize2")]
    public InputAction? SprayPaintSize2 { get; set; }

    public PluginInputActions() : base() {
        #if DEBUG
        BetterSprayPaint.Plugin.cleanupActions.Add(() => {
            var id = (string)typeof(PluginInputActions).GetProperty("Id", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(this);
            var inputActionsMap = (Dictionary<string, LcInputActions>)typeof(LcInputActionApi).GetField("InputActionsMap", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            inputActionsMap.Remove(id);
            UnityEngine.Object.Destroy(Asset);
        });
        #endif
    }
}

public partial class Plugin {
    internal static PluginInputActions inputActions = new PluginInputActions();
}


public class ActionSubscriptionBuilder(List<Action> cleanupActions, Func<bool> active) {
    public List<Action> cleanupActions = cleanupActions;
    public Func<bool> active = active;

    public void Subscribe(InputAction? action, EventHandler<InputAction.CallbackContext> onStart, EventHandler<InputAction.CallbackContext>? onStop = null) {
        Subscribe<object?>(
            action,
            (object sender, InputAction.CallbackContext e) => { onStart(sender, e); return null; },
            onStop == null ? null : (object sender, InputAction.CallbackContext e, object? ret) => onStop(sender, e)
        );
    }

    // full implementation which allows onStart to return a value that onStop can access
    public void Subscribe<T>(InputAction? action, Func<object, InputAction.CallbackContext, T> onStart, Action<object, InputAction.CallbackContext, T?>? onStop = null) {
        if (action == null) {
            Plugin.log.LogWarning($"{nameof(ActionSubscriptionBuilder.Subscribe)} called with null InputAction");
            return;
        }
        var startedEvent = typeof(InputAction).GetEvent("started");
        var canceledEvent = typeof(InputAction).GetEvent("canceled");
        T ret = default!;
        bool actionHeld = false;
        var startHandler = (InputAction.CallbackContext e) => {
            if (active()) {
                actionHeld = true;
                ret = onStart(this, e);
            }
        };
        var cancelHandler = (InputAction.CallbackContext e) => {
            if (actionHeld && onStop != null) {
                onStop(this, e, ret);
                actionHeld = false;
            }
        };
        startedEvent.AddEventHandler(action, startHandler);
        cleanupActions.Add(() => { startedEvent.RemoveEventHandler(action, startHandler); });
        canceledEvent.AddEventHandler(action, cancelHandler);
        cleanupActions.Add(() => { canceledEvent.RemoveEventHandler(action, cancelHandler); });
    }
}