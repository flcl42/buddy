# Buddy product specification

Status: Chitchat Buddy 0.2 core implemented; later-stage expansion retained below
Target: Windows 11 desktop, personal-use first
UI technology: .NET MAUI on WinUI 3

## 1. Product promise

Buddy remembers what the user said in meetings, provides a private practice
loop for saying it better next time, and supports persistent spoken AI dialogs
whose context lasts for the complete session.

The product must feel like a dependable recorder first and an AI feature
second. Recording, saving, browsing, and playback remain useful while offline
or when every AI provider is unavailable.

## 2. Scope

### 2.1 Meeting recorder

The user can start and stop recording from the tray menu, the main window, or a
configurable global shortcut.

The app captures the selected microphone in Windows shared mode so that meeting
applications can use the same device. Its purpose is to capture the user's
microphone channel, not the remote participants' system audio.

While capture is active:

- the tray icon changes to a high-contrast recording state containing a literal
  lowercase `r`;
- the tooltip shows `Recording · hh:mm:ss`;
- the tray menu changes from `Start mic recording` to `Stop and save`;
- the main window shows the live duration, input device, and level meter;
- a crash-safe recording journal is updated continuously.

After capture:

- the raw source is finalized before any AI work starts;
- local voice activity detection identifies speech regions;
- a compact playback derivative removes long silence while retaining short,
  natural gaps;
- local Whisper transcribes the speech;
- a short title is generated from the transcript when a language provider is
  available;
- the item appears immediately in All Recordings, even while background work is
  still running.

### 2.2 Monologue workflow

Monologue mode is intentionally a staged workflow. A direct speech-to-speech model
is not the primary design because the user must be able to inspect and edit the
recognized text.

The normal flow is:

1. Press and hold or click `Record practice take`.
2. Speak, then stop manually or allow optional end-of-speech detection.
3. Replay the original take.
4. Review the word-level pronunciation signals and edit the recognized text.
5. Choose an improvement mode and press `Improve`.
6. Review a highlighted diff, explanations, and any ambiguity warnings.
7. Edit the improved version if desired.
8. Choose a voice and speed, then press `Create better audio`.
9. Replay the better version.
10. Compare and optionally record another take.

Every monologue take is saved automatically and appears in All Recordings with
source `Monologue`. Its transcript, edits, improvements, and generated audio are
linked versions, not destructive replacements.

The local Whisper pass also stores each recognized word's start/end time and
token confidence. Monologue mode renders clear words in green, words worth
reviewing in amber, and likely unclear words in red, together with average
confidence and speaking pace. A local IPA transcription appears above those
signals as a pronunciation guide. This is an intelligibility signal for finding
possible pronunciation mistakes; it is explicitly not presented as a
phoneme-level accent score.

Initial improvement modes:

- **Correct only** — grammar, word form, articles, and punctuation with minimal
  change.
- **Natural** — make spoken English fluent while preserving intent and tone.
- **Clear and concise** — remove repetition and make the message easier to
  follow without changing facts.

The model contract requires it to:

- preserve names, numbers, technical terms, facts, uncertainty, and intent;
- never invent evidence or make the speaker sound more certain;
- flag materially ambiguous passages instead of silently guessing;
- return both a corrected version and, where useful, a polished alternative;
- treat the transcript as quoted user data rather than instructions;
- honor a user-maintained vocabulary glossary.

### 2.3 AI Dialog

The user can start one free-form spoken conversation and keep talking without
holding a push-to-talk control. Buddy keeps the microphone capture active for
the whole session while processing durable two-second checkpoints for near-live
recognition.

During a dialog:

- the live transcript is rebuilt from the current utterance and replaced in the
  UI, avoiding duplicated text from overlapping Whisper windows;
- Silero VAD and Whisper run locally;
- a persisted `Allowed pause` preset from 0.75 to 15 seconds determines the
  trailing silence required to commit a user turn; terminal punctuation never
  silently shortens the selected allowance;
