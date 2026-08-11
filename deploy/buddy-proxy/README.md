# Buddy DeepSeek proxy deployment

The production deployment is deliberately confined to `/root/buddy-proxy`.
The executable, TLS certificate, settings, SQLite quota database, PID, logs,
scripts, staging folders, and release backups all live below that directory.
No systemd, nginx, Caddy, firewall, cron, or other host-wide configuration is
created or changed.

The service listens on HTTPS port `38472`. `start.sh`, `stop.sh`, and
`status.sh` validate their own resolved directory before operating. Since the
scope forbids files outside the deployment directory, the process is not
automatically restarted after a server reboot; run `./start.sh` from the target
directory when the host restarts.

`appsettings.Production.json` and `private/` are deployment secrets and are
never committed. Client keys are stored only as HMAC-SHA-256 hashes in SQLite.
Create a client key on the server with:

```bash
cd /root/buddy-proxy
ASPNETCORE_ENVIRONMENT=Production ./buddy-proxy admin create \
  --name buddy-release --reply-limit 1000 --token-limit 1000000
```

The plaintext 12-letter code (for example, `ABCDEF-GHIJKL`) is printed once.
Key status and usage can be inspected
without revealing it:

```bash
ASPNETCORE_ENVIRONMENT=Production ./buddy-proxy admin list
```

Disable or re-enable a client by the numerical id shown by `admin list`:

```bash
ASPNETCORE_ENVIRONMENT=Production ./buddy-proxy admin disable --id 2
ASPNETCORE_ENVIRONMENT=Production ./buddy-proxy admin enable --id 2
```

## Client API

All client routes except `/healthz` require a bearer code such as
`Authorization: Bearer ABCDEF-GHIJKL`.

- `GET /v1/models` returns the approved DeepSeek model list and quota snapshot.
- `GET /v1/quota` returns the current reply, prompt-token, completion-token,
  and total-token counters.
- `POST /v1/chat/completions` accepts the non-streaming DeepSeek chat-completion
  shape. `/chat/completions` is an equivalent compatibility route.

Successful requests return `X-Buddy-Quota-Replies-*` and
`X-Buddy-Quota-Tokens-*` headers. Errors use an OpenAI-compatible `error`
object with `type: buddy_proxy_error` and a stable `code`:

| HTTP | Code | Meaning |
| --- | --- | --- |
| 401 | `proxy_key_invalid` | Missing, malformed, or unknown client key |
| 403 | `proxy_key_disabled` | Administratively disabled key |
| 429 | `proxy_reply_quota_exhausted` | Reply limit reached |
| 429 | `proxy_token_quota_exhausted` | Not enough token allowance remains |
| 429 | `proxy_rate_limited` | The source address exceeded the request-rate limit |
| 422 | `proxy_streaming_unsupported` | Streaming is rejected for atomic billing |
| 422 | `proxy_model_unavailable` | Model is not on the deployment allowlist |

The proxy forwards request text only in memory. SQLite contains hashed client
keys and numerical usage records; normal ASP.NET logging records no request or
response bodies.

## Deploy and operate

From the Windows repository root:

```powershell
.\scripts\deploy-proxy.ps1
```

The deploy script validates that the remote root resolves exactly to
`/root/buddy-proxy`, publishes one self-contained Linux executable, stages it
inside that root, makes a target-local backup, atomically activates the new
files, and requires the HTTPS health check to pass. On the server:

```bash
cd /root/buddy-proxy
./status.sh
./stop.sh
./start.sh
```
