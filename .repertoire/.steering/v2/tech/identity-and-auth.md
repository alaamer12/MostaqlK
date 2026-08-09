# Identity and authentication

Local-first device identity for the monitor, with a clean upgrade path to cryptographic peer identity when multi-device sync arrives.

This is deliberately **not** a user-account system. There is no login, no email, no password, no cloud identity provider, and no server-side user table in any version.

## Goals

- Distinguish one install from another on the same machine (crash reports, optional telemetry tags, local “this device” references).
- Keep zero user-facing recovery or key-management UI in MVP and v2.
- Leave a zero-rewrite seam so that the same material can later become a real signing identity for LAN peer sync (see [roadmap-future.md](../product/roadmap-future.md)).

## Buzz-style lite (current approach)

On first launch the app generates a real secp256k1 keypair and stores the private key in the platform secure store. The keypair is **dormant**: it is never used for signing, challenge-response, or any cryptographic proof until peer sync is implemented.

### What is stored

| Item | Where | Lifetime |
|---|---|---|
| Private key | Platform SecureStorage (see below) | Survives normal app reinstalls on the same OS user; lost on OS wipe / factory reset / different Windows user |
| Public identifier | Derived from the public key (or a UUID bound to the keypair) | Same lifetime as the private key |
| Nothing else | — | No seed phrase, no recovery file, no cloud backup |

### Platform secure store

Accessed through MAUI’s `SecureStorage` abstraction (or equivalent):

| Platform | Native store |
|---|---|
| Windows | Credential Manager / DPAPI |
| macOS | Keychain Services |
| iOS | iOS Keychain |
| Android | Android Keystore (hardware-backed when available) |
| Linux | libsecret / GNOME Keyring / KWallet |

Code surface is intentionally tiny:

```csharp
await SecureStorage.SetAsync("device_identity_private", privateKeyBytes);
var key = await SecureStorage.GetAsync("device_identity_private");
```

### What the public identifier is used for today

- Distinguishing this install from another install on the same machine
- Optional crash / telemetry tags
- Local “device ID” that the later peer-sync layer can promote

It is **not** shown to the user, not used for any authorization decision, and not required for the core poll → enrich → store → notify pipeline.

### Behaviour on loss

If the secure store entry disappears (OS wipe, different Windows user, etc.) the app simply generates a fresh keypair. This is acceptable while the key is dormant: nothing has ever been signed with the old material<App should emit warning alert for that>, so there is no orphaned identity to recover.

## Why not full Buzz / Nostr auth yet

Full Buzz-style authentication (NIP-42 WebSocket challenge-response, NIP-98 HTTP signed events, scopes, channel membership, etc.) assumes:

- a relay or peer that verifies signatures, and
- a reason to prove ownership of a private key.

Neither exists in MVP or v2. The product is strictly local. Introducing challenge-response, recovery UX, or “this is your master credential” messaging would add surface area with no corresponding benefit.

The lite approach keeps the cryptographic material in the correct place and format so that the upgrade cost is near zero when the need appears.

## Future: full cryptographic identity (v3 peer sync)

When LAN pairing / multi-device union-merge lands (see [roadmap-future.md](../product/roadmap-future.md) and the reusable `Resolve(candidates, providers)` shape in [diff-engine.md](../../v1/tech/diff-engine.md)), the dormant keypair is activated:

1. The existing private key begins signing peer manifests.
2. A minimal pairing flow is added (short-lived QR / pairing secret, or optional encrypted export of the key material).
3. Signature verification becomes part of the peer provider that feeds the diff engine.
4. Authorization stays local: “does this public key belong to a device I previously paired?” — still no cloud, no central user table, no JWT.

At that point the identity model becomes closer to classic Buzz/Nostr:

- Public key = device (or owner) identity
- Private key never leaves the device
- Proof is a signature, not a session token

Until then the keypair remains completely silent.

## Explicit non-goals

- No email / password / OAuth / social login
- No server-side user records
- No recovery phrase or backup UI in MVP/v2
- No MACHINE-ID / hardware fingerprint as the primary identity (unstable across reinstalls and restricted on modern platforms)
- No NIP-42 / NIP-98 implementation until peer sync exists

## Related docs

- [diff-engine.md](../../v1/tech/diff-engine.md) — the abstraction that will later consume signed peer manifests
- [roadmap-future.md](../product/roadmap-future.md) — mobile companion + LAN peer sync
- [concurrency-model.md](../../v1/tech/concurrency-model.md) — in-flight tracking is independent of identity
- [MVP.md](../../v1/product/README.md) — identity work is outside the MVP cut
