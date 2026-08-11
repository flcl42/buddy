# Buddy technical architecture

This document translates the product specification into an implementation
boundary for a Windows-first .NET MAUI application.

## 1. Selected architecture

Buddy uses a staged, local-first pipeline:

```text
                         ┌──── durable source archive ────┐
Microphone ─> chunk writer ─> Silero VAD ─> compact audio │
                 │                    └─> Whisper transcript
                 │                              │
                 │             selected provider correction/title
                 │                              │
                 └─────────────────> Kokoro generated speech
                 │
                 └─ two-second Dialog checkpoints
                         └─> rolling VAD + Whisper
                                  └─> persistent messages
                                           └─ complete history ─> Buddy proxy (default)
                                                                  direct DeepSeek
                                                                  or local Qwen
                                                                   │
                                               local answer WAV <─ Kokoro
```

Audio capture, recognition, pronunciation analysis, and synthesis are local.
Language-provider interfaces keep the default capped proxy, direct cloud use,
and local Qwen separate from recording and persistence.

## 2. Repository layout

The initial solution should use explicit platform and domain boundaries:

```text
Buddy.slnx
src/
  Buddy.App/                 MAUI XAML, navigation, view models, composition
  Buddy.Core/                domain records, use cases, interfaces, state machines
  Buddy.Audio.Windows/       WASAPI capture/playback and Windows device handling
  Buddy.Speech/              VAD, Whisper, Kokoro, model management
  Buddy.Language/            correction/title contracts and provider adapters
  Buddy.Persistence/         SQLite, current-schema reset, file catalog, recovery journal
tests/
  Buddy.Core.Tests/
  Buddy.Persistence.Tests/
  Buddy.Language.Tests/
  Buddy.IntegrationTests/
tools/
  Buddy.AudioFixtures/       deterministic fixture and fault-injection utilities
```

Target `net10.0-windows10.0.19041.0` for application projects. Keep Core,
Language contracts, and most persistence code on plain `net10.0` so the domain
can be tested without MAUI.

## 3. Main dependencies

| Area | Selection | Reason |
| --- | --- | --- |
| UI | .NET MAUI / WinUI 3 | Requested stack and native Windows integration |
| View models | CommunityToolkit.Mvvm | Small, testable MVVM primitives |
| Tray | H.NotifyIcon.Maui | WinUI/MAUI tray support, double-click, generated icons |
| Audio I/O | NAudio | Mature Windows WASAPI capture and playback |
| Archive codec | Concentus + Ogg container | Managed Opus without an ffmpeg install |
| Recognition | Whisper.net | In-process whisper.cpp bindings with CUDA and VAD support |
| Local VAD | Silero VAD | Fast speech segmentation and streaming use |
| Local voice | KokoroSharp + MisakiSharp | Native .NET/ONNX English pipeline without Python |
| Markdown | Markdig | One parsed representation for native rendering and speech-safe text |
| Metadata | Microsoft.Data.Sqlite | One explicit current schema with reset-on-mismatch behavior |
| Resilience | Polly | Bounded retry/timeout policies for provider requests only |
| Logging | Microsoft.Extensions.Logging | Structured logs with a strict redaction boundary |
| Tests | xUnit + FluentAssertions | Unit and integration test foundation |

Pin exact package versions centrally after the first compile-and-license audit.
Do not use floating versions.

## 4. Runtime components

### 4.1 Application host

The MAUI process owns:

- a single-instance coordinator using Windows App SDK `AppInstance`;
- a tray controller independent of window lifetime;
- a durable background-job queue;
- an audio device service;
- a persistent dialog coordinator with a serialized live-analysis queue;
- an explicit shutdown coordinator;
- lazily loaded speech models.

No recording or model inference is owned by a Page or ViewModel. Hiding or
recreating a window must not stop active work.

### 4.2 Recording session state machine

