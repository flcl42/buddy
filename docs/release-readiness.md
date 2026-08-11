# Chitchat Buddy 0.3 release readiness

Updated: 2026-08-11

This record separates the working product from later expansion items in the
broader product specification.

## Verified in this build

- Debug and Release MAUI builds complete with zero warnings.
- 174 automated tests pass across App presentation, Core,
  Persistence, Language, Proxy, and Integration.
- A real clean-state first run passed the blocking localized setup flow. The
  interface changed to Belarusian without restarting, every dependency status
  changed language in place, a lowercase trial code normalized to the required
  `AAAAAA-BBBBBB` shape, and the UI stayed responsive while 1,624,555,275-byte
  Whisper, 885,098-byte Silero, and 325,508,342-byte Kokoro artifacts downloaded
  to `H:\Buddy\models` and received verification stamps. The localized party
  completion screen opened the default Speak chooser with a selected
  three-second pause.
- The welcome screen performs no model download or model-status network work
  before the explicit Download and set up action. Its editable pickers and key
  fields use one rounded native Windows frame with clear normal, hover, focus,
  and disabled states.
- The default window is 1,260 by 830 and is centered once inside the active
  monitor work area. Hyper-V UI Automation measured the complete setup action
  inside the window with zero visible vertical scrolling; later tab changes,
  maximizing, and manual resizing retain the user's geometry.
- Opening Speak presents separate, equally sized AI Dialog and Monologue cards.
  Settings and feedback are modal surfaces that close when their backdrop is
  clicked without affecting the active workspace.
- The feedback surface accepts bounded text and one optional signature-checked
  PNG, JPEG, or WebP image. Hyper-V UI Automation opened the modal, attached an
  image, kept the process responsive, and found no plaintext credential file.
  The proxy authenticates the request before forwarding it and returns stable
  invalid, unavailable, and delivery-failure codes.
- A final clean release run held the visible included-trial selection stable
  while localized Picker items refreshed, completed setup in 583 ms, and left
  an existing 2,954,887,168-byte resumable Qwen partial download untouched.
- Explicit tray exit no longer awaits dialog finalization, transcription, model
  shutdown, or save completion. It arms the recovery-safe one-second process
  watchdog before requesting normal MAUI shutdown. A real installed tray-menu
  invocation exited the process in 117 ms.
- Capped Buddy DeepSeek access is the default when no explicit provider setting
  exists. Settings persists a choice among the proxy, direct DeepSeek, and local
  Qwen without a schema migration. Selecting Qwen starts resumable verified
  model/runtime setup with visible global progress and notifications.
- The proxy is live at its pinned HTTPS endpoint from `/root/buddy-proxy`.
  Acceptance calls verified a real DeepSeek V4 Flash completion, durable and
  separate prompt/completion counters across redeployment, a 1,000-reply and
  1,000,000-token release cap, and stable responses for invalid (401), disabled
  (403), reply-exhausted (429), and token-exhausted (429) keys. Temporary
  acceptance keys were disabled afterward.
- Release packaging produces exactly `Buddy.exe` as the portable single-file
  app and `Buddy-Setup.exe` as the guided installer. The installer compiles with
  Inno Setup 6.7.3, defaults to `C:\Programs`, and blocks replacement while the
  tray process is running.
- Qwen 3.6 27B Q4_K_M was the prior default and remains fully selectable.
- The official 19,095,766,304-byte GGUF at pinned revision
  `4c8d89a3b10d66695ded02bacee44f9dcf64848b` matches SHA-256
  `65B753EA835627F7B511143C6CEB976525C7F21F5DF8C664BC0A9C23D1C49921`.
  The matching 1,849,481,440-byte DFlash Q8_0 draft at the same revision matches
  SHA-256 `A31ADDDB37ADACA315B94A18D96D124135EE15B76B7249986E77057267B01909`.
  Both pinned llama.cpp `b10243` CUDA 13.3 archives also match their published
  SHA-256 digests. The installed runtime reports build `10243 (563dec81c)`.
