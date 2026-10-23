# Discord application

The Discord application runs in a dedicated Cloudflare Worker, with a Durable Object for each Discord user. The Windows app makes outbound HTTPS requests to that Worker every three seconds while paired. No inbound port, public home address, tunnel, Gateway bot process, or extra server is required. Pilot must be open and the PC awake. One PC is paired per Discord user.

The Worker endpoint is public by design. Discord signatures and device credentials protect its operations. GitHub secrets protect deployment credentials; they do not authenticate end users. Users of the hosted app never need the Cloudflare token or Discord bot token.

## Deploy the official instance

1. Create a dedicated Discord application. Enable Guild Install and User Install, using the `applications.commands` scope. Message Content and other privileged intents are unnecessary. Use the application's install link to let users add it.
2. Choose a dedicated Cloudflare Worker name in `discord-worker/wrangler.jsonc`. Use an organization-owned Workers subdomain or custom domain without a personal name. A Worker address is public and is shown during pairing. Do not put account IDs, personal domains or tokens in tracked configuration.
3. Create a GitHub environment named `discord-production`. Restrict deployment branches to the default branch and configure required reviewers if available. Protect changes to the workflow and Worker source with repository rules and code review. Anyone who can modify production code can access runtime data.
4. Add these **environment secrets**, never source files:

| Secret | Value |
| --- | --- |
| `DISCORD_CLOUDFLARE_API_TOKEN` | Cloudflare Workers deployment token scoped to the deployment account |
| `DISCORD_CLOUDFLARE_ACCOUNT_ID` | Deployment account ID |
| `DISCORD_APPLICATION_ID` | Discord application ID |
| `DISCORD_PUBLIC_KEY` | Application verification public key |
| `DISCORD_BOT_TOKEN` | Bot token, used only during command registration |
| `DISCORD_WORKER_ORIGIN` | HTTPS origin, e.g. `https://endurance-pilot-discord.example.workers.dev`, without a trailing slash |

The public key and application ID are not cryptographic secrets. **Publish these two values in a trusted, reviewed setup guide alongside your official Worker origin**, so users can pin the Discord identity locally. Users must not accept a replacement verification key supplied by an untrusted Worker. The bot token is not installed in the Worker or Windows app. Consider a separate Cloudflare account for the application if other services share your existing account; account-scoped deployment tokens can affect other Workers in that account.

5. Run the **Discord Worker** GitHub Actions workflow manually from the default branch. It tests and bundles the Worker, deploys code and runtime configuration together, then registers `/pilot`. Pull requests and pushes run checks without deployment secrets. No `pull_request_target` workflow is used. Command registration replaces global commands for this dedicated Discord application.
6. Set the Discord application's Interactions Endpoint URL to `https://YOUR-WORKER/interactions`. Discord verifies its signed PING handshake. Install the app and run `/pilot pair`.

The workflow is separate from Windows releases. It pins GitHub actions to commit SHAs, installs locked npm dependencies without lifecycle scripts, checks dependency advisories and exposes credentials only to their deployment or registration step. Forks can operate their own Discord application and Worker by supplying their own secrets. No maintainer credentials are shipped in a release. Keep `.dev.vars`, `.env` files and Wrangler local state out of Git; these are ignored. The Worker accepts only its configured HTTPS origin, so the Discord endpoint, desktop origin and `DISCORD_WORKER_ORIGIN` must match exactly.

## Pair and use

1. Run `/pilot pair`. Its private reply contains the Worker origin, your Discord user ID and a random code valid for five minutes.
2. On your own PC, open Pilot's **Epic account menu → Discord remote control**. Enter those three values, plus the **Discord application ID and public verification key** from the trusted publisher's guide (or your own Discord Developer Portal). Verify the Worker address and pinned Discord identity independently of the Worker.
3. Choose whether to allow game/account actions and game screenshots. Both permissions default to off. Pairing grants progress, statistics and account display-name access.
4. Select **Pair this PC**. Pairing survives restarting Pilot and expires after 90 days. To change permissions or renew access, disconnect and pair again. Keep the PC clock synchronized; device requests allow 60 seconds of skew, and desktop commands have a stricter 90-second proof lifetime with at most five seconds of future skew.

