# Security policy

## Reporting a vulnerability

Please do not publish credentials, private recordings, transcripts, or an
exploitable security issue in a public GitHub issue. Use the repository's
**Security** tab to open a private vulnerability report. Include the affected
Buddy version, the smallest safe reproduction, and whether the issue concerns
the Windows app, local model runner, installer, website, or hosted proxy.

Ordinary bugs and feature requests can use the public issue tracker as long as
the report contains no private data.

## Supported version

Security fixes target the latest published release. Early Buddy binaries are
not code-signed; compare both release assets with `SHA256SUMS.txt` before use.

## Credential and privacy boundaries

- Microphone audio, Whisper recognition, pronunciation analysis, and Kokoro
  synthesis run locally.
- Hosted language providers receive only the text required for the requested
  action. The default release provider is capped Buddy access to DeepSeek;
  direct DeepSeek and local Qwen are explicit alternatives.
- The distributed Buddy proxy key is an intentionally extractable, capped
  access token. It cannot reveal the unrestricted upstream DeepSeek key.
- User-entered provider keys use Windows Current User data protection and are
  excluded from diagnostics.
- Proxy production settings, TLS private material, the usage database, and logs
  are deployment-only files and are excluded from source control.
