namespace NSFinTech.Api.Persistence.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }

    public ICollection<FinancialAccount> FinancialAccounts { get; set; } = [];
    public ICollection<ImportJob> ImportJobs { get; set; } = [];
}