```text
Created
  -> Capturing
  -> FinalizingSource
  -> ReadyForPlayback
  -> DetectingSpeech
  -> BuildingCompactAudio
  -> Transcribing
  -> Titling
  -> Ready

Any processing state -> NeedsAttention -> retry the failed stage
Capturing -> Interrupted -> Recovering -> FinalizingSource
```

Each transition is persisted before side effects begin. Jobs are idempotent:
restarting a completed stage does not create a second recording or replace an
edited transcript.

### 4.3 Audio capture

Use WASAPI shared mode through NAudio so Teams, Zoom, or a browser can use the
same input. Normalize into a stable internal format, initially 48 kHz mono PCM,
before writing fixed-duration chunks.

Capture callbacks only copy audio into a bounded channel. File I/O, metering,
VAD, and UI updates occur downstream. If a consumer falls behind, durable audio
has priority over waveform or live VAD work.

Required events:

- device removed;
- default device changed;
- capture discontinuity;
- suspend/resume;
- disk-space threshold crossed;
- controlled stop;
- unexpected process termination, recovered from journal.

### 4.4 Archive and silence compaction

The validated original archive is Ogg Opus. Opus is appropriate for speech,
small enough for long meetings, and avoids external ffmpeg deployment.

The compact derivative is built from VAD segments with:

- configurable speech threshold;
- pre-roll and post-roll padding;
- minimum speech duration;
- minimum silence duration;
- a short replacement gap for long pauses;
- short crossfades at splice boundaries.

A segment table maps compact offsets to original offsets:

```text
CompactSegment(
    CompactStartMs,
    CompactEndMs,
    OriginalStartMs,
    OriginalEndMs,
    Confidence)
```

This supports correct seeking, transcript highlighting, and exports without
destroying temporal provenance.

### 4.5 Recognition

Default model: unquantized Whisper `large-v3-turbo`.

Reasons:

- the installed RTX 5070 Ti has enough VRAM;
- the model is much faster than full large-v3 while retaining high accuracy;
- Whisper.net keeps inference in the .NET process and supports CUDA with
  CPU/Vulkan fallback;
- recognition remains private and usable offline.

Implementation requirements:

- model downloads are resumable and checksum-verified;
- disk space is checked before download and extraction;
- the model version and hash are stored with transcript revisions;
- a language can be pinned or detected;
- timestamps are preserved per segment;
- Trainer passes enable Whisper token timestamps and probabilities, merge
  subword pieces into readable words, and preserve word start/end times;
- word-confidence thresholds are versioned and displayed as an intelligibility
  aid, with an explicit warning that they are not phoneme-level scoring;
- a vocabulary glossary is injected where the backend supports prompting;
- CUDA startup is health-checked and falls back with a visible diagnostic;
- a cloud retry never replaces a user-edited transcript automatically.

AI Dialog uses the same model in a rolling-batch mode. Each completed
two-second capture chunk is copied from the durable capture journal into an
isolated work directory. The current utterance is rebuilt as mono 16 kHz PCM,
then VAD and Whisper reprocess that utterance. The UI replaces the previous
partial result; it does not append overlapping hypotheses. This is bounded
near-live recognition, not token-level streaming.

OpenAI `gpt-4o-transcribe` can be offered as an explicit per-recording retry for
hard audio. It is not the default meeting pipeline.

### 4.6 Language correction and titles

Define a small provider-neutral contract rather than coupling the domain to one
OpenAI-compatible JSON shape:

```csharp
public interface ILanguageImprovementProvider
{
    Task<ImprovementResult> ImproveAsync(
        ImprovementRequest request,
        CancellationToken cancellationToken);

    Task<TitleResult> CreateTitleAsync(
        TitleRequest request,
        CancellationToken cancellationToken);
}
```

`ImprovementRequest` includes:

- source transcript;
- mode;
- locale;
- tone preference;
- protected glossary terms;
- optional prior user edits;
- strict maximum output size.

