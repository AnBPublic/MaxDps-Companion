using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// STA construction test for the Class skills screen: the real window builds
/// the view, the dropdown preselects the requested class/spec and the
/// generated list actually contains rows (icons are optional). Runs the UI on
/// a dedicated STA thread — the same contract as --ui-smoke-test.
/// </summary>
public class ClassSkillsViewTests
{
    private static (string? State, Exception? Error) RunOnSta(Func<string?> action)
    {
        string? state = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                state = action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA UI thread timed out");
        return (state, error);
    }

    [Fact]
    public void Window_Builds_The_Class_Skills_Screen_With_Rows()
    {
        var (state, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            form.CreateControl();
            form.OpenClassSkillsForSnapshot("ROGUE", "Outlaw");
            return form.ClassSkillsDebugState;
        });

        Assert.Null(error);
        Assert.NotNull(state);
        Assert.Contains("wantVisible=True", state);
        Assert.Contains("class=ROGUE", state);
        Assert.Contains("spec=Outlaw", state);
        Assert.DoesNotContain("rows=0", state);
    }

    [Fact]
    public void Window_Opens_Class_Skills_For_Every_Class()
    {
        var (states, error) = RunOnSta(() =>
        {
            var settings = new AppSettings();
            using var form = new MainForm(settings);
            form.CreateControl();
            var results = new List<string>();
            foreach (var className in AbilityCatalog.ClassOrder)
            {
                if (className.Length == 0) continue;
                var spec = AbilityCatalog.SpecOrder[className][1];
                form.OpenClassSkillsForSnapshot(className, spec);
                results.Add($"{className}/{spec}: {form.ClassSkillsDebugState}");
            }
            return string.Join("\n", results);
        });

        Assert.Null(error);
        Assert.NotNull(states);
        foreach (var line in states!.Split('\n'))
        {
            Assert.DoesNotContain("rows=0", line);
        }
    }
}
