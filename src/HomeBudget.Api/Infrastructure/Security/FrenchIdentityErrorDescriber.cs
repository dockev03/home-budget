using Microsoft.AspNetCore.Identity;

namespace HomeBudget.Api.Infrastructure.Security;

/// <summary>Messages d'erreur ASP.NET Identity en français (ceux exposés à l'utilisateur).</summary>
public sealed class FrenchIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError() =>
        new() { Code = nameof(DefaultError), Description = "Une erreur inconnue est survenue." };

    public override IdentityError ConcurrencyFailure() =>
        new() { Code = nameof(ConcurrencyFailure), Description = "Les données ont été modifiées entre-temps. Rechargez puis réessayez." };

    public override IdentityError PasswordMismatch() =>
        new() { Code = nameof(PasswordMismatch), Description = "Mot de passe incorrect." };

    public override IdentityError InvalidToken() =>
        new() { Code = nameof(InvalidToken), Description = "Code invalide ou expiré." };

    public override IdentityError InvalidUserName(string? userName) =>
        new() { Code = nameof(InvalidUserName), Description = "Identifiant invalide : lettres, chiffres et . _ - @ uniquement." };

    public override IdentityError InvalidEmail(string? email) =>
        new() { Code = nameof(InvalidEmail), Description = "Adresse e-mail invalide." };

    public override IdentityError DuplicateUserName(string userName) =>
        new() { Code = nameof(DuplicateUserName), Description = "Cet identifiant est déjà utilisé." };

    public override IdentityError DuplicateEmail(string email) =>
        new() { Code = nameof(DuplicateEmail), Description = "Cette adresse e-mail est déjà utilisée." };

    public override IdentityError PasswordTooShort(int length) =>
        new() { Code = nameof(PasswordTooShort), Description = $"Le mot de passe doit contenir au moins {length} caractères." };

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        new() { Code = nameof(PasswordRequiresUniqueChars), Description = $"Le mot de passe doit contenir au moins {uniqueChars} caractères différents." };

    public override IdentityError UserLockoutNotEnabled() =>
        new() { Code = nameof(UserLockoutNotEnabled), Description = "Le verrouillage n'est pas activé pour cet utilisateur." };
}