- a live progress line advances toward that same detector threshold, and a
  `Reset · keep talking` action restarts the real countdown rather than only its
  presentation; each reset also renews the long-turn safety window and may be
  repeated without a fixed limit;
- the user can still press `Send now` to commit immediately;
- every user and assistant message is persisted before the next request;
- every recognized user turn includes an IPA pronunciation guide and, when the
  turn was captured by the current pipeline, color-coded word confidence and
  timing;
- the selected language provider receives the stable system instruction plus
  every ordered message in the current session on every answer request;
- earlier turns are never silently summarized or dropped by Buddy;
- the selected provider returns every answer as a validated, versioned pair: complete
  Markdown for reading and an equivalent plain-text rendition optimized for
  pronunciation; neither representation may add, omit, or summarize content;
- the answer appears as text even when local speech synthesis is unavailable;
- when Kokoro is installed, each answer gets a saved local WAV, plays
  automatically from the pronunciation-ready rendition, and retains the shared
  compact speech transport;
- AI answers, synthesized user turns, and selected words all use one transport
  pattern: `▶` when idle, `■` plus `Ⅱ` while playing, and `▶` plus `↺` while
  paused. Stop returns to the beginning, resume continues from the paused
  position, and restart begins again from zero;
- every user turn can be spoken on demand by local Kokoro rather than the cloud
  provider;
- new turns scroll into view while the conversation is already at the bottom;
  scrolling upward suspends following until the user returns to the bottom;
- delayed pronunciation and replay controls keep the last reply anchored when
  they increase its height;
- a `Latest` button restores following and changes to `New reply` when a turn
  arrives while the user is reading earlier messages; virtualized layout must
  reach and remain at the true bottom before `Latest` hides.

Recognition pauses while an answer is synthesized and spoken, then resumes
after a short feedback guard so the assistant voice is not mistaken for a new
question. Audio checkpoints observed during thinking and playback are discarded
from live recognition. The original microphone archive may still contain sound
played through speakers; headphones provide the cleanest recording.

`Finish & save` stops the microphone safely, flushes the last partial turn,
stores a readable `You`/`Buddy` conversation revision, and sends the recording
through the normal archive, silence-compaction, search, title, and playback
pipeline. Provider failures retain the user message and expose `Retry answer`.
If a session grows beyond a provider request limit, Buddy reports that limit
instead of pretending to retain context that was not sent.

### 2.4 All Recordings tab

The second top-level workspace contains meeting, monologue, and completed dialog
recordings in one searchable timeline.

Each collapsed row shows:

- AI title or a deterministic fallback title;
- local date and start time;
- source badge: `Meeting`, `Monologue`, or `Dialog`;
- wall-clock duration;
- speech duration after silence compaction;
- processing, ready, warning, or failed status;
- one icon-only play/pause control;
- an inline sound-level waveform with played progress and click-to-seek.

Expanding a row reveals:

- `Compact` / `Original` playback toggle;
- transcript with timestamps;
- monologue versions, if present;
- input device and processing details;
- rename, export, retry processing, and delete actions.

A Dialog row additionally exposes `Read full dialog`. Its reader restores every
persisted user and assistant turn in sequence without truncation, renders the
assistant's original Markdown, restores durable user pronunciation feedback,
and uses the same on-demand word guide and speech controls as the live dialog.
Opening or scrolling the archive performs no definition lookup; IPA and meaning
are requested only when the reader selects a word.

The tab header provides:

- `Start mic recording`;
- search across titles and transcripts;
- filters for source, date, and status;
- a compact storage indicator.

Deletion is soft for a configurable grace period so an accidental deletion is
recoverable.

### 2.5 Speak tab — Monologue mode

The first and default top-level workspace is `Speak`. Its persistent mode
selector presents `AI Dialog` first and `Monologue` second, switching without
navigating to another top-level tab or changing window geometry. AI Dialog is
the initial mode. The `Speak` / `All recordings` choices and the `AI Dialog` /
`Monologue` selector all live in the main application header rather than using
dedicated navigation rows, leaving the remaining window height to the active
workflow. Monologue remains arranged vertically for a clear speaking loop:

