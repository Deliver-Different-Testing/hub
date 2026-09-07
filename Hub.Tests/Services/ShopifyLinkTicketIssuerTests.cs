using System.Security.Cryptography;
using Hub.Services;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Hub.Tests.Services;

/// <summary>
/// The link ticket: Hub's signed statement that a named merchant authenticated, for one tenant and
/// one store, a moment ago.
/// <para>
/// It exists so the hand-over key stops doubling as an identity assertion. The key says "this caller
/// is our front door"; the ticket says who is behind the request, and the tenant that receives it can
/// check that for itself without trusting the front door at all.
/// </para>
/// <para>
/// Signed with ES256 rather than the shared HMAC secret every deployment already holds: with a
/// symmetric key any tenant could mint a ticket naming any other tenant, which is worse than the
/// arrangement it replaces. Tenants verify with a public key, which is not a secret and can be
/// distributed before anything uses it.
/// </para>
/// </summary>
public class ShopifyLinkTicketIssuerTests : IDisposable
{
    private const string Shop = "a-shop.myshopify.com";
    private const string KeyId = "test-key-1";

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
    private readonly ShopifyLinkTicketKey _ticketKey;

    public ShopifyLinkTicketIssuerTests()
    {
        _ticketKey = new ShopifyLinkTicketKey(_key.ExportPkcs8PrivateKeyPem(), KeyId);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _ticketKey.Dispose();
        _key.Dispose();
    }

    private ShopifyLinkTicketIssuer Issuer() => new(_ticketKey, _clock);

