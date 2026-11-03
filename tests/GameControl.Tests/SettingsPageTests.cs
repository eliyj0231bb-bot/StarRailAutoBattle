using GameControl;
using GameLauncher.Core;

namespace GameControl.Tests;

public class SettingsPageTests
{
    // R75: two operations on one key are reported (by the canonical key name), but allowed.
    [Fact]
    public void R75_shared_keys_are_listed_by_key()
    {
        Assert.Empty(new KeyMap().SharedKeys());

        var map = new KeyMap().With("ult1", "q").With("left", "空格");
        Assert.Equal(["Q: basic, ult1", "Space: confirm, left"],
            map.SharedKeys().Select(s => $"{s.Key}: {string.Join(", ", s.Names)}"));
        map.Validate();
    }

    // R75: the settings page edits control-keys.json; what it saves is what srctl loads.
    [Fact]
    public void R75_example_splash_and_slot_anim_round_trip_through_the_srctl_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "srab-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new KeyMapStorage(dir);
            storage.Save(new KeyMap { SplashMs = 1000 }.WithSlotAnim(2, 8000));
            var loaded = storage.Load();
            Assert.Equal(1000, loaded.SplashMs);
            Assert.Equal(8000, loaded.AnimFor(2));
            Assert.Equal(3000, loaded.AnimFor(1));
            Assert.Contains("\"ult2AnimMs\": 8000", File.ReadAllText(storage.FilePath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // R75: the retry pause is kept in the launcher's settings.json, and other saves keep it.
    [Fact]
    public void R75_pause_after_failures_defaults_and_round_trips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "srab-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new AppStorage(dir);
            Assert.Equal(10, storage.LoadSettings().PauseAfterFailures);
            storage.SaveSettings(new UserSettings { PauseAfterFailures = 0 });
            storage.SaveSettings(storage.LoadSettings() with { EntryPath = @"D:\a.lnk" });
            Assert.Equal(0, storage.LoadSettings().PauseAfterFailures);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // R76: restart at the first pending step and pause after 15 pending steps by default; an older settings.json
    // without these fields gets the defaults.
    [Fact]
    public void R76_pending_settings_default_and_round_trip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "srab-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new AppStorage(dir);
            Directory.CreateDirectory(dir);
            File.WriteAllText(storage.SettingsPath, """{ "EntryPath": "", "PauseAfterFailures": 5 }""");
            var old = storage.LoadSettings();
            Assert.Equal((5, 0, 15), (old.PauseAfterFailures, old.AllowedPendingPerBattle, old.PauseAfterPending));

            storage.SaveSettings(old with { AllowedPendingPerBattle = 2, PauseAfterPending = 0 });
            var saved = storage.LoadSettings();
            Assert.Equal((5, 2, 0), (saved.PauseAfterFailures, saved.AllowedPendingPerBattle, saved.PauseAfterPending));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
