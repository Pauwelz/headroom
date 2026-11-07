# Passkeys sit beside the Login code, with Fido2NetLib

A Login code email can take a while to arrive, so a Person can also log in with a Passkey. The Login code stays for everyone: it is the way to log in the first time, on a new device and after a lost Passkey. Headroom verifies Passkeys with Fido2NetLib (the `Fido2` package), not with the Passkey support of ASP.NET Core Identity. The login page offers Passkeys without an email address first, with browser autofill, so it still never reveals whether an email is known.

## Considered Options

- **Passkeys replace the Login code:** breaks first-run setup and leaves no recovery after a lost device.
- **A Person can turn off the Login code:** a lost device then needs a Planner to recover the Person.
- **ASP.NET Core Identity Passkeys (.NET 10):** no new package, but it works only through `UserManager` and an Identity user store. Headroom has neither. Microsoft scopes it to Identity, not to general WebAuthn use.
- **Write the WebAuthn checks ourselves:** too much security code to get right.

## Consequences

- A Passkey is bound to the domain of the Instance. When an Operator changes the domain, all Passkeys stop working, and People fall back to the Login code.
- A Passkey login passes the same `CanLogIn` check as a Login code. An ended Person cannot log in with a Passkey.
- Headroom asks for no attestation, so it needs no FIDO metadata service.