    /// <summary>
    /// Verifies exactly as a tenant's Integration Manager will: the public half of the key, named by
    /// the same <c>kid</c> the header carries, the pinned algorithm, the audience, and a real
    /// lifetime check.
    /// </summary>
    private async Task<JsonWebToken> VerifyAsync(string token, string audience)
    {
        var publicKey = ECDsa.Create();
        publicKey.ImportSubjectPublicKeyInfo(_key.ExportSubjectPublicKeyInfo(), out _);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            IssuerSigningKey = new ECDsaSecurityKey(publicKey) { KeyId = KeyId },
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            ValidIssuer = ShopifyLinkTicket.Issuer,
            ValidAudience = audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.Zero,
            LifetimeValidator = (nbf, exp, _, _) =>
                (nbf is null || nbf <= _clock.GetUtcNow().UtcDateTime) &&
                (exp is not null && exp > _clock.GetUtcNow().UtcDateTime)
        });

        Assert.True(result.IsValid, result.Exception?.Message);
        return (JsonWebToken)result.SecurityToken;
    }

    // ------------------------------------------------------------------ the link ticket

    [Fact]
    public async Task LinkTicket_NamesTheUserTheTenantAndTheShop()
    {
        var ticket = Issuer().IssueLink(7, "merchant@test.com", 42, Shop);

        var jwt = await VerifyAsync(ticket.Token, ShopifyLinkTicket.LinkAudience);

        Assert.Equal("7", jwt.Subject);
        Assert.Equal("merchant@test.com", jwt.GetClaim(ShopifyLinkTicket.EmailClaim).Value);
        Assert.Equal("42", jwt.GetClaim(ShopifyLinkTicket.TenantClaim).Value);
        Assert.Equal(Shop, jwt.GetClaim(ShopifyLinkTicket.ShopClaim).Value);
    }

    [Fact]
    public void LinkTicket_ExpiresInTwoMinutes()
    {
        var ticket = Issuer().IssueLink(7, "merchant@test.com", 42, Shop);

        Assert.Equal(_clock.GetUtcNow().UtcDateTime.AddSeconds(120), ticket.ExpiresAtUtc);
    }

    /// <summary>
    /// A tenant needs to know which public key signed this without being handed a new one out of
    /// band, so rotation is "publish the new key everywhere, then switch Hub's private key" rather
    /// than a flag day.
    /// </summary>
    [Fact]
    public async Task LinkTicket_CarriesTheKeyIdSoTheKeyCanBeRotated()
    {
        var ticket = Issuer().IssueLink(7, "merchant@test.com", 42, Shop);

        Assert.Equal(KeyId, (await VerifyAsync(ticket.Token, ShopifyLinkTicket.LinkAudience)).Kid);
    }

    [Fact]
    public async Task LinkTicket_IsSignedWithEs256AndNotAnHmacSecret()
    {
        var ticket = Issuer().IssueLink(7, "merchant@test.com", 42, Shop);

        Assert.Equal(SecurityAlgorithms.EcdsaSha256,
            (await VerifyAsync(ticket.Token, ShopifyLinkTicket.LinkAudience)).Alg);
    }

    [Fact]
    public async Task LinkTicket_IsUniquePerIssue_SoAReplayCanBeRecognised()
    {
        var issuer = Issuer();

        var first = await VerifyAsync(issuer.IssueLink(7, "m@test.com", 42, Shop).Token, ShopifyLinkTicket.LinkAudience);
        var second = await VerifyAsync(issuer.IssueLink(7, "m@test.com", 42, Shop).Token, ShopifyLinkTicket.LinkAudience);

        Assert.NotEqual(first.Id, second.Id);
    }

    /// <summary>
    /// The audiences are what keep the two apart. A selection ticket says only "this person proved
    /// who they are"; a link ticket also names the tenant it authorises. Accepting one for the other
    /// would let an unfinished sign-in provision a store.
    /// </summary>
    [Fact]
    public async Task LinkTicket_IsNotAcceptedWhereASelectionTicketIsExpected()
    {
        var ticket = Issuer().IssueLink(7, "m@test.com", 42, Shop);

        await Assert.ThrowsAnyAsync<Exception>(() => VerifyAsync(ticket.Token, ShopifyLinkTicket.SelectAudience));
    }

    [Fact]
    public async Task LinkTicket_WithABodyTamperedWith_FailsVerification()
    {
        var ticket = Issuer().IssueLink(7, "m@test.com", 42, Shop);
        var parts = ticket.Token.Split('.');
        var forged = $"{parts[0]}.{parts[1].Replace('a', 'b')}.{parts[2]}";

        await Assert.ThrowsAnyAsync<Exception>(() => VerifyAsync(forged, ShopifyLinkTicket.LinkAudience));
    }

    [Fact]
    public async Task LinkTicket_TwoMinutesLater_HasExpired()
    {
        var ticket = Issuer().IssueLink(7, "m@test.com", 42, Shop);

        _clock.Advance(TimeSpan.FromSeconds(121));

        await Assert.ThrowsAnyAsync<Exception>(() => VerifyAsync(ticket.Token, ShopifyLinkTicket.LinkAudience));
    }

    /// <summary>
    /// Refused at construction rather than at the first sign-in, so a deployment missing the key
    /// fails on the way up instead of when a merchant tries to use it.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-----BEGIN PRIVATE KEY-----\nnot a key\n-----END PRIVATE KEY-----")]
    public void WithNoUsableSigningKey_RefusesToExist(string? pem)
    {
        Assert.Throws<InvalidOperationException>(() => new ShopifyLinkTicketKey(pem, KeyId));
    }

    // ------------------------------------------------------------------ the selection ticket

    /// <summary>
    /// Issued when a merchant belongs to more than one Shopify-enabled courier. It carries the
    /// authentication forward while they choose, so the password is typed once and not held in the
    /// browser across the interaction.
    /// </summary>
    [Fact]
    public void SelectionTicket_ReadsBackTheUserTheShopAndTheCandidates()
    {
        var issuer = Issuer();
        var ticket = issuer.IssueSelection(7, "m@test.com", Shop, [42, 43]);

        var selection = issuer.ReadSelection(ticket.Token);

        Assert.NotNull(selection);
        Assert.Equal(7, selection.UserId);
        Assert.Equal("m@test.com", selection.Email);
        Assert.Equal(Shop, selection.Shop);
        Assert.Equal([42, 43], selection.TenantIds);
    }

    /// <summary>
    /// The candidate list in the ticket is not the authority - membership is re-read from master
    /// before a link ticket is issued - but it must still be signed, or the second call becomes a way
    /// to name any tenant at all.
    /// </summary>
    [Fact]
    public void SelectionTicket_TamperedWith_ReadsBackAsNothing()
    {
        var issuer = Issuer();
        var parts = issuer.IssueSelection(7, "m@test.com", Shop, [42]).Token.Split('.');

        Assert.Null(issuer.ReadSelection($"{parts[0]}.{parts[1].Replace('a', 'b')}.{parts[2]}"));
    }

    [Fact]
    public void SelectionTicket_AfterItExpires_ReadsBackAsNothing()
    {
        var issuer = Issuer();
        var ticket = issuer.IssueSelection(7, "m@test.com", Shop, [42]);

        _clock.Advance(TimeSpan.FromSeconds(121));

        Assert.Null(issuer.ReadSelection(ticket.Token));
    }

    /// <summary>
    /// A link ticket presented where a selection ticket belongs is the dangerous direction: it would
    /// let a ticket already bound to one tenant be exchanged for one naming another.
    /// </summary>
    [Fact]
    public void SelectionTicket_ALinkTicketIsNotOne()
    {
        var issuer = Issuer();

        Assert.Null(issuer.ReadSelection(issuer.IssueLink(7, "m@test.com", 42, Shop).Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-jwt")]
    public void SelectionTicket_ThatIsNotATicketAtAll_ReadsBackAsNothing(string token)
    {
        Assert.Null(Issuer().ReadSelection(token));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
