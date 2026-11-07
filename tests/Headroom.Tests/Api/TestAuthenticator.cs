using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fido2NetLib.Cbor;
using Fido2NetLib.Objects;

namespace Headroom.Tests.Api;

// A software authenticator: one EC P-256 key and one credential. It answers the options of the server as a browser
// would, and returns the JSON of credential.toJSON(). Use it for the registration and the login of a Passkey.
public sealed class TestAuthenticator(string rpId = "localhost", string origin = "https://localhost") : IDisposable
{
    private const AuthenticatorFlags Signed = AuthenticatorFlags.UP | AuthenticatorFlags.UV | AuthenticatorFlags.BE | AuthenticatorFlags.BS;

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(32);

    // The user handle that the server put in the creation options. The assertion returns it.
    public byte[]? UserHandle { get; private set; }

    // A wrong origin makes a forged response.
    public string CreateAttestation(string optionsJson, string? forgedOrigin = null)
    {
        using var options = JsonDocument.Parse(optionsJson);
        UserHandle = Base64Url.DecodeFromChars(options.RootElement.GetProperty("user").GetProperty("id").GetString()!);
        var authData = new AuthenticatorData(RpHash(), Signed | AuthenticatorFlags.AT, 0,
            new AttestedCredentialData(Guid.Empty, CredentialId, new CredentialPublicKey(_key, COSE.Algorithm.ES256))).ToByteArray();
        var attestation = new CborMap { { "fmt", "none" }, { "attStmt", new CborMap() }, { "authData", authData } }.Encode();
        var clientData = ClientData("webauthn.create", options.RootElement.GetProperty("challenge").GetString()!, forgedOrigin);
        return Credential($$"""{"clientDataJSON":"{{B64(clientData)}}","attestationObject":"{{B64(attestation)}}","transports":["internal"]}""");
    }

    // signWith signs with another key: a forged assertion.
    public string CreateAssertion(string optionsJson, uint signCount = 1, string? forgedOrigin = null, ECDsa? signWith = null, bool sendUserHandle = true)
    {
        using var options = JsonDocument.Parse(optionsJson);
        var authData = new AuthenticatorData(RpHash(), Signed, signCount, null).ToByteArray();
        var clientData = ClientData("webauthn.get", options.RootElement.GetProperty("challenge").GetString()!, forgedOrigin);
        var signature = (signWith ?? _key).SignData([.. authData, .. SHA256.HashData(clientData)], HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        var handle = sendUserHandle && UserHandle is not null ? $"\"{B64(UserHandle)}\"" : "null";
        return Credential($$"""{"clientDataJSON":"{{B64(clientData)}}","authenticatorData":"{{B64(authData)}}","signature":"{{B64(signature)}}","userHandle":{{handle}}}""");
    }

    public void Dispose() => _key.Dispose();

    private byte[] RpHash() => SHA256.HashData(Encoding.UTF8.GetBytes(rpId));

    private byte[] ClientData(string type, string challenge, string? forgedOrigin) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type, challenge, origin = forgedOrigin ?? origin, crossOrigin = false }));

    private string Credential(string response) =>
        $$"""{"id":"{{B64(CredentialId)}}","rawId":"{{B64(CredentialId)}}","type":"public-key","clientExtensionResults":{},"response":{{response}}}""";

    private static string B64(byte[] bytes) => Base64Url.EncodeToString(bytes);
}
