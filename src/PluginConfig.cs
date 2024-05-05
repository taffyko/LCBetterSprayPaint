using System;
using BepInEx.Configuration;
using UnityEngine;

namespace BetterSprayPaint;

public partial class Plugin {
    public static bool AllowErasing { get; private set; }
    public static bool AllowColorChange { get; private set; }
    public static bool InfiniteTank { get; private set; }
    public static float TankCapacity { get; private set; }
    public static float ShakeEfficiency { get; private set; }
    public static bool ShakingNotNeeded { get; private set; }
    public static float Volume { get; private set; }
    public static float MaxSize { get; private set; }
    public static float Range { get; private set; }
    public static bool ShorterShakeAnimation { get; private set; }
    public static int MaxSprayPaintDecals { get; private set; }
    public static float DrawDistance { get; private set; }

    void ConfigInit() {

        Config.Bind<string>("README", "README", "", """
All config values are text-based, as a workaround to make it possible for default values to change in future updates.

See https://github.com/taffyko/LCNiceChat/issues/3 for more information.

If you enter an invalid value, it will change back to "default" when the game starts.
""");
        ConfEntry("General", nameof(AllowErasing), true, "When enabled, players can erase spray paint. (Note: With default controls, erasing is done by holding E and LMB at the same time)", bool.TryParse, hostControlled: true);
        ConfEntry("General", nameof(AllowColorChange), true, "When enabled, players can control the color of their spray paint.", bool.TryParse, hostControlled: true);
        ConfEntry("General", nameof(InfiniteTank), true, "When enabled, the spray can has infinite uses.", bool.TryParse, hostControlled: true);
        ConfEntry("General", nameof(TankCapacity), 25.0f, "Amount of time (in seconds) that each can may spray for before running out (Has no effect when InfiniteTank is enabled.)", float.TryParse, hostControlled: true, vanillaValue: 25.0f);
        ConfEntry("General", nameof(ShakeEfficiency), .30f, "The percentage to restore on the \"shake meter\" each time the can is shaken.", float.TryParse, hostControlled: true, vanillaValue: 0.15f);
        ConfEntry("General", nameof(ShakingNotNeeded), false, "When enabled, the can never needs to be shaken.", bool.TryParse, hostControlled: true);
        ConfEntry("General", nameof(MaxSize), 2.0f, "The maximum size of spray paint that players are allowed to create.", float.TryParse, hostControlled: true);
        ConfEntry("General", nameof(Range), 6.0f, "The maximum distance that players can spray.", float.TryParse, hostControlled: true, vanillaValue: 4f);
        ConfEntry("Client-side", nameof(Volume), .1f, "Volume of spray paint sound effects.", float.TryParse, vanillaValue: 1.0f);
        ConfEntry("Client-side", nameof(ShorterShakeAnimation), true, "Whether to shorten the can-shaking animation.", bool.TryParse);
        ConfEntry("Client-side", nameof(MaxSprayPaintDecals), 4000, "The maximum amount of spray paint decals that can exist at once. When the limit is reached, spray paint decals will start to disappear, starting with the oldest.", int.TryParse, vanillaValue: 1000);
        ConfEntry("Client-side", nameof(DrawDistance), 35.0f, "The maximum distance from which spray paint decals can be seen (Only applies to new spray paint drawn after the setting was changed, if changed mid-game)", float.TryParse, vanillaValue: 20.0f);
    }

    delegate bool ParseConfigValue<T>(string input, out T output);
    private static bool NoopParse(string input, out string output) {
        output = input;
        return true;
    }
    private void ConfEntry<T>(string category, string name, T defaultValue, string description, ParseConfigValue<T> tryParse, bool hostControlled = false) {
        ConfEntryInternal(category, name, defaultValue, description, tryParse, hostControlled);
    }
    private void ConfEntry<T>(string category, string name, T defaultValue, string description, ParseConfigValue<T> tryParse, T vanillaValue, bool hostControlled = false) {
        ConfEntryInternal(category, name, defaultValue, description, tryParse, hostControlled, ConfEntryToString(vanillaValue));
    }
    private void ConfEntryInternal<T>(string category, string name, T defaultValue, string description, ParseConfigValue<T> tryParse, bool hostControlled = false, string? vanillaValueText = null) {
        var property = typeof(Plugin).GetProperty(name);
        // Build description
        string desc = $"[default: {ConfEntryToString(defaultValue)}]\n{description}";
        desc += hostControlled ? "\n(This setting is overridden by the lobby host)" : "\n(This setting's effect applies to you only)";
        if (vanillaValueText != null) {
            desc += $"\n(The original value of this setting in the base-game is {vanillaValueText})";
        }
        var config = Config.Bind<string>(category, name, "default", desc);
        if (string.IsNullOrEmpty(config.Value)) { config.Value = "default"; }
        // Load value
        bool validCustomValue = tryParse(config.Value, out T value) && config.Value != "default";
        property.SetValue(null, validCustomValue ? value : defaultValue);
        if (!validCustomValue) { config.Value = "default"; }
        // Handle changes in value during the game
        EventHandler loadConfig = (object? sender, EventArgs? e) => {
            bool validCustomValue = tryParse(config.Value, out T value) && config.Value != "default";
            property.SetValue(null, validCustomValue ? value : defaultValue);
        };
        config.SettingChanged += loadConfig;

        cleanupActions.Add(() => {
            config.SettingChanged -= loadConfig;
            property.SetValue(null, defaultValue);
        });
    }
    private string ConfEntryToString(object? value) {
        if (value == null) { return "null"; }
        var type = value.GetType();
        if (type == typeof(float)) {
            return string.Format("{0:0.0#####}", (float)value);
        } else if (type == typeof(UnityEngine.Color)) {
            return $"#{ColorUtility.ToHtmlStringRGBA((UnityEngine.Color)value)}";
        } else {
            return value.ToString();
        }
    }
}