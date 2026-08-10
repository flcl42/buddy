# Buddy

Buddy is a Windows 11 speech companion built with .NET MAUI. It has three linked
workflows:

1. Open directly into **Speak · AI Dialog**, talk freely, watch the local
   transcript update, get contextual answers with automatic local voice
   playback, and save the complete session as a normal recording.
2. Switch the same **Speak** workspace to **Monologue** to record a practice
   take, fix recognition
   mistakes in the editable transcript, improve its grammar and wording, and
   hear the revised version spoken aloud.
3. Record your microphone during meetings and replay only the useful speech,
   with long pauses collapsed.

The recorder is local-first and remains useful without a cloud provider.

## Implemented product

- A tray icon with idle, recording (`r`), processing, and attention states.
- Click or double-click to reopen the window, close-to-tray behavior, and
  single-instance activation.
- Meeting, Monologue, and AI Dialog microphone capture through Windows WASAPI
  shared mode.
- Persistent microphone selection plus a four-second live input test.
- Crash-recoverable capture chunks and a durable SQLite catalog: five-second
  chunks normally and two-second checkpoints for live dialogs.
- Original Ogg Opus archives and compact derivatives made with Silero VAD.
- Natural pause compaction: speech lead-in/tail, 200 ms collapsed gaps, fades,
  and a compact-to-original timeline map.
- Local Whisper large-v3-turbo transcription with CUDA preference and CPU
  fallback.
- Searchable recordings with date, time, source, wall duration, speech duration,
  generated title, state, an icon-only play/pause control, and a cached
  sound-level waveform that supports click-to-seek. Dialog rows also open the
  complete saved conversation in a dedicated reader: Markdown formatting,
  pronunciation feedback, per-turn replay, and click-to-load word meanings all
  remain available after restart.
- An editable Monologue transcript, three improvement modes, versioned
  provider-neutral results, change notes, ambiguity warnings, and
  protected-term checks.
- A durable local Monologue pronunciation review with IPA phonetic transcription,
  per-word timing and confidence, green/amber/red attention cues, average
  confidence, and speaking pace.
- A persistent AI Dialog with rolling local Whisper updates, VAD-based
  turn detection, manual `Send now`, retryable answers, and every prior turn
  resent in order so context is never silently discarded. Recognized user turns
  include the same pronunciation view and IPA guide as Monologue mode.
- A persisted **Allowed pause** control chooses 0.75–15 seconds before an
  utterance is sent. A live silence line fills toward that real detector
  threshold; **Reset · keep talking** restarts the detector countdown and can be
  used repeatedly while finishing a longer idea.
- Every AI Dialog answer is speaker-aware: the selected provider returns a versioned pair
  containing complete Markdown for the screen and an equivalent plain-language
  narration for the local voice. Buddy validates and saves both, displays only
  the Markdown, and sends only the narration to Kokoro.
- Assistant replies render Markdown headings, emphasis, lists, quotes, links,
  code, and tables without executing embedded HTML. Words in both your turns
  and Buddy's replies remain clickable, while the complete rendered message is
  mouse-selectable with native **Copy**, **Select All**, and `Ctrl+C`. A normal
  word click opens an inline guide: IPA is generated locally, and the contextual
  meaning is requested from the selected provider only then and cached for that
  turn.
- Compact one-line navigation: `Speak`, `All recordings`, `AI Dialog`, and
  `Monologue` share the main header so the active workflow gets the full
  remaining window height without changing window geometry.
- Local Kokoro synthesis with four curated English voices, three speaking
  speeds, saved Monologue and dialog-answer WAV artifacts, automatic Dialog
  answers, replay after restart, on-demand speech for your own dialog turns and
  selected words, and a real Stop action for every dialog speech control.
  New answers use their dedicated pronunciation-ready narration. Older answers
  safely fall back to removing Markdown punctuation and hidden link destinations
  while retaining visible content. Long replies are synthesized in bounded,
  lossless sentence chunks; negative contractions such as `isn't`, `doesn't`,
  and `won't` use clear spoken forms such as `is not`, without changing the
  displayed text. Older Dialog WAVs are refreshed in place on first replay;
  later replays and repeated local snippets use their updated audio cache.
