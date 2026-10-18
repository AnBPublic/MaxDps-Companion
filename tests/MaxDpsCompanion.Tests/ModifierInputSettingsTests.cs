using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// 4.1 A2.2 (g) + A2.10: the new [Window] ModifierInput key defaults to Post
/// (today's behaviour), parses Auto/SendInput, round-trips through Save, and
/// the colour profile is replaced by reference rather than mutated in place.
/// </summary>
public class ModifierInputSettingsTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"mdb-modifier-{Guid.NewGuid():N}.ini");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void ModifierInput_Defaults_To_Post()
    {
        var settings = AppSettings.Load(_path);
        Assert.Equal(ModifierInputMode.Post, settings.ModifierInput);
    }

    [Theory]
    [InlineData("Post", 0)]
    [InlineData("post", 0)]
    [InlineData("Auto", 1)]
    [InlineData("auto", 1)]
    [InlineData("SendInput", 2)]
    [InlineData("sendinput", 2)]
    [InlineData("garbage", 0)]
    [InlineData("", 0)]
    public void Load_Parses_Window_ModifierInput(string value, int expected)
    {
        File.WriteAllText(_path, $"[Window]\nModifierInput={value}\n");

        var settings = AppSettings.Load(_path);

        Assert.Equal(expected, (int)settings.ModifierInput);
    }

    [Fact]
    public void Save_Round_Trips_ModifierInput_And_Documents_It()
    {
        var settings = AppSettings.Load(_path);
        settings.ModifierInput = ModifierInputMode.Auto;
        settings.Save();

        var text = File.ReadAllText(_path);
        Assert.Contains("ModifierInput=Auto", text);
        Assert.Contains("; ModifierInput:", text);

        var reloaded = AppSettings.Load(_path);
        Assert.Equal(ModifierInputMode.Auto, reloaded.ModifierInput);

        reloaded.ModifierInput = ModifierInputMode.SendInput;
        reloaded.Save();
        Assert.Equal(ModifierInputMode.SendInput, AppSettings.Load(_path).ModifierInput);
    }

    [Fact]
    public void Color_Profile_Is_Replaced_Not_Mutated()
    {
        var settings = AppSettings.Load(_path);
        var original = settings.Color;
        original.Tolerance = 64;

        var copy = original.WithTolerance(99);
        settings.Color = copy;

        Assert.NotSame(original, copy);
        Assert.Equal(64, original.Tolerance);   // the old snapshot never changes
        Assert.Equal(99, copy.Tolerance);
    }

    [Fact]
    public void A_Replaced_Profile_Round_Trips_Through_Save()
    {
        var settings = AppSettings.Load(_path);
        settings.Color = settings.Color.WithTolerance(99);
        settings.Save();

        var reloaded = AppSettings.Load(_path);
        Assert.Equal(99, reloaded.Color.Tolerance);
    }

    [Fact]
    public void WithTolerance_Clamps_To_The_Supported_Box()
    {
        var profile = new ColorProfile();
        Assert.Equal(16, profile.WithTolerance(1).Tolerance);
        Assert.Equal(128, profile.WithTolerance(999).Tolerance);
    }
}