| Command | Behavior |
| --- | --- |
| `/pilot progress` | Running/stopped state, attention flag, wave and completed session runs |
| `/pilot statistics` | Session/lifetime completed runs and lifetime reward totals |
| `/pilot accounts` | Saved account numbers and display names, selected account, sign-in status |
| `/pilot switch account:2` | Select account 2 for the next launch; runner must be stopped and Fortnite closed |
| `/pilot login` | Get an Epic website approval link; account credentials are acquired and encrypted on the PC |
| `/pilot screenshot` | Capture the Fortnite window using Windows Graphics Capture; requires screenshot permission |
| `/pilot menu` | Show the already-running Pilot window on the PC |
| `/pilot start` | Start endurance with the saved settings |
| `/pilot stop` | Stop automation, leaving Fortnite open |
| `/pilot launch` | Launch Fortnite with the selected account, if it is not already running |
| `/pilot unlink` | Revoke cloud device access and discard queued work |

The desktop **Disconnect and revoke** button disables the connection and deletes its Windows signing key and saved configuration, even when the cloud revoke request fails. If the network is down, also use `/pilot unlink` to clear the cloud pairing. Commands already executing cannot be undone by revocation. Cloud revocation is observed on the next poll; pending sign-in is then cancelled. Loss of connectivity also cancels pending remote sign-in. `/pilot login` cannot add an account after local disconnection or after the client has detected cloud revocation.

The security-audit changes introduce device protocol v2. **Old bearer-token pairings are intentionally unsupported.** Update the Worker and desktop together, run `/pilot unlink`, and pair again with the application ID and public key. If unlink and pair are sent within the same second, wait a second and resend pair: requests predating revocation are rejected. A failed or interrupted pairing can leave an unusable cloud reservation; unlink it before retrying.

Epic passwords, authorization codes, device IDs and device secrets are never entered into Discord. The approval URL is sensitive until it expires: approve only your own pairing and never forward that link. `/pilot accounts` reports sign-in completion. Remote account switching does not forcibly close Fortnite or terminate a live mission.

## Security and privacy boundaries

