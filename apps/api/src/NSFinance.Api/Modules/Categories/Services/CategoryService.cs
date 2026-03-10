using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.Categories.DTOs;
using NSFinance.Api.Persistence;

namespace NSFinance.Api.Modules.Categories.Services;

public sealed class CategoryService(AppDbContext dbContext)
{
    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        return await dbContext.TransactionCategories
            .AsNoTracking()
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Name)
            .Select(x => new CategoryDto(x.Id, x.Name, x.Type, x.CreatedUtc))
            .ToListAsync(cancellationToken);
    }
}
