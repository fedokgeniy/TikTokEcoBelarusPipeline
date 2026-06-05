using Microsoft.EntityFrameworkCore;
using TikTokEcoBelarus.Domain.Entities;

namespace TikTokEcoBelarus.Infrastructure.Repositories;

public class SearchQueryRepository(AppDbContext db) : ISearchQueryRepository
{
    public async Task<IList<SearchQuery>> GetActiveQueriesAsync()
        => await db.SearchQueries
            .Where(q => q.IsActive)
            .OrderBy(q => q.Priority)
            .ToListAsync();

    public async Task UpdateLastRunAtAsync(Guid id)
    {
        var query = await db.SearchQueries.FindAsync(id);
        if (query is null) return;
        query.LastRunAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}