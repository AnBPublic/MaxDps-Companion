using System.Reflection;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Wave B safety gate (4.1 "Paragon"). Both assemblies use
/// <c>namespace MaxDpsCompanion</c>, so a leftover Windows copy of a type that
/// Core also defines compiles fine and only warns CS0436 - the app's csproj
/// suppresses that warning because RotationEngine is intentionally duplicated
/// until Wave C. This test is the counterweight: it reflects over both
/// assemblies and asserts the set of full type names defined in BOTH is
/// EXACTLY <see cref="AllowedShadows"/>.
///
/// Why that matters: a stray Windows copy would silently win over Core for
/// every Windows call site (CS0436 "using the type defined in ..."), forking
/// the brain again with no behavioural test failing.
///
/// How to change the list: only when the compiler forces a duplicate for an
/// aggregate/type that cannot live in one assembly yet. Add the FULL name
/// (nested types are "Outer+Inner"), with a comment saying why it cannot move,
/// and delete the entry in the same change that removes the duplicate. Never
/// add a type just to silence a build.
/// </summary>
public class ShadowedTypeGateTests
{
    /// <summary>
    /// The COMPLETE, justified set of types that may exist in both
    /// MaxDpsCompanion.Core and MaxDpsCompanion.
    ///
    /// RotationEngine - intentional, Wave C pending: the Windows engine binds
    /// concrete WowWindow/ScreenSampler/KeySender/KeyDelivery; Core's engine
    /// binds IPlatform/IClock and is what the Mac host runs. Merging them is
    /// Wave C (NO-GO in this run) and must delete this entry.
    /// </summary>
    private static readonly string[] AllowedShadows =
    [
        "MaxDpsCompanion.RotationEngine",
    ];

    [Fact]
    public void Only_AllowListed_Types_Are_Defined_In_Both_Assemblies()
    {
        var coreTypes = FullTypeNames("MaxDpsCompanion.Core");
        var winTypes = FullTypeNames("MaxDpsCompanion");

        var shadowed = coreTypes
            .Intersect(winTypes, StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var expected = AllowedShadows
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            expected.SequenceEqual(shadowed, StringComparer.Ordinal),
            "Types defined in BOTH MaxDpsCompanion.Core and MaxDpsCompanion changed."
            + Environment.NewLine + "expected (allow-list): " + Join(expected)
            + Environment.NewLine + "actual  (shadowed):   " + Join(shadowed)
            + Environment.NewLine + "A type that is not allow-listed must be deleted from the"
            + " Windows project (Core is the single source); an allow-listed type that is gone"
            + " must be removed from AllowedShadows.");
    }

    /// <summary>
    /// Every type declared by the assembly, nested types included, as
    /// namespace-qualified names ("Ns.Outer+Inner"). Partial/XAML-generated
    /// types are ordinary metadata types, so they appear once per assembly.
    /// ReflectionTypeLoadException is tolerated for dependencies we cannot
    /// resolve here (the WinUI/XAML surface): the types that DID load are
    /// enough for a duplicate check, and a type that fails to load cannot be
    /// the same metadata symbol as one in the other assembly's loaded set.
    /// </summary>
    private static HashSet<string> FullTypeNames(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            // A null entry means "this type's dependencies failed to load".
            if (type is null) continue;
            // Compiler-generated names (<PrivateImplementationDetails>, the
            // "<>c" closure classes, anonymous types) are present in any two
            // assemblies that use array initializers or lambdas. They are not
            // addressable shadowing - no source name can bind to them - so they
            // are excluded from the gate.
            var name = type.FullName ?? type.Name;
            if (name.Contains('<')) continue;
            names.Add(name);
        }
        return names;
    }

    private static string Join(IEnumerable<string> names) =>
        names.Any() ? string.Join(", ", names) : "(none)";
}
