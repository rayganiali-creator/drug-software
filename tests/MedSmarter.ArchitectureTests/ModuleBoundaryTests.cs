using System.Xml.Linq;

namespace MedSmarter.ArchitectureTests;

/// <summary>
/// Enforces the Phase 0 modular-monolith rules (02-architecture.md §4) by inspecting ProjectReferences,
/// which is reliable even for projects that have no code yet.
/// </summary>
public class ModuleBoundaryTests
{
    private static readonly string[] ExpectedModules =
    [
        "Identity", "Users", "Patients", "Physicians", "Pharmacists", "Pharmacies", "Medications",
        "ActiveIngredients", "Prescriptions", "MedicationSchedule", "MedicationIntake", "Adherence",
        "Symptoms", "ADR", "AI", "KnowledgeBase", "ClinicalRules", "Notifications", "Consent", "Audit",
        "Analytics", "Integrations",
    ];

    private sealed record Proj(string Name, string Path, IReadOnlyList<string> References);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MedSmarter.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("MedSmarter.sln not found");
    }

    private static List<Proj> LoadProjects() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(p => new Proj(
                System.IO.Path.GetFileNameWithoutExtension(p),
                p,
                XDocument.Load(p).Descendants("ProjectReference")
                    .Select(e => System.IO.Path.GetFileNameWithoutExtension(((string)e.Attribute("Include")!).Replace('\\', '/')))
                    .ToList()))
            .ToList();

    [Fact]
    public void All_22_phase0_modules_exist_with_contracts_project()
    {
        var names = LoadProjects().Select(p => p.Name).ToHashSet();
        foreach (var m in ExpectedModules)
        {
            Assert.Contains($"MedSmarter.Modules.{m}", names);
            Assert.Contains($"MedSmarter.Modules.{m}.Contracts", names);
        }
    }

    [Fact]
    public void Modules_only_reference_other_modules_through_Contracts()
    {
        var violations = new List<string>();
        foreach (var p in LoadProjects().Where(p => p.Name.StartsWith("MedSmarter.Modules.", StringComparison.Ordinal)))
        {
            foreach (var r in p.References.Where(r => r.StartsWith("MedSmarter.Modules.", StringComparison.Ordinal)))
            {
                var ownContracts = p.Name + ".Contracts";
                var isOther = r != ownContracts && !p.Name.StartsWith(r + ".", StringComparison.Ordinal);
                if (isOther && !r.EndsWith(".Contracts", StringComparison.Ordinal))
                {
                    violations.Add($"{p.Name} -> {r}");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void Contracts_projects_reference_nothing_except_other_Contracts()
    {
        var bad = LoadProjects()
            .Where(p => p.Name.EndsWith(".Contracts", StringComparison.Ordinal))
            .SelectMany(p => p.References.Where(r => !r.EndsWith(".Contracts", StringComparison.Ordinal)).Select(r => $"{p.Name} -> {r}"));
        Assert.Empty(bad);
    }

    [Fact]
    public void Building_blocks_never_depend_on_modules_or_host()
    {
        var bad = LoadProjects()
            .Where(p => p.Name.StartsWith("MedSmarter.BuildingBlocks", StringComparison.Ordinal))
            .SelectMany(p => p.References.Where(r => r.Contains(".Modules.", StringComparison.Ordinal) || r == "MedSmarter.Api"));
        Assert.Empty(bad);
    }

    [Fact]
    public void Module_dependency_graph_has_no_cycles()
    {
        var projects = LoadProjects().ToDictionary(p => p.Name);
        var state = new Dictionary<string, int>();
        bool Visit(string n)
        {
            if (!projects.TryGetValue(n, out var p)) { return false; }
            if (state.GetValueOrDefault(n) == 1) { return true; }
            if (state.GetValueOrDefault(n) == 2) { return false; }
            state[n] = 1;
            var cyc = p.References.Any(Visit);
            state[n] = 2;
            return cyc;
        }

        Assert.DoesNotContain(projects.Keys, k => Visit(k));
    }

    [Fact]
    public void Phase7_the_clinical_safety_engine_depends_on_no_model_provider_or_network_client()
    {
        var all = LoadProjects();
        var engine = all.Single(p => p.Name == "MedSmarter.Modules.ClinicalRules");
        // Matching, urgency and safety are decided by fixed code: the module must not even be able to reach the AI module or the Identity internals.
        Assert.DoesNotContain(engine.References, r => r.StartsWith("MedSmarter.Modules.AI", StringComparison.Ordinal));
        Assert.DoesNotContain(engine.References, r => r == "MedSmarter.Modules.Identity");
        var xml = XDocument.Load(engine.Path);
        var packages = xml.Descendants("PackageReference").Select(e => (string?)e.Attribute("Include") ?? string.Empty).ToList();
        Assert.DoesNotContain(packages, p => p.Contains("Http", StringComparison.OrdinalIgnoreCase) || p.Contains("OpenAI", StringComparison.OrdinalIgnoreCase) || p.Contains("Anthropic", StringComparison.OrdinalIgnoreCase));
        // and no source file of the module opens a network connection
        var dir = System.IO.Path.GetDirectoryName(engine.Path)!;
        var code = string.Join('\n', Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains("/obj/") && !f.Contains("/Migrations/")).Select(File.ReadAllText));
        Assert.DoesNotContain("HttpClient", code, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Net", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FromSqlRaw", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteSql", code, StringComparison.Ordinal);
    }
}