`ImprovementResult` includes:

- minimally corrected text;
- optional polished text;
- structured change explanations;
- ambiguities with alternatives;
- protected-term violations;
- provider/model/version and usage metadata.

The provider must return schema-valid structured data. Invalid or truncated
responses do not pass through as corrected prose. The original transcript is
delimited as untrusted data and cannot change system behavior.

Selected provider order:

1. Buddy's capped DeepSeek proxy, selected by default in release builds and
   limited per key by reply and combined input/output token counters.
2. Direct DeepSeek V4 Flash with thinking disabled and the user's own API key.
3. Qwen 3.6 27B Q4_K_M plus its matching DFlash Q8_0 speculative draft through
   a pinned llama.cpp CUDA 13.3 runtime, installed on demand for local use.
4. Kimi and OpenAI adapters remain disabled-until-validated capabilities.

The Qwen runtime is an app-owned loopback process, not a public service. It
binds only `127.0.0.1`, receives a fresh 256-bit bearer key for every Buddy
process, and is attached to a Windows kill-on-job-close object. A fixed partial
GPU offload keeps 24 target-model layers on CUDA while the DFlash draft remains
CPU-resident; this preserves target-model quality and reserves VRAM for Whisper.
Quantized KV cache and a 32,768-token context bound memory. The server remains warm for two
minutes so adjacent dialog turns can reuse the loaded model and prompt cache,
then sleeps after idle time; Settings can warm or unload it explicitly. The GGUF and runtime live
outside the single-file application under `H:\BuddyAI` when that drive is
available, with `BUDDY_AI_ROOT` as an explicit override and a per-user fallback
on other PCs. Recordings and speech models similarly prefer `H:\Buddy` on this
machine and can be redirected with `BUDDY_DATA_ROOT`.

The Buddy proxy is a separate ASP.NET Core service. Its SQLite catalog stores
hashed client keys, key state, limits, aggregate prompt/completion counters, and
usage events without prompt or answer bodies. A per-client async lock closes the
concurrent overspend race: remaining quota is reread, a conservative input
reservation is made, DeepSeek output is clamped, actual non-streaming usage is
recorded in one transaction, and updated counters are returned as response
headers. Stable `buddy_proxy_error` codes let the desktop distinguish an invalid
or disabled key from reply/token exhaustion and upstream availability. The
release endpoint uses a target-local certificate whose SHA-256 fingerprint is
pinned in the client. Friendly codes use 12 random uppercase letters with a
hyphen after the sixth character; per-source request limiting constrains online
guessing attempts before authentication.

The live synthetic comparison documented in
`research/language-model-evaluation.md` found Flash substantially faster and Pro
more careful/natural. Both could make an unsafe guess around ambiguous wording,
confirming that ambiguity output and editable versioning are product
requirements, not optional polish.

AI titles are asynchronous. A deterministic fallback such as
`Meeting · 30 Jul · 14:05` is always present. Title failure never blocks Ready
state or playback.

### 4.7 Persistent AI Dialog

The dialog coordinator owns one session-level recording plus a serialized
channel of completed capture chunks, flush requests, answer retries, and replay
resets. Page and ViewModel lifetimes do not own the microphone.

Turn completion is deterministic:

- one persisted allowed-pause preset from 0.75 to 15 seconds;
- no hidden shorter punctuation threshold;
- 45 seconds maximum per unreset safety window;
- repeatable `Keep talking` resets that move the detector's actual audio-time
  countdown origin and renew that safety window;
- explicit `Send now` override.

The detector returns elapsed and required silence with normalized progress, not
only a complete/continue flag. The snapshot includes that measured state and
its observation time. A ViewModel-local 100 ms interpolation makes the right-
panel progress line smooth without publishing high-frequency dialog snapshots
or reconciling the message collection. It reaches the threshold visually while
the serialized audio worker confirms the boundary on its next durable chunk.
Reset advances the detector origin using the latest analyzed audio position plus
capture time elapsed since that analysis, so repeated clicks genuinely postpone
commit even while analysis is catching up.

