using TikTokEcoBelarus.Domain.Entities;

namespace TikTokEcoBelarus.Infrastructure.Repositories;

public interface ISearchQueryRepository
{
    Task<IList<SearchQuery>> GetActiveQueriesAsync();
    Task UpdateLastRunAtAsync(Guid id);
}