```text
┌──────────────────────────────────────────────────────────────┐
│ Speak · Monologue                           Provider status  │
├──────────────────────────────────────────────────────────────┤
│  Input device       live meter       [ Record practice ]     │
│  Original audio                    [ Play ]  00:08            │
├──────────────────────────────────────────────────────────────┤
│  What I said                                               ↕ │
│  [ editable recognized text                              ]  │
│  [ Re-transcribe ] [ Correct only | Natural | Concise ]     │
│                                              [ Improve ]     │
├──────────────────────────────────────────────────────────────┤
│  Pronunciation review                                       │
│  [ clear ] [ review ] [ likely unclear ]   confidence · wpm │
├──────────────────────────────────────────────────────────────┤
│  Better version                                             │
│  [ editable improved text                                ]  │
│  Changed phrases are highlighted; ambiguities appear here.  │
│  Voice [ ... ]  Speed [ 1.0x ]       [ Create better audio ]│
│  Better audio                         [ Play ]  [ Compare ]   │
└──────────────────────────────────────────────────────────────┘
```

Recognition never locks the text box. Re-transcription proposes a new version
and asks before replacing user edits.

### 2.6 Speak tab — AI Dialog mode

AI Dialog mode in the same `Speak` tab separates the durable conversation from
the live utterance:

```text
┌──────────────────────────────────────────────────────────────┐
│ AI Dialog                       [ Start ] [ Finish & save ]   │
├───────────────────────────────────┬──────────────────────────┤
│ Conversation                      │ Live transcription       │
│ You · recognized question         │ [ current utterance ]    │
│ Buddy · formatted answer          │ local Whisper status     │
│ [clicked word · IPA · meaning]    │                          │
│ [ ▶ / ■Ⅱ / ▶↺ ]                  │ [ Send now ] [ Retry ]    │
│ ...                               │ full-context notice      │
└───────────────────────────────────┴──────────────────────────┘
```

The conversation list follows the newest persisted message only while the user
is at the bottom, including when pronunciation or replay controls enlarge the
last card. Scrolling upward preserves the reading position and exposes a
`Latest` / `New reply` control. A phase badge distinguishes listening, local
transcription, provider thinking, local voice generation, saving, and failure.
While the provider is generating, a prominent animated indicator floats over
the conversation without inserting a temporary message or changing the scroll
position.
The privacy notice explicitly distinguishes local audio processing from text
language actions. The release default sends only requested action text through
the capped Buddy proxy to DeepSeek. Direct DeepSeek does the same through the
user's account, while local Qwen keeps the action text on this PC.

Assistant messages retain the provider's Markdown representation and render a
safe native subset: headings, emphasis, strikethrough, lists, quotes, links,
inline and block code, rules, and tables. The equivalent narration is stored in
the answer's local versioned document and is never shown as duplicate chat text.
No WebView or executable HTML is used.
The complete formatted text of both user and assistant messages is selectable
with the mouse and exposes native **Copy** and **Select All** actions plus
`Ctrl+C` for the active selection. A normal click on a rendered word opens a
contextual guide inside that message, generates IPA locally, and requests a
short definition from the language provider only after the click. The guide
includes the same local compact speech transport used by whole turns. Repeated
clicks on the same word in the same turn reuse an in-memory definition and a
content-addressed local speech file. Displaying, restoring, and scrolling
messages must never fan out dictionary or synthesis requests.

### 2.7 Tray and window behavior

- Double-clicking the tray icon opens the window, restores it if minimized, and
  activates it if already open.
- A normal window close hides the window to the tray.
- `Exit Buddy` in the tray menu is never blocked by transcription, AI, or a
  long-running save. It requests normal shutdown after arming a one-second
  watchdog; the durable capture journal and SQLite transactions recover any
  interrupted work on the next launch.
- Only one application instance may run. Launching a second instance activates
  the first.
- Tray menu:
  - Start/stop microphone recording
  - Open Speak · Monologue
  - Open Speak · AI Dialog
  - Open Buddy
  - Settings
  - Exit Buddy
