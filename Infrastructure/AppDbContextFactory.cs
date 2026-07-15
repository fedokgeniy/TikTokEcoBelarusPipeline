using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace TikTokEcoBelarus.Infrastructure;

/// <summary>
/// Used by EF Core Tools (dotnet ef migrations add / database update) at design time.
/// Reads connection string from appsettings.json so that the full DI container
/// (including CommentClassifierService etc.) does not need to be constructed.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(config.GetConnectionString("Default"));

        return new AppDbContext(optionsBuilder.Options);
    }
}