- Three live runs through Buddy's real Qwen adapter passed correction, title,
  speaker-aware Markdown plus narration, remembered dialog context, and lazy
  contextual definition contracts. The final run also exercised authenticated
  token counting, disabled context shifting, Windows kill-on-job-close process
  ownership, and clean GPU release. The pre-acceleration run completed in 2
  minutes 46 seconds; the DFlash run passed the same four language contracts in
  1 minute 31 seconds.
- Measured DFlash profiles rejected full draft offload because it left only
  128 MiB free. The production profile uses 24 target GPU layers and a
  CPU-resident draft: a clean synthetic structured response measured 8.17
  generated tokens per second with 72.9% draft acceptance while retaining
  3.4-3.7 GiB free for Whisper. The previous production trace measured
  3.83-3.90 tokens per second. The 120-second idle window also avoids a measured
  6-8 second reload between nearby dialog operations. Hosted DeepSeek remains
  the lower-latency alternative through either the capped proxy or a direct key.
- Prior installed UI Automation opened Settings when Qwen was still the default,
  loaded the exact `D:\ai\Buddy` model, and reached `Loaded locally · 32,768
  token context · 24 GPU layers · DFlash accelerated`. It switched to DeepSeek
  and back through the real picker, survived a full restart with Qwen selected,
  and returned to the ready-on-disk state. The final installed check reported
  9,979 MiB GPU use with 6,017 MiB free; idle sleep
  returned total GPU use to 2.5 GiB and woke for a request in 7.85 seconds.
  Unauthenticated completion and tokenization requests both returned HTTP 401.
  Terminating Buddy with a managed server active reduced the llama.cpp process
  count from one to zero, directly verifying kill-on-job-close cleanup.
- The main navigation has exactly two top-level tabs: `Speak` first and
  `All recordings` second. `Speak` and its first mode, `AI Dialog`, are the
  application defaults. Both tabs and the persistent `AI Dialog` / `Monologue`
  selector now share the 72 px application header. Removing the two dedicated
  navigation rows moves every workspace heading from 256 px to 128 px below
  the window top, recovering 128 px for conversation and recording content.
- The active workspace uses a solid Buddy-purple fill with white text; the
  active Speak mode uses a lighter purple fill and outlined edge. Every button
  also has an explicit inactive state, preventing a previously selected tab or
  mode from retaining its active colors after a switch. Installed visual checks
  covered Speak/AI Dialog, All recordings, and Speak/Monologue transitions.
- Dialog following now tracks tail-card height changes as pronunciation and
  answer audio arrive, debounces rapid updates, converges against the changing
  native virtualized extent, and waits through a quiet run of stable bottom
  observations before exposing `Latest` / `New reply` while the user reads
  earlier turns.
- The installed conversation opens at exactly 100%. It was moved to
  23.949647% and preserved that exact
  reading position across both AI Dialog/Monologue and Speak/All recordings
  round-trips. `Latest` then converged against the virtualized extent to exactly
  100%, hid itself, and retained the normal `1180×780` window bounds.
- Installed UI Automation found `Speak`, `All recordings`, `AI Dialog`, and
  `Monologue` on the same header baseline. Every normal-size switch retained
  `78,78,1180,780`; every maximized switch retained
  `-8,-8,2576,1408`; all three workspace headings began 128 px below the
  window top.
- Code formatting validation passes.
- Real Windows microphone enumeration identifies all active capture endpoints.
- The Settings input test reaches its listening state and reports a real input
  peak (15% in the release test).
- Meeting and Monologue capture both create recoverable chunks, original Opus,
  compact Opus, speech segments, and local transcripts.
- Long gaps collapse to 200 ms with lead-in, tail, and boundary fades.
- Monologue transcription now persists merged word timings and confidence in the
  same local Whisper pass. Green/amber/red word chips, speaking pace, summary
  counts, timing accessibility text, and stable no-rebuild refresh behavior are
  covered by focused tests.
- Monologue and Dialog pronunciation cards include a local IPA guide. New Dialog
  user turns persist their message, IPA, and timed confidence words atomically;
  earlier usable turns are backfilled with IPA without inventing word timing.
- Every recording card now uses one icon-only play/pause button and a cached
  96-bar sound-level envelope. The waveform is a real decoded-audio summary,
  shows played progress and a playhead, and supports direct click-to-seek.