- Optional startup with Windows is disabled until the user opts in.
- The icon has at least four distinguishable states: idle, recording with `r`,
  processing, and attention required.

### 2.8 Settings

Before Settings or any workspace can be used, a one-time blocking setup screen
chooses the interface language (English, Беларуская, Русский), dialog language
(English, German, Spanish, French, Belarusian), and provider (included trial
code, direct DeepSeek, local Qwen). It verifies Whisper, Silero, and the selected
voice in sequence. Qwen progress appears only after the speech dependencies are
ready. A localized completion screen then instructs the user to choose AI
Dialog, press Start, and talk. The initial allowed pause is three seconds.

The first release needs:

- microphone selection and input test;
- answer-speaker selection with the current Windows default, explicit active
  endpoints, and an audible output test;
- hotkey configuration;
- recording storage location and retention;
- compact playback silence length;
- local model status, warm-load, unload, and path controls;
- recognition language: auto-detect or fixed;
- persisted correction/conversation provider selection, with capped Buddy
  DeepSeek access as the release default, direct DeepSeek with the user's key,
  and Qwen 3.6 27B as the local option;
- application-wide resumable local-model setup progress, cancellation, and
  completion/error notifications;
- voice, speed, and pronunciation test;
- vocabulary and pronunciation glossary;
- cloud privacy controls;
- API credential entry, validation, and removal;
- startup and close-to-tray behavior;
- diagnostics export that excludes transcripts, audio, and keys by default.

## 3. Audio behavior

### 3.1 What “my remarks” means

The default product captures the selected microphone channel. With a headset,
this is a clean approximation of only the user's remarks. With speakers or a
room microphone, other people can leak into the recording.

Reliable speaker separation from a room recording would require speaker
enrollment/verification or diarization. It is a separate feature with different
accuracy, privacy, and test requirements and is not silently promised by the
base recorder.

### 3.2 Silence omission

“Pauses omitted” means compact playback, not destructive chopping:

- speech regions receive a small lead-in and tail;
- brief pauses remain natural;
- long gaps collapse to a short configurable gap, initially about 200 ms;
- adjacent regions use a small fade to avoid clicks;
- the original recording and a compact-to-original time map are retained.

The UI reports both wall duration and speech duration so the user knows exactly
what was compressed.

### 3.3 Durability

Capture writes bounded, recoverable chunks and a journal. It does not hold a
meeting in memory or wait until stop to create a file.

Finalization order:

1. close and validate source chunks;
2. encode and validate the original archive;
3. commit metadata atomically;
4. derive compact audio, waveform, transcript, and title;
5. remove temporary chunks only after the source archive is verified.

If the application or machine crashes, the next launch offers to recover any
unfinished capture.

## 4. Privacy and trust

Default data flow:

```text
Microphone
    │
    ├── local source and compact audio
    ├── local Silero speech detection
    └── local Whisper transcription + word timing/confidence
             │
             ├── local Monologue pronunciation review
             ├── editable Monologue text ──> correction provider
             ├── complete dialog text history ──> conversation provider
             └── clicked word + its one-turn context ──> definition provider

Improved text ──> local Kokoro voice ──> local generated audio
AI answer text ──> safe chunks ──> local Kokoro voice ──> local answer audio
User turn or selected word ──> local Kokoro cache ──> shared speech transport
```

Rules:

- meeting audio is never uploaded automatically;
- cloud re-transcription is a per-item, explicit action;
- the UI states exactly what will leave the device before first use;
- provider requests contain the minimum required text and a random request ID;
  for a dialog, the complete current-session message history is required to
  preserve context; a word definition request is made only after a click and
  contains the selected word plus that one user or assistant turn;
- OpenAI requests use non-persistent options where the endpoint supports them;
- secrets are protected with Windows credentials or DPAPI;
- logs exclude keys, raw provider payloads, transcript text, and file contents;
- original content is never overwritten by an AI result;
- model provenance is stored with each generated artifact.

## 5. Accessibility and convenience