SQLite is conversational memory. A user message is committed before calling
the provider, and an assistant message is committed before local synthesis.
Every provider request is rebuilt from the session system instruction and all
stored user/assistant messages in sequence. Buddy does not use a sliding window
or silently summarize prior messages. A provider-size failure remains visible
and retryable.

Every selected provider's dialog output uses the versioned
`buddy.conversation-answer.v1` JSON contract. Each result contains
`display_markdown` plus `spoken_text`: both must carry the same facts,
qualifications, examples, and order, while the latter rewrites visual
structures, symbols, abbreviations, URLs, and code as natural plain speech.
The provider adapter appends this contract to every request, including requests
for a session created by an older build, and rejects a missing, empty, oversized,
or unversioned representation.

Only `display_markdown` is committed as the immutable assistant message and
resent as conversation history. A versioned answer document containing both
representations is atomically saved beside that message's generated audio, so
future synthesis refreshes retain the original narration without a database
schema change. Markdig converts the display value once into a neutral block/run
document used by a Windows MAUI handler backed by one selectable WinUI
`RichTextBlock`; raw HTML is never executed. Styled headings, lists, quotes,
code, and inline runs therefore remain one continuous mouse-selectable text
surface with native `Copy` and `Select All`. A selection-scoped WinUI keyboard
accelerator also routes `Ctrl+C` to the one message that currently owns a
selection. User turns pass through the same renderer without treating their
transcript as executable content. Thinking is disabled,
output length is bounded, and audio is never included in the request. Before a
local Qwen dialog request, Buddy asks llama.cpp to tokenize the complete history
and rejects an oversized session; context shifting remains disabled, so no
older turn can be silently discarded.
Each successful answer may create a `DialogAssistant` WAV artifact through
Kokoro from `spoken_text` and play it automatically, while retaining manual
replay. Older messages without an answer document use the existing safe
Markdown-to-speech normalization. Thinking,
synthesis, playback, and a short feedback guard reject live checkpoints; a
capture-sequence watermark also discards work that was already queued before
playback began.

Word lookup is command-driven by hit-testing a normal tap against the rendered
WinUI `Run` on either role; the runs are deliberately not hyperlinks, so dragging
can remain owned by native text selection. Building or recycling a message view
performs no lookup. After a click, eSpeak supplies IPA locally while a separate
schema-validated selected-provider request receives only the selected word,
locale, and that one user or assistant turn as context.
Both operations can complete independently, are cancelled when selection
changes or the guide closes, and a completed result is cached by normalized
word on the stable message view model. The guide's explicit play control uses a
separate local, content-addressed Kokoro cache and never triggers a provider
request.

The All recordings reader resolves a dialog through the session's unique
`recording_id`, then loads immutable messages and durable pronunciation rows in
sequence. It rebuilds the same neutral Markdown documents and stable message
view models used by the live conversation, but keeps a separate collection so
opening an archive cannot replace active-session context or disturb live
autoscrolling. Each archived message retains its recording identifier for safe
assistant-audio lookup. Word IPA and definitions remain lazy and reuse the same
cancelled, per-message interaction path; no provider request is made merely by
opening or scrolling a saved dialog.

Playback uses Windows WASAPI shared mode on the selected active render endpoint.
Settings lists the current multimedia default plus every active speaker, saves
an optional explicit route, and provides a short two-note test. The dialog
status names the endpoint used for each automatically spoken answer, while
diagnostics record artifact, duration, route, start, stop, and categorized
failure metadata without transcript text or full private paths.
Dialog playback ownership distinguishes automatic/manual answers, synthesized
user turns, and selected words. All three render through one reusable segmented
transport presentation: play while idle, stop plus pause while active, and
resume plus restart while paused. Pause retains both the loaded source and its
owner; restart seeks and resumes atomically so recognition never observes a
false completed-playback transition. Stop rebuilds the output at position zero
without resuming it, and starting another owner replaces the current source.
Playing and paused owners both hold the same recognition gate, so locally
generated user or word speech cannot become a new microphone turn.

