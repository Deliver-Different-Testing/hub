using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Hub.Services;

/// <summary>
/// The short-lived token the DFRNT Drive app hands to Hub so a courier opens After Hours already
/// signed in (UC #866). MarsAPI issues it (POST api/mobile/afterhours/signin-token) for the courier
/// on the app's own session; Hub redeems it at /Account/AcceptCourierAppToken.
/// <para>
/// Format: <c>base64url(json) "." base64url(HMAC-SHA256(key, base64url(json)))</c>, where key is the
/// base64 <c>CourierAppSsoKey</c> both services share. Signed rather than only encrypted (unlike the
/// tenant-switch token) so a changed payload is always rejected. MarsAPI's copy of
/// <see cref="Create"/> must stay byte-identical.
/// </para>
/// </summary>
public static class CourierAppToken
{
    public sealed record Payload(int UserId, int TenantId, int CourierId, long ExpiresAt);

    public static string Create(Payload payload, string base64Key)
    {
        var body = Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(payload));
        return body + "." + Base64UrlEncoder.Encode(Sign(body, base64Key));
    }

    /// <summary>The payload when the signature checks out, otherwise null. Expiry is the caller's check.</summary>
    public static Payload? Read(string? token, string base64Key)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrEmpty(base64Key)) return null;

        var parts = token.Split('.');
        if (parts.Length != 2) return null;

        try
        {
            var expected = Sign(parts[0], base64Key);
            var actual = Base64UrlEncoder.DecodeBytes(parts[1]);
            if (!CryptographicOperations.FixedTimeEquals(expected, actual)) return null;

            return JsonSerializer.Deserialize<Payload>(Base64UrlEncoder.DecodeBytes(parts[0]));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static byte[] Sign(string body, string base64Key) =>
        HMACSHA256.HashData(Convert.FromBase64String(base64Key), Encoding.ASCII.GetBytes(body));
}