- Resumable, SHA-256-verified downloads for Whisper, Silero, and Kokoro.
- Three language-provider choices. Release builds start with capped Buddy proxy
  access to DeepSeek V4 Flash (1,000 dialog replies or 1,000,000 combined input
  and output tokens), while direct DeepSeek with your own key and private local
  Qwen 3.6 27B remain selectable. The proxy exposes durable quota counters and
  explicit invalid, disabled, reply-exhausted, and token-exhausted key errors.
- Qwen 3.6 27B Q4_K_M runs through a pinned local llama.cpp CUDA runtime and
  matching DFlash speculative draft. Selecting Qwen starts a resumable setup
  with visible progress and completion/error notifications. Every downloaded
  model and runtime archive is pinned by size and SHA-256. Balanced CUDA/CPU
  offload reserves GPU memory for Whisper, stays warm across nearby dialog
  turns, sleeps after two idle minutes, and uses a fresh process-local bearer
  key.
- Bring-your-own credentials protected with Windows DPAPI; keys, transcript
  content, and audio content are excluded from diagnostics.

See the [product specification](docs/product-specification.md), the
[technical architecture](docs/technical-architecture.md), and the
[release-readiness record](docs/release-readiness.md).

## Speech stack

| Job | Active implementation |
| --- | --- |
| Capture and playback | NAudio, Windows WASAPI shared mode |
| Storage | Ogg Opus through Concentus; generated speech as PCM WAV |
| Voice activity detection | Silero VAD, local |
| Recognition | Whisper large-v3-turbo through Whisper.net, local |
| Phonetic transcription | eSpeak NG through the bundled KokoroSharp tokenizer, local |
| Grammar, vocabulary, titles, dialog answers | Capped Buddy DeepSeek proxy by default; direct DeepSeek with your key; or local Qwen 3.6 27B Q4_K_M through llama.cpp |
| Speech synthesis | Kokoro 82M through KokoroSharp, local |
| Metadata and recovery queue | SQLite in WAL mode |
| Tray integration | H.NotifyIcon.Maui |

Kimi and OpenAI key slots are reserved for optional adapters and are explicitly
labelled as inactive in the current build. OpenAI speech-to-text and
text-to-speech can be integrated through the OpenAI API, but API usage is
separate from ChatGPT or Codex subscriptions and requires API credentials and
billing.

## Run from source

Requirements:

- Windows 11 x64
- .NET SDK 10.0.302 or a compatible .NET 10 SDK
- the .NET MAUI Windows workload

```powershell
dotnet workload install maui-windows
dotnet restore Buddy.slnx
dotnet run --project src/Buddy.App/Buddy.App.csproj
```

Open Settings to verify the microphone and answer speaker and inspect the local
models. Published releases include a capped Buddy proxy key and select that
provider by default. Source builds can read `BUDDY_PROXY_API_KEY` and
`BUDDY_PROXY_CERT_SHA256`, or you can select local Qwen or direct DeepSeek.
Buddy reads `DEEPSEEK_API_KEY` from the process environment without copying it
into the application database.

AI Dialog requires the local Whisper and Silero models plus working access from
one of the three language providers. Kokoro is optional: answers still appear
as text when its local model has not been downloaded.

## Use AI Dialog

1. Open Buddy. **Speak · AI Dialog** is the first and default workspace; select
   **Start dialog**.
2. Speak normally. The current utterance refreshes in **Live transcription**.
3. Pause for an automatic turn, or select **Send now** to commit it immediately.
   Choose **Allowed pause** to control how long Buddy waits. While a recognized
   turn is waiting, the progress line shows the remaining silence; choose
   **Reset · keep talking** as often as needed to postpone sending.
4. Buddy speaks the answer automatically when Kokoro is installed. The answer
   keeps its useful Markdown while the voice uses the equivalent
   pronunciation-ready form produced with it. Every answer and user turn uses
   the same compact transport: **▶** starts it, **■** and **Ⅱ** stop or pause it
   while playing, and **▶** and **↺** resume or restart it while paused.
5. Click any word in either side of the conversation for locally generated IPA
   and a concise contextual definition. The definition is loaded only after
   the click; its speech uses the same transport as complete turns. Drag across
   message text to select any passage, then press `Ctrl+C` or right-click for
   **Copy** or **Select All**.
6. Continue speaking; every earlier turn remains part of the same AI request
   context.
7. Select **Finish & save**. The conversation and silence-compacted microphone
   recording then appear in **All recordings** with source **Dialog**. Choose
   **Read full dialog** on that recording to reopen every formatted turn; click
   any archived word for its lazy IPA and contextual meaning.

