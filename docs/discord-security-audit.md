# Discord remote-control security audit

Date: 23 October 2026. Scope: the current local implementation of `discord-worker`, the desktop `Remote` connection and command handlers, Epic credential handling reached by those commands, and the associated deployment/package boundary. This is a source review plus local adversarial testing by the implementation agent, **not an independent penetration test or a guarantee of security**. No production service was deployed or contacted with production credentials.

## Result

The identified implementation weaknesses listed below have been fixed and regression-tested where practical. No unauthenticated cross-user command-execution path was reproduced in the original public routes. The largest improvements reduce the damage from a compromised Worker or copied device credential, and make the desktop independently enforce the signed Discord user's identity.

The updated installed app contains **no shared Discord bot token, Cloudflare API token or reusable device bearer token**. Each PC creates a non-exportable Windows CNG signing key; the cloud receives only its public key. The desktop independently verifies the original Discord signature and pinned application/user identity before executing any command. This is protocol v2 and requires re-pairing.

This does not make a compromised Windows user session or malicious desktop build safe. It also does not make messages and screenshots confidential from Discord or the Worker operator. Public deployment still requires the operational checks below.

## Findings and fixes

| Finding | Impact / conditions | Fix and evidence |
| --- | --- | --- |
| Worker could invent permitted desktop commands | High impact if Worker code or deployment access was compromised; the original client trusted the poll response | Forward the original signed Discord interaction. `RemoteCommandProof` verifies Ed25519 with a locally pinned key, application ID, invoking user, command ID, action, argument types/values, freshness and pairing date. Tests reject substituted users, apps, keys, actions, account numbers and unsigned/tampered/expired proofs. Local capability checks still apply. |
| Reusable device bearer token | Copying it allowed device impersonation for that pairing: polling, forged results and revocation. It did not by itself forge a Discord command | Replaced with a per-PC CNG P-256 key whose export policy is `None`. Requests sign the HTTPS audience, user, route, method, exact body hash, timestamp and nonce. Worker stores public keys only. Captured requests cannot authenticate another route/user/body or be replayed. Pairings remain until explicit unlink/revoke; individual request and command expiry still prevent replay. Windows export-refusal and CNG/Web Crypto interoperability tests pass. |
| Desktop replay protection was memory-only | A restart could clear the client's record of accepted commands, particularly relevant under Worker compromise | Encrypt and atomically flush a ten-minute replay journal before dispatch. Failure to read/write it denies execution. Exclusive file ownership prevents two Pilot processes sharing the connection. Reload, corrupt-journal and duplicate-instance tests pass. |
| Unauthenticated bodies were read while holding the per-user object lock | A slow sender could delay legitimate commands/revocation until the Durable Object's timeout | Bound body size and enforce a five-second read deadline at the edge before dispatch to the object. Public device endpoints remain rate-limited. Stream timeout/size tests pass. Object state transitions stay serialized. |
| Command quota could block `/pilot unlink` | A user hitting the replay/rate quota could temporarily lose the ability to revoke | Process fresh signed unlink before command quota checks while retaining replay and revocation cutoffs. Test fills the quota, successfully unlinks, and rejects older pairing requests. |
| Five incorrect pairing attempts could lock out the owner | Anyone knowing the public Discord user ID could exhaust that user's pairing attempts | Remove the per-user failed-guess allowance; keep a 128-bit random, five-minute, single-use code and edge rate limiting. Incorrect guesses no longer change enrollment state. Tests verify the owner can pair after bad guesses and expired/wrong-user codes are rejected. |
| Pairing lifecycle and cleanup gaps | Concurrent/aborted pairing or failed persistence/revoke could leave confusing local connection state | Serialize configuration operations; link pairing to dialog/app cancellation; delete uncommitted CNG keys in `finally`; save configuration atomically; cancel before disconnect; delete key/configuration even when cloud revoke fails. Expired pairings disable locally. Tests verify authenticated operation and both successful and failed cloud-revoke cleanup. An unusable cloud reservation can still require `/pilot unlink` after an interrupted pair. |
| Redirects and unbounded external response waits | Secret-bearing outbound traffic could follow an unintended redirect; a Discord response could occupy the object lock too long | Desktop Worker and Epic clients disable redirects/cookies. Worker Discord replies and command registration use a fixed destination, redirect rejection and explicit timeouts. Device signatures bind the configured origin. Exact-origin routing rejects aliases/query strings. Response tests check the destination, redirect policy and timeout signal. |
| Sensitive record string formatting | Default record formatting could expose Epic secrets, approval details, interaction reply tokens or screenshot/result data if later logged | Redact `ToString()` for Epic accounts/sign-in records, Discord proofs and remote results. Production connection errors remain generic. Existing remote replies use explicit allowed fields instead of serializing account/settings objects. Redaction tests pass. This does not protect arbitrary future serializers or process dumps. |
| Mutable CI action references and build-time credential exposure | An action tag could change; persisted checkout credentials exposed a release write token to later build tools | Pin actions in both workflows to verified full commit SHAs; disable persisted checkout credentials. Release source preparation and tag publishing obtain the short-lived job token via the GitHub CLI credential helper in their own steps, without persisting it for builds. Worker installs use the lockfile with lifecycle scripts disabled; the check job runs an advisory scan. Production secrets are scoped to the deployment/registration steps and GitHub environment. Actual GitHub environment protections still require administrator configuration. |