- Every Discord request requires a valid Ed25519 signature, matching application ID and timestamp within five minutes at the Worker. Before executing, the desktop independently verifies the original signed bytes against the **locally pinned Discord public key**, application ID, invoking user, command ID, action and arguments. Proofs must be newer than pairing and no more than 90 seconds old. The Worker cannot invent another user's commands without Discord's signing key or replacing the trusted desktop/configuration.
- Each Durable Object serializes pairing, command delivery and revocation. Pairing codes contain 128 random bits, expire after five minutes and are consumed once. Guesses are rate-limited at the edge; they do not consume a victim's enrollment allowance. Existing pairings cannot be overwritten without unlinking. Signed unlink remains available when the user's command quota is full.
- Each PC creates a P-256 signing key using Windows CNG with **private-key export disabled**. No shared secret or reusable device bearer token is transmitted. The Worker stores only the public key. Each device request signs its method, origin, user, route, exact body hash, timestamp and fresh 128-bit nonce; replay nonces are retained through the full acceptance window. Pairings expire after 90 days. Configuration and the replay journal use Windows DPAPI. This uses the Windows software key provider, not a guaranteed TPM: another process running as that Windows user may still ask Windows to sign requests.
- The desktop sends no inbound listener, shell command, arbitrary executable, file read, URL fetch or desktop capture capability. Requests to the configured Worker require HTTPS and never follow redirects. The desktop enforces its permission switches independently of Discord.
- Commands require a recent device poll, have a 45-second deadline and are delivered at most once. There is one pending command per user and a three-second command cooldown. Signed interaction IDs are retained for ten minutes at the Worker and written to an encrypted desktop replay journal **before execution**, including across restarts. Corrupt/unwritable journals fail closed. A file lock prevents two Pilot processes from sharing one connection. Lost command delivery is deliberately not retried; check the game before repeating an uncertain action.
- Public device endpoints have an edge rate limit of 120 requests/minute per source IP per Cloudflare location. This is approximate, shared by PCs behind one NAT, and complements per-user checks. It does not eliminate distributed abuse or billing risk. Configure Cloudflare usage alerts and appropriate account limits for a public deployment. The rate-limit namespace number should be unique within the deployment account.
- Replies are ephemeral, and mentions are disabled. Ephemeral messages and attachments are still handled by Discord. Screenshots can reveal in-game names, chat and overlays. The capture API targets Fortnite only and never falls back to the desktop.
- The Worker stores the public device key and owner ID, pairing hash until expiry, short-lived signed interaction (including Discord metadata and reply token), command arguments and rate/replay state. The interaction is discarded on completion or the 45-second timeout. It does not persist screenshots or result content. Discord still stores delivered messages/attachments under its own policies. Unlink removes the pairing and pending work; replay IDs expire within ten minutes. A revocation cutoff timestamp remains to reject pre-revocation requests.
- Application request logging is disabled. The code does not log secrets, IPs, usernames, paths or payloads. Cloudflare and Discord necessarily process connection metadata. Operators can enable platform logs or change the code; hosting is not end-to-end encrypted. The repository cannot guarantee operator anonymity or hide identifiers already present in Git history, registration records, domains or billing accounts.
- A compromised Discord account can issue legitimately signed commands to its own paired PC within locally enabled permissions. A compromised Worker can see replies, withhold or delay genuine commands within their validity window, deny service, alter replies and suppress cloud revocation. It cannot mint a valid Discord proof for a different user when the desktop's pinned identity remains trusted. **Local disconnect remains the reliable way to stop a compromised Worker.** These protections do not provide end-to-end confidentiality from Cloudflare or Discord.
- Epic refresh/device credentials remain encrypted at rest on the PC and necessarily exist in memory during authentication. Fortnite's existing launch protocol passes a short-lived exchange code on its process command line; local processes with sufficient access may read it. Public Epic OAuth client constants already in the open-source implementation are not maintainer deployment secrets. No architecture can promise that a fully compromised Windows account, administrator or compromised desktop release cannot steal/use credentials.

## Development and verification

Use Node 24. From `discord-worker`, run `npm ci --ignore-scripts`, `npm test`, `npm audit --audit-level=moderate`, and `npx wrangler deploy --dry-run`. Tests cover signature verification, cross-user isolation, pairing expiry and replay, signed-device body/audience binding, command expiry/deduplication, revocation, payload/time limits and a real local Cloudflare runtime with concurrent pairing and polling. A public-only Windows CNG signature fixture verifies interoperability with Web Crypto. The dry run does not deploy.

Build the desktop using `dotnet build EndurancePilot.sln`. A live acceptance check still requires a deployed Discord application and a Windows PC running Fortnite: pair with controls disabled, check progress, confirm screenshot/control denial, re-pair with permission enabled, then exercise screenshot, sign-in, account selection, menu and runner commands. Verify both Discord unlink and desktop disconnect prevent later commands. Test using two Discord users and two PCs before opening a public instance.

Desktop self-checks include signature tampering, owner/application/action/argument binding, expiry, persistent replay protection, CNG export refusal, request signatures, exclusive connection ownership and local key deletion. `Publish-SingleExe.ps1` runs them against the isolated packaged executable and checks its contents for unexpected files and personal build paths. See [the security audit](discord-security-audit.md) for findings, fixes and deployment checks that cannot be verified from source alone.

References: [Discord HTTP interactions](https://github.com/discord/discord-api-docs/blob/main/developers/interactions/receiving-and-responding.mdx), [Cloudflare GitHub Actions deployment](https://developers.cloudflare.com/workers/ci-cd/external-cicd/github-actions/), [Worker secrets](https://developers.cloudflare.com/workers/configuration/secrets/), [Worker rate limits](https://developers.cloudflare.com/workers/runtime-apis/bindings/rate-limit/).