The final local Whisper pass requests word timestamps and confidence. The user
message and its pronunciation assessment are committed atomically before the
provider call, so a crash cannot leave conversational text without its matching
analysis. Each assessment stores an eSpeak-generated IPA guide plus timed word
confidence. Earlier saved turns can be backfilled with IPA even when their
original word timing was not retained.

Silent VAD checkpoints and empty Whisper results are presentation no-ops. The
ViewModel reconciles stable dialog-message identities in place instead of
clearing and rebuilding the collection, so status and playback events cannot
reset the user's conversation scroll position. The conversation view uses the
native Windows scroll extent to follow appended turns only when it is genuinely
at the bottom; a user scroll upward cancels even an already queued follow.
Tail-item height changes from delayed pronunciation or answer-audio metadata
participate in the same follow decision. A single animated move is followed by
non-animated convergence checks while virtualization is still changing the
native extent. When following is suspended, an overlaid `Latest` / `New reply`
control restores it explicitly without moving the reader first.

Finishing first moves the session to `Completing`, stops and archives the
microphone, places a queue barrier after the final partial chunk, persists a
`Conversation` transcript revision, and then marks the session complete. A
processing failure still stops capture safely; the completing session and raw
recording remain recoverable on restart.

### 4.8 Local speech synthesis

Default: KokoroSharp with MisakiSharp for an English-first release.

Requirements:

- selectable voice and speed;
- streamed first audio where practical;
- generated audio cached by a hash of normalized text, voice, speed, and
  synthesis/text-normalization versions, with a bounded 512 MiB local cache;
- technical pronunciation overrides;
- unambiguous spoken expansions for common English negative contractions so
  native-tokenizer reductions cannot turn an apostrophe suffix into a separate
  syllable and inline phoneme overrides cannot displace neighboring words; the
  displayed and persisted text remains unchanged;
- Markdown syntax, formatting-only characters, and hidden link destinations
  removed before synthesis while visible words, punctuation, code text, and
  explicit phoneme literals remain;
- lossless sentence/clause chunking below Kokoro's safe sequence length, with a
  short PCM pause between chunks, so long quoted or list-heavy replies do not
  lose words late in one inference sequence;
- output normalized to avoid large loudness changes during A/B playback;
- model and voice licenses included in application notices;
- no Python or external eSpeak dependency in the English-only package.

Unknown technical terms are surfaced in a pronunciation preview rather than
silently accepted. A user glossary maps a written form to pronunciation.

Dialog answer artifacts record speech-normalization, synthesis, and, when
available, speaker-aware answer-contract versions in their existing generator
metadata. The corresponding `dialog-message-<id>.answer.json` document keeps
the formatted and spoken representations together. If an older artifact lacks the current
synthesis marker, the first manual replay regenerates that derived WAV at the
same safe path and updates duration, size, hash, generator, and timestamp in
place. The button exposes the preparation state, concurrent replay is disabled,
and subsequent replays do no synthesis. On-demand user-turn and selected-word
speech is content-addressed under `speech-cache/`, reused on later clicks, and
trimmed by least-recent access above its size bound. This changes neither the
persisted message text nor any microphone archive and requires no database
migration.

An OpenAI speech adapter can be added for users who prefer cloud voices. It is
optional and must display the data boundary.

### 4.9 Provider credentials

The app never accepts a developer-owned secret in source or packaging.

For personal use, provider keys are entered in Settings and stored with Windows
credential protection. Only a redacted suffix is shown. A validation call is
manual and reports unauthorized, quota, billing, or connectivity failures
separately.