- Whisper large-v3-turbo and Silero artifacts download resumably and pass
  SHA-256 verification before use.
- DeepSeek returns schema-validated improvement results and generated titles.
- Assistant replies now keep their Markdown source while a native Markdig/MAUI
  renderer displays headings, emphasis, strikethrough, lists, quotes, links,
  code, rules, and tables. Each installed message exposes one native WinUI
  `RichTextBlock` with styled inline runs, without a WebView or raw HTML
  execution.
- The installed saved-dialog reader and live conversation expose their rendered
  messages through the native text-selection pattern. The reader's right-click
  menu exposed enabled `Select All` and `Copy` actions; a real 39-character
  message selection copied all 39 characters exactly through both the menu and
  `Ctrl+C`, and the pre-test Windows clipboard was restored after each check. A
  coordinate-level normal click on archived `introduce` still opened the lazy
  word guide and cleared the prior selection, confirming that text selection
  did not replace word interaction.
- Every rendered word on both sides of the Dialog has a real Windows tap
  target. An installed coordinate-level click on the user word `lenient`
  opened `/lˈiːnɪənt/`, `adjective`, and `not strict; allowing freedom and
  relaxed rules.` Its on-demand local speech entered an active Windows audio
  session and then stopped to an inactive zero-peak session. Restoring and
  scrolling the conversation initiates no eager word or synthesis requests.
- AI Dialog persists ordered user/assistant messages, sends the complete session
  history on every answer request, rejects silent context truncation, and keeps
  a failed answer retryable.
- Every Dialog recording now exposes `Read full dialog`. The installed reader
  resolved a real recording through its persisted session, restored all 16
  ordered turns with native Markdown and durable pronunciation content, and
  reached the final archived turn at exactly 100% scroll. It displayed no eager
  definition activity on open; clicking the first archived user word then
  produced local IPA, part of speech, and contextual meaning and started the
  selected local provider only on demand. Closing the reader returned to the
  recording list without changing the `312,312,1180,780` window bounds.
- AI Dialog now requires the versioned `buddy.conversation-answer.v1` result:
  complete `display_markdown` for the native conversation plus equivalent
  `spoken_text` for Kokoro. Both fields are independently bounded and validated;
  the pair is atomically retained in a per-message answer document without a
  database migration. Replay refreshes reuse that narration, while older saved
  answers retain the Markdown-to-speech fallback.
- A live request through Buddy's actual DeepSeek adapter returned a Markdown
  heading and two formatted bullets containing `24 kHz`; its separate narration
  removed the structure markers and digits and said `twenty-four kilohertz`.
  No private dialog content was used by the probe.
- One-second Dialog capture checkpoints convert to mono 16 kHz analysis WAV;
  deterministic VAD rules now use the persisted 0.75–15 second allowed-pause
  preset without a hidden punctuation shortcut. Evaluations expose measured
  silence and normalized progress, while explicit send remains immediate.
- The live transcription card exposes the pause preset, a smooth progress line,
  and `Reset · keep talking`. Reset advances the detector's real audio-time
  origin, not just the bar, and can be repeated; it also renews the 45-second
  safety window. The setting uses the existing key/value store and requires no
  database schema change.
- Installed UI Automation found the accessible allowed-pause combo at the
  normal `1180×780` window size. Selecting `3 s` survived a full process
  restart; the verification then restored and rechecked the prior `1.1 s ·
  quick` selection. Rapid picker changes cancel stale pending writes before the
  serialized settings update.
- Dialog recognition is explicitly English and receives no generated-answer
  text as a decoder prompt. Punctuation-only, one-character, repeated-phrase,
  and observed short decoder-garbage patterns are rejected instead of being
  sent to the conversation provider.
- Every Buddy playback path pauses active Dialog recognition. Capture remains
  gated through the feedback tail and discards the first complete microphone
  frame after playback before returning the UI to `Listening`.
- User turns are generated locally into a versioned, content-addressed cache;
  the existing installed checks covered both cache miss and cache hit paths. A
  138-character user turn entered an active WASAPI session, and stopping made
  eight consecutive samples inactive with zero peak.
