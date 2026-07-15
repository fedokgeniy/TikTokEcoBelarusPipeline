using Microsoft.EntityFrameworkCore;
using TikTokEcoBelarus.Domain.Entities;

namespace TikTokEcoBelarus.Infrastructure.Repositories;

/// <summary>
/// Repository that creates its own short-lived DbContext per operation via IDbContextFactory.
/// This avoids ObjectDisposedException when CollectionPipeline keeps the repo alive across
/// multiple async iterations while PipelineOrchestrator has already disposed the scoped context.
/// </summary>
public class SearchQueryRepository(IDbContextFactory<AppDbContext> dbFactory) : ISearchQueryRepository
{
    public async Task<IList<SearchQuery>> GetActiveQueriesAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.SearchQueries
            .Where(q => q.IsActive)
            .OrderBy(q => q.Priority)
            .ToListAsync();
    }

    public async Task UpdateLastRunAtAsync(Guid id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = await db.SearchQueries.FindAsync(id);
        if (query is null) return;
        query.LastRunAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}
