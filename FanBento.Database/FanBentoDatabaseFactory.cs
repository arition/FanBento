using Microsoft.EntityFrameworkCore.Design;

namespace FanBento.Database;

/// <summary>Lets EF tools inspect the model without loading worker settings or credentials.</summary>
public sealed class FanBentoDatabaseFactory : IDesignTimeDbContextFactory<FanBentoDatabase>
{
    /// <summary>Uses a local design-time database; update commands can override it with --connection.</summary>
    public FanBentoDatabase CreateDbContext(string[] args) => new("Data Source=fanbento-design.db");
}