- AI answers, user turns, and selected words now share one compact segmented
  transport. Idle exposes `▶`; playing exposes `■` plus `Ⅱ`; paused exposes
  `▶` plus `↺`. Paused playback retains its owner and keeps Dialog recognition
  gated. Stop returns to zero, resume continues from the held position, and the
  dedicated restart operation seeks and plays atomically without emitting a
  false finished state to the Dialog coordinator.
- Installed UI Automation scrolled a saved AI answer into view and drove the
  complete native sequence: `Play AI answer`, `Stop` plus `Pause`, `Resume` plus
  `Restart`, resume, pause, restart from zero, then stop back to `Play`. The
  process remained responsive throughout. A saved user turn independently
  entered the same stop-plus-pause presentation; selected-word speech is bound
  to that same reusable transport component.
- Settings now exposes the Windows multimedia default and all four active
  render endpoints, supports a persistent explicit speaker, and provides a
  two-note test. Installed UI Automation expanded the picker, invoked the test,
  and replayed a saved AI answer through WASAPI on `Speakers (Realtek(R)
  Audio)`. Both paths created active, unmuted Buddy sessions with nonzero peaks;
  the UI reported the tested endpoint and returned replay to its ready state.
- The affected late turn from the latest saved Dialog was replayed from its
  original archive: the audio that live recognition had reduced to `...D`
  transcribed as a coherent English request, confirming that the saved
  microphone data and local Whisper model were intact.
- Silent VAD checkpoints and empty Whisper results publish no redundant UI
  state. Dialog messages are reconciled in place; eight focused presentation
  tests verify that unchanged snapshots and audio updates cause zero collection
  resets, a new turn adds only its new tail item, bottom-following resumes
  correctly, and scrolling upward preserves the reading position.
- Schema evolution now uses one current-schema declaration with no incremental
  migration pipeline. Automated checks cover populated unversioned, older, and
  newer databases plus an interrupted reset: prior state is archived, a clean
  schema is created, and models, logs, and protected provider secrets remain.
- The two existing Monologue takes with detected speech were backfilled locally
  on CUDA into 17 durable word rows. An older manually edited take without a
  speech timeline is skipped rather than retried on every launch.
- UI Automation opened the installed Monologue mode, found the pronunciation
  summary plus all eight timed word groups, scrolled the card into view, and a
  visual check at 1180×780 confirmed the card and legend render without
  clipping.
- The current schema-5 database retains 18 recordings, 73 audio artifacts, 35
  transcript revisions, and 58 Dialog messages. Integrity is `ok`, foreign-key
  violations are zero, and there are no active captures, active dialogs,
  pending/running jobs, capture journals, or dialog scratch files.
- The installed build sought a saved Opus recording to 65% (`0:02 / 0:03`),
  switched the sole playback icon from `Play` to `Pause`, continued from that
  position, and returned to `Play` when paused. A decoder regression test also
  verifies that Opus position advances from the requested sample instead of
  incorrectly reporting end-of-stream.
- Installed visual checks at 1180×780 confirmed the partial purple waveform
  progress, Monologue IPA and confidence chips, and the Dialog pronunciation
  panel without clipping.
- The installed AI Dialog tab was opened through UI Automation and exposed the
  expected start, finish, conversation, live transcription, send, retry,
  context, and privacy controls. A visual pass at the default 1180×780 window
  confirmed that the right-side cards fit without clipping.
- Kokoro creates a valid mono, 24 kHz, PCM16 WAV, speaks each Dialog answer
  automatically, and retains replay after synthesis and application restart.
- Straight and typographic-apostrophe negative contractions receive clear
  spoken expansions before synthesis (`is not`, `does not`, `will not`) while
  saved and displayed answer text stays untouched. Deterministic tests cover
  the expansion and prove existing explicit user glossary literals remain
  intact.
- Older Markdown-only answers are normalized before Kokoro without modifying
  the stored answer; new speaker-aware answers send their dedicated plain-text
  narration through the same final safety normalization.
  A live Release-service check synthesized
  `**Important**: *Buddy* should say [clear words](https://example.com).` to a
  2.751-second WAV; local CUDA Whisper recovered exactly
  `Important, Buddy should say clear words.` with no spoken delimiters or link
  destination.