For a future multi-user commercial distribution, API calls should move behind a
Buddy-controlled service with authentication, per-user quotas, abuse controls,
and secret rotation. That is deliberately outside the personal-app
architecture.

## 5. Persistence model

SQLite is the source of truth for metadata; the file store is the source of
truth for binary artifacts.

Buddy intentionally has no incremental database migrations. The persistence
project contains one current-schema declaration and one integer schema version.
Changing the shape means editing that declaration and incrementing the version.
At startup, a matching database is left untouched and a genuinely empty
database receives the current schema directly. Any populated version mismatch,
whether older, newer, or unversioned, starts a fresh current-schema state.

Before that reset, Buddy moves the prior database, recordings,
capture journals, and dialog scratch data into a timestamped directory under
`backups`. Downloaded speech models, diagnostic logs, and DPAPI-protected
provider secrets remain in place. A small reset journal distinguishes the
archive and create phases, allowing startup to resume safely after interruption.

Core tables:

### `Recording`

- `Id`
- `Kind` (`Meeting`, `Trainer`, `Dialog`)
- `CreatedUtc`
- `LocalOffsetMinutes`
- `CaptureStartedUtc`
- `CaptureEndedUtc`
- `WallDurationMs`
- `SpeechDurationMs`
- `InputDeviceId`
- `Status`
- `DisplayTitle`
- `GeneratedTitle`
- `DeletedUtc`
- optimistic concurrency version

### `AudioArtifact`

- `Id`
- `RecordingId`
- `Kind` (`Original`, `Compact`, `TrainerGenerated`, `DialogAssistant`)
- relative path
- format, sample rate, channels
- duration and byte length
- SHA-256
- model/voice metadata where generated

### `SpeechSegment`

- `RecordingId`
- original and compact start/end offsets
- VAD confidence

### `TranscriptRevision`

- `Id`
- `RecordingId`
- `ParentRevisionId`
- `Kind` (`Recognized`, `UserEdited`, `Corrected`, `Polished`,
  `Conversation`)
- text
- immutable created timestamp
- provider/model/prompt-schema version
- `IsCurrent`

### `PronunciationAssessment` and `PronunciationWord`

- one durable assessment per Trainer recording;
- the audio-derived transcript, IPA phonetic transcript, model, creation time,
  and scoring schema;
- contiguous words with start/end offsets and normalized confidence;
- derived average confidence, words per minute, review count, and likely-unclear
  count;
- cascade deletion with the parent recording.

### `DialogPronunciationAssessment` and `DialogPronunciationWord`

- one optional durable assessment per recognized user message;
- the recognized text and local IPA phonetic transcript;
- contiguous word timing/confidence for newly captured turns;
- a foreign key to the immutable dialog message, with cascade deletion.

### `AudioWaveform`

- one cached envelope per preferred playback artifact;
- 96 normalized sound-level samples and the artifact duration;
- generated from decoded WAV or Opus audio without changing the source;
- rebuildable optional metadata whose failure never makes playback unusable.

### `ImprovementRun`

- request settings;
- response status;
- structured changes and ambiguities;
- redacted provider metadata;
- token/latency measurements;
- no secret or raw HTTP headers.

### `BackgroundJob`

- stage, state, attempt count;
- next attempt time;
- lease and heartbeat;
- last safe error category.

### `AppSetting`

- non-secret settings only, schema-versioned.

### `DialogSession`

- one-to-one recording link;
- status (`Active`, `Completing`, `Completed`, `Interrupted`,
  `NeedsAttention`);
- start/end time and optimistic version;
- stable system instruction;
- provider/model metadata and safe last error;
- a filtered unique index allowing at most one active/completing session.

### `DialogMessage`

- session and monotonic sequence;
- role (`User`, `Assistant`) and immutable text;
- created timestamp;
- provider/model, latency, and token counts;
- optional foreign key to a local assistant audio artifact.

