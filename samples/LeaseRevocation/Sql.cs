// SQL classifier — sqlglot-equivalent in production. Returns the operation
// kind plus referenced tables.
namespace ARCP.Samples.LeaseRevocation;

public sealed record StatementClass(string Op, IReadOnlyCollection<string> Tables);

internal static class Sql
{
    // Real version: parse SQL into an AST, walk Table nodes for `Tables`,
    // pattern-match Insert/Update/Delete/Merge for `Op`.
    public static StatementClass Classify(string sql) => throw new NotImplementedException();
}