## Retained protections checked

- Worker verifies Discord signatures and application IDs before resolving a user object. Public routing never forwards the internal interaction-dispatch endpoint. Ordinary user-controlled command options cannot select a victim's device.
- Pairing binds a public key to the signed Discord owner. Existing devices cannot be overwritten by a new pairing code; unlink is required. Concurrent pairing allows one winner; concurrent polls deliver a command once.
- Authenticated results must match a delivered, unexpired pending command. Responses cannot choose their Discord webhook destination. Screenshot uploads are bounded, accepted only for screenshot commands and relayed as ephemeral attachments. Mentions are disabled.
- The desktop independently gates screenshots and game/account actions, both off by default. Unknown commands are denied. There is no remote shell, file-download, arbitrary-executable, desktop-capture or arbitrary-URL action.
- Screenshots use Windows Graphics Capture for the Fortnite window with no desktop fallback. Progress/statistics replies omit diagnostic logs, local paths, machine names, account IDs and credentials. `/accounts` deliberately reveals display names only to the invoking user.
- Epic authentication goes directly from the PC to Epic. Device credentials use existing current-user DPAPI storage. No Epic password, refresh/device secret or authorization code is sent to the Worker. `/login` intentionally sends a short-lived approval URL, which must be treated as sensitive.
- Interaction reply tokens and signed payloads are removed from object state on completion or expiry. Screenshots/results are not persisted by the Worker. Revocation clears pending work and public device enrollment. Cloudflare/Discord platform retention remains outside the application's control.
- The release bundle allowlist was updated for the Ed25519 verification library. The bundle audit rejects unexpected files, embedded application debug data and local build usernames/paths.

## Validation completed

| Check | Result |
| --- | --- |
| Worker Node tests | 17 passed, including a local Cloudflare runtime with concurrent claims/polls, signed-body/audience/owner binding, replay, expiry, revocation, body/time limits and Windows CNG signature interoperability |
| Desktop remote checks in the packaged app | 61 passed: 31 capability/URL checks plus 30 cryptographic, replay, key-policy and connection checks |
| Complete isolated single-executable self-check | 300 passed |
| Existing core checks during packaging | 981 passed |
| Release single-executable publish/bundle audit | Passed; expected files only, no personal app build paths/debug data detected |
| Worker deployment dry run | Passed; no deployment performed |
| `npm audit --audit-level=moderate` | No known vulnerabilities reported at audit time |
| `.NET` package advisory scan including transitives | No known vulnerabilities reported by the configured feeds at audit time |
| Release PowerShell syntax / Git whitespace checks | Passed |

