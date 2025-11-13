using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace CryptoBlade.Authentication
{
    public sealed class ApiKeyAuthenticationHandler
        : AuthenticationHandler<ApiKeySchemeOptions>
    {
        public new const string Scheme = "ApiKeyScheme";
        private readonly ApiKeyOptions _opt;

        public ApiKeyAuthenticationHandler(
            IOptionsMonitor<ApiKeySchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IOptions<ApiKeyOptions> apiKeyOptions)
            : base(options, logger, encoder)
            => _opt = apiKeyOptions.Value;

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-API-TOKEN", out var token) ||
                token != _opt.Token)
                return Task.FromResult(AuthenticateResult.Fail("Bad token"));

            var identity = new ClaimsIdentity(Scheme);
            identity.AddClaim(new Claim(ClaimTypes.Name, "ApiClient"));
            var ticket = new AuthenticationTicket(new(identity), Scheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}