# ADR 0004: Capped Buddy proxy access is the release default

- Status: accepted and implemented
- Date: 2026-08-10
- Supersedes: ADR 0003 only for default-provider selection

## Context

Local Qwen provides the strongest privacy boundary, but its approximately
21.5 GB setup and mixed CPU/GPU inference are a substantial first-run cost.
Direct DeepSeek is fast and needs no local language-model download, but requiring
every user to create and enter a provider account makes first use awkward. The
owner wants to offer a deliberately limited amount of DeepSeek access without
putting the unrestricted DeepSeek credential in the desktop application.

The proxy must be deployable through the existing `rs` SSH target and must not
create or change anything outside `/root/buddy-proxy`.

## Decision

Published Buddy builds select `buddy-proxy` when no provider setting exists.
The picker remains explicit and also offers direct DeepSeek with the user's key
and local Qwen. Changing this key/value default introduces no data migration.

The proxy is an ASP.NET Core HTTPS service with these controls:

- client keys are friendly 12-letter uppercase codes in `ABCDEF-GHIJKL` form;
- only an HMAC-SHA-256 key digest is stored, using deployment-only pepper;
- client routes are rate-limited per source address before key validation;
- every client has independent reply and combined prompt/completion token
  limits; the release key stops at 1,000 replies or 1,000,000 tokens, whichever
  is reached first;
- non-streaming requests are serialized per client, output limits are clamped
  before forwarding, and actual DeepSeek usage is charged atomically;
- stable machine-readable errors distinguish invalid, disabled,
  reply-exhausted, and token-exhausted keys;
- only the two approved DeepSeek model identifiers are accepted;
- request text and response text are never written to the proxy database or
  application logs; the durable usage event contains ids, model, counts, and
  time only;
- the upstream DeepSeek key, key pepper, TLS private key, database, logs,
  executable, and process files all remain under `/root/buddy-proxy`.

The server's existing SQLite runtime is dynamically loaded; no operating-system
package is installed. The target-local Kestrel certificate is pinned by SHA-256
in release builds. The included client key is expected to be extractable from a
distributed executable, so it is treated as a capped access token rather than a
secret capable of exposing the unrestricted DeepSeek account.

Local Qwen setup is demand-driven. Selecting it starts resumable downloads with
visible progress and notifications, then verifies exact sizes and SHA-256 hashes
before activation. Whisper, Silero, and Kokoro use the same application-wide
setup status surface when first needed.

## Consequences

- A release user can try text improvement and dialog without creating an API
  account or downloading Qwen first.
- The default text actions are cloud actions. The UI, website, and privacy
  documentation must say that action text goes through Buddy to DeepSeek while
  microphone audio remains local.
- The release quota is a strict financial containment boundary, not a promise
  of permanently available service. Users can switch to their own DeepSeek key
  or local Qwen after it is exhausted.
- Rotating the target-local certificate requires publishing a build with a new
  pin. Rotating or disabling a client key does not expose or rotate the upstream
  credential.
- Target-local scripts provide start, stop, status, backup, and atomic deploy
  behavior. Automatic boot startup is intentionally absent because it would
  require writing outside the authorized deployment directory.
