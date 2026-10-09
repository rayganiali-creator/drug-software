using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MedSmarter.BuildingBlocks;

/// <summary>Column, key, index and constraint names in snake_case (the platform schema's convention), shared by the module databases.</summary>
public static partial class SnakeCase
{
    public static void Apply(ModelBuilder b)
    {
        foreach (var e in b.Model.GetEntityTypes())
        {
            var table = e.GetTableName();
            if (table is null)
            {
                continue;
            }

            foreach (var p in e.GetProperties())
            {
                if (p.GetColumnName() == p.Name)
                {
                    p.SetColumnName(Name(p.Name));
                }
            }

            foreach (var k in e.GetKeys())
            {
                k.SetName($"pk_{table}");
            }

            foreach (var fk in e.GetForeignKeys())
            {
                fk.SetConstraintName($"fk_{table}_{fk.PrincipalEntityType.GetTableName()}_{string.Join('_', fk.Properties.Select(p => Name(p.Name)))}");
            }

            foreach (var i in e.GetIndexes().Where(i => i.GetDatabaseName() is null or { Length: 0 } || i.GetDatabaseName()!.StartsWith("IX_", StringComparison.Ordinal)))
            {
                i.SetDatabaseName($"{(i.IsUnique ? "ux" : "ix")}_{table}_{string.Join('_', i.Properties.Select(p => Name(p.Name)))}");
            }
        }
    }

    public static string Name(string name) => LowerUpper().Replace(Acronym().Replace(name, "$1_$2"), "$1_$2").ToLowerInvariant();

    [GeneratedRegex("([A-Z]+)([A-Z][a-z])")]
    private static partial Regex Acronym();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex LowerUpper();
}