The Windows signing-key tests create temporary keys and delete them. The committed CNG interoperability fixture contains a public key and a signature only; its private key was deleted. Tests use synthetic account IDs and reply tokens. No real Epic login, game action, production Discord command or public deployment was performed. The GitHub release credential-helper change was syntax-reviewed against the official CLI behavior; an authenticated release run was not performed and `gh` is not installed in this local environment.

## Remaining trust boundaries and release checks

1. **Windows compromise:** CNG non-exportability prevents ordinary private-key export, not use of the key by malware running as the same user or an administrator. Epic tokens necessarily exist in memory during use. Fortnite's launch protocol places a short-lived exchange code on its process command line. Do not claim credentials can never be stolen from an infected PC.
2. **Trusted identity bootstrap:** publish the official application ID/public key/origin in a reviewed, trusted release/setup channel. Users must not take replacement verification keys from an untrusted Worker. Protect the desktop source, release pipeline, signing/distribution credentials and update links. A malicious installer can bypass all client checks.
3. **Discord compromise:** someone controlling the paired Discord account can issue authentic commands within its local permissions. Require strong account security/MFA. Discord application/bot credentials and its Developer Portal must also be protected; app administrators can alter registration/configuration.
4. **Worker compromise:** signed proof prevents the Worker inventing a new user's command, but it can inspect/alter replies, retain screenshots, delay or withhold valid commands, deny service and suppress cloud revocation. It can relay a genuine, not-yet-executed command during its short validity window. Local disconnect is authoritative. This is not end-to-end encrypted messaging.
5. **Infrastructure controls:** before public deployment, verify the `discord-production` environment has default-branch restrictions and required reviewers, least-privilege Cloudflare credentials, protected workflow/source changes, MFA, usage alerts and incident-response owners. These are account settings and were not inspected or changed. A repository workflow alone cannot enforce its own integrity against a repository administrator.
6. **Live acceptance:** exercise two independent Discord accounts and two PCs, both user-install and guild/DM contexts, denied permissions, screenshot upload, Epic approval completion/cancellation, connection loss, local disconnect, Discord unlink and re-pair. Check the real three-second Discord acknowledgement deadline and response delivery. Keep the deployment private until these pass. Local emulation does not prove production permissions, networking or platform behavior.
7. **Migration and rotation:** update Worker and app together, unlink protocol-v1 bearer pairings and re-pair with pinned identity. Unlink and re-pair after suspected compromise. Routine app updates and restarts do not require re-pairing. Rotate Cloudflare/Discord deployment tokens in the protected environment. No deployed secrets were available to inspect or rotate during this audit.
8. **Availability and privacy:** edge limits are approximate and shared behind NAT; distributed abuse/billing exhaustion remains possible. Public domains, provider metadata, Discord attachment handling, existing Git history and external backups may reveal information. This audit did not inspect every historical Git object, provider logs, backups or private infrastructure.

## References

- [Discord interaction signatures and responses](https://github.com/discord/discord-api-docs/blob/main/developers/interactions/receiving-and-responding.mdx)
- [Windows CNG key export policy](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cngkeycreationparameters.exportpolicy?view=net-10.0)
- [Cloudflare Web Crypto support](https://developers.cloudflare.com/workers/runtime-apis/web-crypto/)
- [GitHub Actions SHA pinning](https://github.blog/changelog/2025-08-15-github-actions-policy-now-supports-blocking-and-sha-pinning-actions/)
- [GitHub CLI credential-helper setup](https://cli.github.com/manual/gh_auth_setup-git)
- [Bouncy Castle .NET package](https://www.nuget.org/packages/bouncycastle.cryptography/)