- Kokoro input is now split losslessly at sentence/clause boundaries below the
  safe model sequence length, with a short PCM pause between chunks. A scan of
  the latest saved Dialog reproduced the long-text defect: the old sequence-17
  WAV omitted `ambiguous`, `basic cause`, and `and well-organized`, with the
  failures appearing before later inline contraction overrides. The installed
  build refreshed that same artifact in place with
  `synthesis=buddy.kokoro-safe-sequences.v1`, entered real WASAPI playback, and
  its stop action returned to an inactive zero-peak session. CUDA Whisper then
  recovered every
  substantive word, including all three formerly missing phrases; contractions
  were recovered as their intentional full spoken forms. The message text,
  microphone archive, and database schema were unchanged.
- Original and generated playback can pause and replay after reaching EOF.
- A live UI Automation check held the conversation at 35% scroll while an
  answer played and stopped; repeated playback events left it at exactly 35%.
- The installed virtualized conversation opened at the bottom (99.98%), kept a
  deliberately scrolled-up position exactly unchanged across tab switches, and
  resumed bottom-following at 100%.
- In-window tab commands no longer call the tray window-restoration path. A
  live installed check maximized Buddy and switched through Speak · AI Dialog,
  All recordings, Speak · Monologue, and back to AI Dialog. Every
  surface retained the exact `-8,-8,2576,1408` native window bounds.
- The inactive Dialog header exposes only its enabled `Start dialog` action.
  During an active session that action is replaced by `Finish & save`, which
  remains available while another turn is processing and changes to `Saving…`
  while finalization is running.
- A live notification-area pixel check confirms that the ready state renders a
  visible white lowercase `b` on Buddy purple; capture switches the same
  state-driven Windows icon to a white `r` on red.
- A second launch activates the primary instance without leaving another
  process behind.
- Closing the window leaves the tray process alive; the ready-state tray entry
  is present and restores the window.
- The application recovers pending processing jobs after restart.
- Provider keys are DPAPI-protected and private content is absent from startup
  diagnostics.
- The 270.3 MiB self-contained x64 publish contains exactly one file, installs
  atomically to `C:\Programs\Buddy.exe`, includes exactly four curated Kokoro
  voices, excludes foreign Whisper runtimes, and launches through its Start
  Menu shortcut without relying on the machine-wide .NET runtime.
- The installed single-file build was launched directly from `C:\Programs`,
  generated a local Kokoro WAV in 5.5 seconds, entered playback, returned to its
  ready state, hid to the tray, and restored from the tray.
- A reboot-interrupted 13-minute Dialog was recovered from 398 durable chunks:
  its original and compact Opus files, all 16 messages, and all eight existing
  answer WAVs were preserved; SQLite integrity remained `ok`, and terminal
  dialog scratch files were removed on the next startup.

## Packaging boundary

The installed executable is `C:\Programs\Buddy.exe`, version 0.3.0, and matches
the staged portable release. Exact portable and installer hashes are published
in `SHA256SUMS.txt` alongside each release. This personal build is intentionally
unpackaged and unsigned; a public release still requires a trusted code-signing
certificate and a distribution decision compatible with the bundled
GPL-3.0-or-later eSpeak NG runtime. See
[THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md).

## Release scope

Chitchat Buddy 0.3 covers the requested recording plus the unified Speak workspace's
Monologue and persistent AI Dialog loops. These broader specification items
remain later-stage work and are not presented as implemented:

- configurable global hotkey;
- start with Windows;
- filters beyond text search;
- expanded recording details, export, rename, soft-delete, and retry controls;
- configurable retention and storage relocation;
- recognition-language selector;
- user-managed vocabulary and pronunciation glossary UI;
- Kimi and OpenAI provider adapters;
- privacy-safe diagnostics export;
- formal two-hour soak, forced-crash, sleep/resume, and device-unplug test
  matrix;
- a formal multi-accent acoustic corpus for Dialog end-of-turn latency and
  very-long-session provider-limit testing.

These omissions do not block the core local recorder, silence-compacted
playback, editable recognition, language improvement, synthesized replay, or
persistent full-context dialog workflow.