The conversation follows new turns while its scrollbar is at the bottom. Scroll
up to review an earlier turn and Buddy preserves that reading position. A
**Latest** button returns to the bottom; it changes to **New reply** when a turn
arrives while you are reading older messages. Delayed pronunciation and replay
controls also keep the true bottom anchored without restarting the scroll.

Use headphones during an active dialog. Buddy suppresses recognition while it
speaks and briefly afterward, but speaker audio can still be present in the
original microphone archive.

## Build and install

Build the default Release configuration:

```powershell
.\build.ps1
```

The result is exactly one self-contained executable at
`C:\Programs\Buddy.exe`. The same defaults are stored in the MAUI project, so
this command also writes the single executable there:

```powershell
dotnet publish .\src\Buddy.App\Buddy.App.csproj -c Release
```

The build script stages and validates the bundle before atomically replacing
the installed executable. To create both distributable assets, install Inno
Setup 6 and run:

```powershell
.\scripts\build-installer.ps1 -Version 0.1.0
```

This produces `artifacts\release\Buddy-Setup.exe` and the portable
`artifacts\release\Buddy.exe`. The installer defaults to `C:\Programs`, adds a
Start Menu shortcut, offers a desktop shortcut, refuses to overwrite a running
tray instance, and preserves personal data. The current binaries are unsigned,
so Windows may show an unknown-publisher warning. Run
`.\scripts\install.ps1` for the original direct single-file installation flow.

On first launch, .NET extracts the versioned native speech runtime into its
per-user bundle cache; the portable app needs no companion files beside
`Buddy.exe`. Personal recordings and speech models remain in
`%LOCALAPPDATA%\Buddy` across upgrades. Whisper, Silero, and Kokoro are fetched
only when needed with resumable verified downloads. Selecting local Qwen starts
its approximately 21.5 GB verified model/runtime setup. Qwen lives under
`D:\ai\Buddy` on this installation; set `BUDDY_AI_ROOT` before launch to use
another root.

## Data and privacy

Meeting audio, compact audio, Whisper transcription, IPA generation, word-level
pronunciation signals, waveform generation, and Kokoro generation are local.
The default release provider sends only the text required for each requested
language action through the capped Buddy proxy to DeepSeek; microphone audio
never leaves the device. The proxy stores key hashes and numerical usage
events, not dialog text. Direct DeepSeek sends the same action text using your
own account. With Qwen selected, improvement text, complete AI Dialog history,
titles, and clicked-word context stay on this PC. The selected provider returns
both the formatted answer and its equivalent narration; both are saved locally,
and narration audio is generated entirely by local Kokoro.
IPA and all Kokoro speech remain local, and no word lookup is performed while
turns are merely displayed or scrolled. Original audio and original transcript
revisions are never overwritten by AI output.

Buddy records the selected microphone channel, not the meeting application's
remote audio. A headset gives the cleanest approximation of “only my remarks”;
speaker identification from a room microphone is not claimed.

## Verification

```powershell
dotnet test Buddy.slnx --no-restore
dotnet format Buddy.slnx --no-restore --verify-no-changes
dotnet build src/Buddy.App/Buddy.App.csproj --no-restore
```

The application has 138 automated tests across domain and turn rules,
schema reset/archive recovery and dialog persistence, speaker-aware provider
routing and local-Qwen authentication/context handling,
configurable/resettable silence boundaries, durable
answer documents, unified speech-transport state, lazy word-definition handling,
Markdown rendering/speech normalization, model
verification, raw and Opus audio processing and seeking, waveform generation,
phonetic transcription, contraction pronunciation, local speech synthesis, and
proxy key/quota durability.
Live checks are recorded in
[release-readiness.md](docs/release-readiness.md).

The deployment is deliberately confined to `/root/buddy-proxy`; operational
commands and the no-system-changes boundary are documented in
[deploy/buddy-proxy/README.md](deploy/buddy-proxy/README.md). The localized
project website lives in [site](site).

## Distribution note

The personal build contains eSpeak NG, used by Kokoro's phonemizer and licensed
under GPL-3.0-or-later. Its complete license and the third-party notices are
embedded in the executable and materialized with the runtime assets. Review
the obligations described in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) before distributing Buddy
binaries outside personal use.
