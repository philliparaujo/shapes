using System.Reflection;
using System.Xml.Linq;
using Shapes.Core.Primitives;

namespace Shapes.Tests.Architecture;

// Enforces the keystone architectural rule from PLAN.md: Shapes.Core references nothing
// but the BCL.
//
// Everything downstream depends on this. If Core stays pure, Phase 4 is a client swap
// rather than a rewrite, and the console, AI, sim, and Godot clients are interchangeable
// consumers. The rule is easy to break accidentally with a single convenient using
// directive, so it is a test rather than a convention.
//
// The package and project checks read the .csproj rather than inspecting the compiled
// assembly. This is deliberate: GetReferencedAssemblies only reports references the
// compiler actually emitted, so a dependency that is declared but not yet used by any Core
// code is invisible at runtime. Checking the project file catches it the moment it is
// added, which is when it is cheap to remove.
public class CorePurityTests
{
    private static readonly Assembly CoreAssembly = typeof(ResourceType).Assembly;

    // Walks up from the test assembly until the solution file appears, so the tests do not
    // depend on the build output layout.
    private static string RepositoryRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Shapes.sln")))
            {
                dir = dir.Parent;
            }

            Assert.True(dir is not null, "Could not locate Shapes.sln above the test output directory.");
            return dir!.FullName;
        }
    }

    private static XDocument CoreProject =>
        XDocument.Load(Path.Combine(RepositoryRoot, "Shapes.Core", "Shapes.Core.csproj"));

    [Fact]
    public void Core_declares_no_package_references()
    {
        // Core must depend on the BCL alone. A NuGet package here is an architectural
        // decision, not a convenience -- and several would break the iOS AOT requirement.
        var packages = CoreProject
            .Descendants("PackageReference")
            .Select(e => e.Attribute("Include")?.Value ?? "(unnamed)")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            packages.Count == 0,
            $"Shapes.Core must declare no PackageReference, but declares: {string.Join(", ", packages)}. "
            + "See PLAN.md 'Core stays pure'.");
    }

    [Fact]
    public void Core_declares_no_project_references()
    {
        // Core sits at the bottom of the dependency graph; everything points inward at it.
        var projects = CoreProject
            .Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value ?? "(unnamed)")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            projects.Count == 0,
            $"Shapes.Core must declare no ProjectReference, but declares: {string.Join(", ", projects)}.");
    }

    [Fact]
    public void Core_binary_references_only_the_bcl()
    {
        // Complements the csproj checks: catches anything that reaches the compiled output
        // by a route other than an explicit reference (transitive deps, MSBuild injection).
        string[] allowedPrefixes = ["System", "mscorlib", "netstandard"];

        var violations = CoreAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => !allowedPrefixes.Any(
                prefix => name.Equals(prefix, StringComparison.Ordinal)
                          || name.StartsWith(prefix + ".", StringComparison.Ordinal)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"Shapes.Core must reference only the BCL, but its compiled output references: "
            + $"{string.Join(", ", violations)}.");
    }

    [Fact]
    public void Core_does_not_reference_ui_or_engine_assemblies()
    {
        // Named explicitly so the failure message points at the actual mistake. These are
        // the references most likely to be introduced by accident.
        string[] forbidden =
        [
            "Godot",
            "GodotSharp",
            "Microsoft.AspNetCore",
            "System.Windows.Forms",
            "System.Drawing",
            "PresentationFramework",
        ];

        var declared = CoreProject
            .Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "ProjectReference" or "Reference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty);

        var compiled = CoreAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty);

        var all = declared.Concat(compiled).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = forbidden.Where(all.Contains).ToList();

        Assert.True(
            found.Count == 0,
            $"Shapes.Core must stay UI-agnostic, but references: {string.Join(", ", found)}.");
    }

    [Fact]
    public void Core_types_live_under_the_expected_namespaces()
    {
        // The folder layout is part of the design (PLAN.md project structure). This catches
        // types dropped into the assembly root or into an unplanned namespace.
        string[] allowed =
        [
            "Shapes.Core.Primitives",
            "Shapes.Core.State",
            "Shapes.Core.Actions",
            "Shapes.Core.Effects",
            "Shapes.Core.Rules",
            "Shapes.Core.Cards",
        ];

        var stray = CoreAssembly
            .GetExportedTypes()
            .Where(t => !allowed.Contains(t.Namespace, StringComparer.Ordinal))
            .Select(t => $"{t.FullName}")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            stray.Count == 0,
            $"Public Core types must live under a planned namespace. Stray: {string.Join(", ", stray)}.");
    }
}