Paths in SQLite are relative to the application data root. Moving a library can
therefore be implemented atomically.

## 6. File layout

```text
BuddyData/
  buddy.db
  recordings/
    2026/
      07/
        <recording-id>/
          original.opus
          compact.opus
          dialog-message-<message-id>.answer.json
          dialog-answer-<sequence>-<artifact-id>.wav
          generated/
            <artifact-id>.wav
  models/
    manifests/
    whisper/
    kokoro/
  capture-journal/
    <session-id>/
      session.json
      000001.pcm
      000002.pcm
  dialog-work/
  speech-cache/
    <session-id>/
      chunk-000001.pcm
      .live-utterance.wav
  logs/
```

The user may relocate `recordings/`; the database, journals, and credentials
remain in the protected application data area unless a full portable mode is
designed later.

## 7. Concurrency and resource policy

- Exactly one microphone capture session at a time.
- Meeting, Trainer, and Dialog capture are mutually exclusive. Normal recording
  controls stay disabled while a dialog owns the microphone.
- Dialog chunk analysis is serialized so rolling hypotheses, committed
  messages, retries, and the final queue barrier cannot reorder.
- The durable batch worker is paused for an active Dialog and resumed after
  finalization, preventing a long meeting transcription from blocking live
  turn recognition. Leased work is released back to SQLite before the pause.
- Capture has higher scheduling priority than inference.
- At most one GPU-heavy Whisper job at a time.
- Trainer pronunciation is captured in the primary transcription pass. A
  recoverable optional job backfills older takes only when they have a detected
  speech timeline; its failure never makes otherwise playable audio unusable.
- IPA generation and waveform caching are optional local jobs. Their results
  are persisted, but either can be rebuilt without touching source audio.
- Kokoro defaults to CPU initially to avoid contending with long Whisper jobs;
  a measured GPU option may be introduced later.
- Provider calls use bounded concurrency, cancellation, timeout, jittered retry
  for transient failures, and no retry for authentication/billing errors.
- Speech models load lazily and may unload after an idle period.
- The background queue pauses model work on battery according to user settings,
  but never pauses active capture.

## 8. UI state model

The main window has two top-level workspaces: `Speak` and `All recordings`, in
that order. `Speak` is selected initially and defaults to `AI Dialog`; its
stable mode selector presents `AI Dialog` before `Monologue`. Both workspace
choices and the Speak mode selector share the main 72 px application header;
there is no separate navigation row, so every active workspace begins directly
below that header. Switching the mode changes only the visible surface and
never calls the tray/window restoration path. The dialog collection remains
alive while hidden, preserving its context, playback state, and deliberate
scroll position across a mode round-trip.

Dialog bottom-following re-evaluates the native CollectionView scroller as
virtualized cards are realized. It requires a quiet run of stable bottom
observations and does not issue new scroll requests during that observation
window; this catches WinUI's delayed extent/offset correction without pushing
the correction perpetually forward. Later passive extent growth is followed
only while the conversation was already following the latest turn.

The tray icon derives only from persisted application state:

```text
Recording > AttentionRequired > Processing > Idle
```

Recording wins every visual-state conflict and always shows `r`. This prevents
a background transcription failure from hiding the fact that the microphone is
active.

ViewModels subscribe to domain-state snapshots. They do not infer recording
state from button text or media-player state.

## 9. Security controls

- No keys in repository, environment dumps, diagnostics, or crash payloads.
- Database parameters for all text.
- Strict file-root validation before move/export/delete.
- Atomic replace rather than in-place mutation.
- Hash and signed/controlled manifest for downloaded models.
- Transcript placed in a delimited data field for model calls.
- Dialog transcripts remain user-role messages under a stable, separate system
  instruction and cannot replace that instruction.
