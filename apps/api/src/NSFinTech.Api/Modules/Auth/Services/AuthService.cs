using Microsoft.EntityFrameworkCore;
using NSFinTech.Api.Modules.Auth.DTOs;
using NSFinTech.Api.Modules.Users.Services;
using NSFinTech.Api.Persistence;
using NSFinTech.Api.Persistence.Entities;

namespace NSFinTech.Api.Modules.Auth.Services;

public sealed class AuthService(
    AppDbContext dbContext,
    IPasswordHasher passwordHasher,
    JwtTokenService jwtTokenService,
    ICurrentUserProvider currentUserProvider)
{
    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var emailExists = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(x => x.Email == normalizedEmail, cancellationToken);

        if (emailExists)
        {
            return new AuthResult(null, "An account with this email already exists.", Conflict: true);
        }

        var utcNow = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            PasswordHash = passwordHasher.HashPassword(request.Password),
            FirstName = request.FirstName?.Trim(),
            LastName = request.LastName?.Trim(),
            CreatedUtc = utcNow,
            LastLoginUtc = utcNow
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        var response = CreateTokenResponse(user);
        return new AuthResult(response, null);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await dbContext.Users
            .SingleOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);

        if (user is null || !passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            return new AuthResult(null, "Invalid email or password.");
        }

        user.LastLoginUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var response = CreateTokenResponse(user);
        return new AuthResult(response, null);
    }

    public async Task<UserProfileDto?> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return null;
        }

        return await dbContext.Users
            .AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new UserProfileDto(
                x.Id,
                x.Email,
                x.FirstName,
                x.LastName,
                x.CreatedUtc,
                x.LastLoginUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private AuthTokenResponse CreateTokenResponse(User user)
    {
        var (accessToken, expiresAtUtc) = jwtTokenService.CreateAccessToken(user);

        return new AuthTokenResponse(
            accessToken,
            expiresAtUtc,
            new UserProfileDto(
                user.Id,
                user.Email,
                user.FirstName,
                user.LastName,
                user.CreatedUtc,
                user.LastLoginUtc));
    }
}
