# ADR 0002: Build AI Dialog on durable local speech chunks

Date: 2026-07-30
Status: Accepted, amended 2026-08-03

## Context

Buddy needs a third workflow where the user can start a spoken dialog, talk
without holding a push-to-talk control, see recognition update while speaking,
receive contextual AI answers, and save the finished session in All
Recordings.

The existing product already has crash-recoverable WASAPI capture, local
Silero VAD, local Whisper transcription, a DeepSeek provider, local Kokoro
synthesis, and SQLite persistence. A separate realtime cloud speech stack
would duplicate those boundaries and would make meeting audio privacy depend
on a provider.

Two details require explicit design:

1. batch Whisper does not expose a stable word-by-word streaming contract;
2. a conversational provider must receive prior turns, not only the latest
   transcript.

## Decision

Add `AI Dialog` as a third recording kind and a third tab.

### Capture and live recognition

- Keep one crash-recoverable microphone recording active for the whole dialog.
- Flush one-second raw capture chunks and publish a completion event only after
  each chunk is durable and readable.
- Copy completed chunks into an isolated live-analysis directory before the
  normal recording finalizer can remove its capture journal.
- Rebuild a mono 16 kHz analysis WAV from the current utterance and run local
  Silero VAD followed by local Whisper.
- Replace the visible partial transcript with the latest whole-utterance
  result. Do not append overlapping Whisper snapshots.
- Complete a user turn after the persisted 0.75–15 second allowed-pause preset,
  after a resettable safety maximum for uninterrupted speech, or when the user
  presses `Send now`. Terminal punctuation does not shorten the selected pause.
- Publish measured silence progress for the UI. `Reset · keep talking` advances
  the detector's real countdown origin and may be repeated, rather than merely
  resetting a visual timer.

This is near-live rolling recognition, not a claim of token-level realtime
Whisper output. One-second durable checkpoints keep latency bounded while
retaining the existing recovery guarantees.

### Conversation and context

- Persist a dialog session and every user/assistant message in SQLite before
  advancing to the next turn.
- Build every DeepSeek request from the ordered, complete message list stored
  for that session.
- Never silently discard earlier messages. If a provider eventually rejects a
  session for its context limit, show that failure instead of pretending the
  model still has the missing history.
- Treat speech transcripts as user messages, not system instructions.
- Keep a stable Buddy system instruction outside the user transcript.

The local database is the source of truth for conversational memory. The
provider receives the complete history again on every response, so a transient
process restart or provider request does not reduce the session to the latest
turn.

### Answers and saved recordings

- Show the text answer as soon as the provider returns.
- Generate a local Kokoro WAV for each assistant answer when the model is
  installed and play it automatically. Keep a manual replay control.
- Suppress live recognition during thinking, synthesis, playback, and a short
  post-playback guard. Discard already queued checkpoints from that interval so
  the assistant cannot become the next recognized user turn.
- On `Finish & save`, flush the last partial turn, store a readable
  `You`/`Buddy` transcript revision, finalize the original microphone archive,
  run normal silence compaction, and expose the result in All Recordings with
  source `Dialog`.

## Consequences

Positive:

- live audio and recognition remain local;
- dialog context survives UI refreshes and is auditable in SQLite;
- the final session participates in existing playback, search, title, recovery,
  and tray behavior;
- one microphone implementation and one model set remain responsible for all
  three product workflows.

Costs:

- rolling batch transcription updates at chunk boundaries rather than every
  word;
- each partial update reprocesses the current utterance;
- provider context is finite even though Buddy never truncates it silently;
- playing an answer through speakers can still be present in the original
  microphone archive, although it is excluded from recognition.

## Acceptance

1. The third tab can start and finish a dialog.
2. A durable audio chunk updates the visible partial transcript.
3. VAD reaches the selected allowed pause, or `Send now` commits immediately.
4. The request contains every earlier user and assistant message in order.
5. Each message is persisted before the next provider call.
6. Assistant text appears even if Kokoro is unavailable.
7. Generated assistant WAVs play automatically and can be replayed.
8. Finishing creates a normal recording with source `Dialog`.
9. Meeting and Trainer capture cannot start while a dialog is active.
10. A process restart does not delete completed dialog messages.
11. Silent checkpoints do not rebuild the conversation collection or move its
    scroll position.
12. New turns follow when the conversation is at the bottom and preserve the
    position when the user has scrolled upward.
