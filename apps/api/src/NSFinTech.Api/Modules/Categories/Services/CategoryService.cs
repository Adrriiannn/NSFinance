using Microsoft.EntityFrameworkCore;
using NSFinTech.Api.Modules.Categories.DTOs;
using NSFinTech.Api.Persistence;

namespace NSFinTech.Api.Modules.Categories.Services;

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
