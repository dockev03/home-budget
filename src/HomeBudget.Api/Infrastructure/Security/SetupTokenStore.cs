using System.Security.Cryptography;
using System.Text;

namespace HomeBudget.Api.Infrastructure.Security;

/// <summary>
/// Jeton à usage unique exigé pour créer le premier administrateur. Il est généré au démarrage
/// (ou imposé par configuration) tant qu'aucun utilisateur n'existe, puis désactivé.
/// </summary>
public sealed class SetupTokenStore
{
    private readonly Lock _lock = new();
    private string? _token;

    public bool IsActive
    {
        get
        {
            lock (_lock)
            {
                return _token is not null;
            }
        }
    }

    public string Activate(string? configuredToken)
    {
        lock (_lock)
        {
            _token = string.IsNullOrWhiteSpace(configuredToken)
                ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=')
                : configuredToken.Trim();
            return _token;
        }
    }

    public bool Validate(string? candidate)
    {
        lock (_lock)
        {
            if (_token is null || string.IsNullOrEmpty(candidate))
            {
                return false;
            }
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(_token), Encoding.UTF8.GetBytes(candidate.Trim()));
        }
    }

    public void Deactivate()
    {
        lock (_lock)
        {
            _token = null;
        }
    }
}
