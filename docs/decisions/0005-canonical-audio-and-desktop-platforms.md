# ADR 0005: Make compact audio canonical and add tiered desktop hosts

Date: 2026-08-11
Status: Accepted

## Context

Buddy already preserves microphone capture as `original.opus` and creates a
second `compact.opus` after local voice-activity detection. Playback prefers the
compact artifact, but the recordings screen still presents captured wall time
as the main duration and transcription is normally queued in the background.
That makes pause removal look incomplete and gives the user no explicit way to
request or retry recognition for an older recording.

The established application is a .NET MAUI WinUI app. MAUI supports macOS
through Mac Catalyst, but it has no supported Linux target. Microsoft now ships
an experimental GTK4 MAUI backend from `dotnet/maui-labs`; treating that backend
as production-ready would overstate its maturity.

## Decision

### Canonical recording

- `compact.opus` is the canonical listening, timeline, waveform, transcription,
  and future export artifact whenever it exists.
- Long silent gaps are removed. Short conversational pauses remain natural;
  larger gaps collapse to a 200 ms transition with 180 ms speech padding and
  short fades at edit boundaries.
- `original.opus` remains an immutable recovery source. It is not the default
  playback artifact and is never silently deleted.
- The primary duration shown in the library is the canonical compact duration.
  Captured duration is shown separately when it differs materially.
- The artifact-selection rule lives in `Buddy.Core` so playback, waveform,
  recognition, UI, and future export cannot drift apart.

### On-demand smart transcription

- Every recording card exposes its transcript, not only trainer and dialog
  recordings.
- A user can explicitly request recognition or re-recognition. The action checks
  and, with visible progress, installs the local Whisper/VAD files if required.
- Whisper receives the compact artifact, automatic language detection for
  general recordings, word timestamps where pronunciation analysis needs them,
  and the selected dialog-language hint for monologues.
- Recognition and user edits are separate immutable transcript revisions. A
  retry never destroys an earlier recognized or edited revision.
- The current transcript is editable and copyable. Saving creates a
  `UserEdited` revision and does not rewrite recognition history.
- AI titles continue to be derived from the current transcript after a manual
  recognition run.

### Desktop platform tiers

| Platform | Host | Audio | Release tier |
| --- | --- | --- | --- |
| Windows 10/11 x64 | MAUI WinUI | NAudio WASAPI shared mode | Stable |
| macOS 13+ Apple Silicon | MAUI Mac Catalyst | MiniAudioEx / Core Audio | Beta until device QA completes |
| Ubuntu 24.04+ x64 | experimental MAUI GTK4 head | MiniAudioEx / PulseAudio or ALSA | Preview |

The Windows host remains isolated from preview platform dependencies. Shared
domain, persistence, speech, language, view-model, and MAUI UI code is reused by
the macOS and Linux heads. Platform-specific implementations provide capture,
playback, secrets, tray/window behavior, file picking, and UI dispatch.

The portable audio backend is pinned to MiniAudioEx 3.3.5, which bundles
miniaudio 0.11.25 runtimes for Windows, Linux, macOS, and supported CPU
architectures. Buddy continues to encode archives itself with Concentus so the
on-disk Opus format and compact-timeline semantics stay identical on every OS.

Linux packages declare GTK 4.12 as a runtime prerequisite. The website and
release notes must call Linux a preview until real microphone, speaker, tray,
window, and installer smoke tests pass on representative distributions.

## Rejected alternatives

### Claiming official MAUI Linux support

Rejected because no supported Linux target framework exists in MAUI. The GTK4
backend is explicitly early and experimental.

### Moving the whole UI to another framework in this release

Rejected because it would replace a large, interaction-tested MAUI surface and
risk regressions in dialog scrolling, word help, onboarding, tray behavior, and
audio controls. A separate host is a smaller and reversible boundary.

### Destructively replacing source audio with the pause-cut derivative

Rejected because VAD can make a bad cut. Keeping the immutable source permits a
future retry with improved detection without making routine playback noisy.

### Cloud-only transcription

Rejected as the default because meeting audio is private and potentially long.
Local Whisper remains the normal path; cloud recognition can remain an explicit
future retry provider.

## Acceptance

1. A recording with a long silent interval plays the compact artifact and shows
   compact duration as its main duration.
2. Waveform seeking and manual transcription use that same artifact.
3. Every recording card can request, display, edit, save, and copy a transcript.
4. Re-recognition preserves the prior revisions.
5. Windows release acceptance remains green and the default single-file publish
   path remains `C:\Programs`.
6. macOS and Linux heads restore and compile on native CI runners.
7. Platform download links are published only after their artifacts exist; the
   site labels beta/preview status and Linux prerequisites plainly.

## References

- .NET MAUI supported platforms:
  <https://learn.microsoft.com/dotnet/maui/supported-platforms>
- Experimental MAUI GTK4 backend:
  <https://github.com/dotnet/maui-labs/tree/main/platforms/Linux.Gtk4>
- MiniAudioEx.NET:
  <https://github.com/japajoe/MiniAudioExNET>
- miniaudio:
  <https://miniaud.io/>