- Fixed correction schema and maximum response length.
- HTTPS only for providers.
- Optional proxy uses operating-system settings.
- Export explicitly lists included source audio, compact audio, transcripts, and
  AI derivatives.

## 10. Observability

Local structured events may include:

- state transition and duration;
- byte counts and audio format;
- model/provider identifiers;
- categorized errors;
- GPU/fallback selection;
- queue latency and processing latency.

They must not include transcript content, audio bytes, API keys, authorization
headers, complete file paths containing user names, or raw provider bodies.

The diagnostics screen shows a user-readable processing timeline and can export
a redacted bundle.

## 11. Verification strategy

### Unit

- state transitions and restart idempotency;
- compact/original timeline mapping;
- VAD padding and splice calculations;
- title fallback;
- correction response validation;
- ambiguity/protected-term enforcement;
- dialog turn-boundary decisions;
- configured silence progress, punctuation independence, repeated resets, and
  renewed maximum-turn windows;
- complete ordered conversation request construction;
- speaker-aware answer-schema validation and answer-document round trips;
- file-root and redaction behavior.

### Integration

- WASAPI capture using a controlled virtual/fixture source;
- Opus round trip and seek;
- SQLite current-schema creation, mismatch reset/archive, and interrupted-reset recovery;
- dialog session/message persistence and answer-artifact links;
- raw durable capture chunks converted to mono 16 kHz analysis WAV;
- local model checksum/download resume;
- Buddy-proxy/direct-DeepSeek/Qwen routing and adapter fixtures, including
  included-key fallback, local bearer authentication, request-shape
  differences, default selection, and exact context rejection;
- proxy key hashing, schema reset, quota durability, prompt/completion
  accounting, and stable invalid/disabled/exhausted errors;
- tray state and single-instance redirect.

### Audio golden set

Include consented or synthetic fixtures for:

- clean and noisy English;
- multiple accents;
- low input level;
- clipped input;
- code and technical names;
- numbers, dates, acronyms, and proper names;
- rapid speech and long pauses;
- first/last phoneme around VAD thresholds.

Golden tests measure recognition text and also listen for compact-audio boundary
damage. Automatic metrics alone are insufficient for the release gate.

### Fault injection

- terminate during each capture/finalization stage;
- disk full;
- corrupt or missing chunk;
- device unplug;
- sleep/resume;
- GPU initialization failure;
- offline network;
- timeout, 429, invalid JSON, quota exhausted, and unauthorized provider;
- application update with queued jobs.

## 12. Current-machine fit

The inspected development machine has:

- Windows 11;
- .NET 10 SDK and the MAUI Windows workload;
- NVIDIA RTX 5070 Ti with about 16 GB VRAM;
- CUDA 13 tooling;
- approximately 96 GB system RAM.

This is ample for an unquantized Whisper large-v3-turbo local default. CPU and
Vulkan fallbacks remain necessary because a distributed app cannot assume this
hardware.

## 13. Sources and license audit starting points

- Whisper reference implementation:
  <https://github.com/openai/whisper>
- Whisper.net:
  <https://github.com/sandrohanea/whisper.net>
- whisper.cpp:
  <https://github.com/ggerganov/whisper.cpp>
- Silero VAD:
  <https://github.com/snakers4/silero-vad>
- KokoroSharp:
  <https://github.com/Lyrcaxis/KokoroSharp>
- MisakiSharp:
  <https://github.com/Lyrcaxis/MisakiSharp>
- NAudio:
  <https://github.com/naudio/NAudio>
- Opus:
  <https://github.com/xiph/opus>
- H.NotifyIcon:
  <https://github.com/HavenDV/H.NotifyIcon>
- Windows single-instance guidance:
  <https://learn.microsoft.com/windows/apps/windows-app-sdk/applifecycle/applifecycle-single-instance>

The implementation stage must record exact package, native binary, model, and
voice licenses in a generated third-party notice. Repository license labels are
inputs to that audit, not a substitute for it.