- Full keyboard operation and visible focus.
- Proper automation labels for screen readers.
- Recording state is communicated by icon, text, and tooltip, not color alone.
- Large primary controls in Monologue mode.
- Playback shortcuts for play/pause, seek backward, and seek forward.
- High-contrast and Windows text-scaling support.
- All destructive actions are undoable or confirmed.
- Background processing never steals focus.

## 6. Error behavior

The app must clearly distinguish:

- no microphone permission;
- device busy, removed, or changed;
- insufficient disk space;
- model missing or corrupt;
- recognition failed;
- provider offline, timed out, rate-limited, out of credit, or unauthorized;
- dialog context rejected for size without silently dropping prior turns;
- speech generation failed;
- archive recovery required.

AI failures leave a retryable recording. Provider failures never make capture
or local playback unavailable.

## 7. Definition of quality

Before the first releasable build:

- A two-hour synthetic capture survives without unbounded memory growth.
- Killing the process during capture leaves recoverable audio.
- Device unplug/replug and default-device changes produce an actionable state.
- Windows sleep/resume is handled without corrupting a recording.
- A second launch activates the first instance.
- Closing the window preserves the tray process; explicit Exit stops it.
- The tray `r` state is visible in light, dark, and high-contrast modes.
- Compact playback does not cut first/last phonemes in the speech test corpus.
- Original and compact timelines seek correctly.
- User transcript edits survive reprocessing and application restart.
- Monologue word timing/confidence survives restart; red/amber attention
  thresholds and subword merging are covered by deterministic tests.
- Correction tests cover ambiguity, names, numbers, technical vocabulary,
  prompt-injection text, and provider schema failures.
- Dialog tests cover turn boundaries, ordered full-context requests, persistent
  messages, configured silence progress and repeated countdown resets, strict
  formatted/spoken answer pairs, durable narration documents, restart-safe
  schema reset/archive recovery, recording-to-dialog lookup, archived Markdown
  reconstruction, and raw-chunk speech preparation.
- The app is fully useful for recording and playback offline.
- API keys and private content do not appear in diagnostic logs.

## 8. Delivery stages

### Stage 1 — durable recorder shell

MAUI window, single-instance behavior, tray icon and `r` state, shared-mode
microphone capture, journaled storage, SQLite catalog, and original playback.

### Stage 2 — compact recordings

Silero voice activity detection, compact time mapping, Opus derivatives,
waveform, searchable recordings UI, recovery, and device-change handling.

### Stage 3 — local recognition

Resumable Whisper model installation, CUDA inference with fallback, transcripts,
word timing/confidence, processing queue, title fallback, and model diagnostics.

### Stage 4 — complete Monologue mode

Editable recognition, word-level pronunciation review, versioned improvement
workflow, provider-neutral adapter, diff/ambiguity UI, Kokoro speech, comparison
playback, and glossary.

### Stage 5 — persistent spoken dialog

Two-second durable live checkpoints, rolling local transcription, explicit
turn detection, persistent ordered messages, full-context provider answers,
local answer synthesis, and normal recording finalization.

### Stage 6 — hardening and optional providers

Long-running and fault-injection tests, packaging, OpenAI retry adapters, Kimi
adapter after account availability, accessibility pass, performance tuning, and
installer validation.

## 9. Confirmed consequential assumptions

The following defaults were accepted on 2026-07-30 and are implemented:

1. **English-first Monologue mode.** Whisper recognition remains multilingual, but the
   first local high-quality voice and pronunciation work target English.
2. **Microphone-channel capture.** “My remarks” means audio entering the selected
   microphone, ideally a headset. Room-mic speaker identification is not part of
   the first release.
3. **Personal Windows app with bring-your-own API keys.** No hosted Buddy backend
   and no bundled shared provider secret.

Changing any of these alters the model, licensing, privacy, or audio design and
should be treated as a new product decision.

The requested recording, Monologue, and persistent AI Dialog loops are in Buddy
0.1. Some convenience and expansion requirements retained in this
specification are intentionally staged after 0.1; the exact boundary and live
verification evidence are recorded in
[release-readiness.md](release-readiness.md).
