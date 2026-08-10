# ADR 0001: Use a staged local-first speech stack

Date: 2026-07-30
Status: Accepted

## Context

Buddy needs high-quality long-form recording, editable recognition, language
improvement, and natural speech synthesis. The user has access to DeepSeek,
Kimi, and Codex and is open to Whisper and Kokoro.

The central decision is whether the app should use:

1. an entirely cloud pipeline;
2. an entirely local pipeline;
3. a local-first hybrid with explicit cloud improvements.

The editing requirement rules out making a direct, opaque
speech-to-speech session the primary workflow. Recognition, text editing,
language improvement, and synthesis must remain separate stages.

## Decision

Choose a local-first hybrid:

- **Capture:** NAudio with Windows WASAPI shared-mode microphone capture.
- **VAD:** local Silero VAD.
- **Recognition:** unquantized Whisper large-v3-turbo through Whisper.net,
  preferring CUDA 13 on the current machine.
- **Correction and titles:** DeepSeek V4 Flash with thinking disabled by
  default; DeepSeek V4 Pro as an optional quality mode.
- **Synthesis:** Kokoro through KokoroSharp and MisakiSharp for an English-first
  package.
- **Cloud options:** explicit OpenAI transcription/speech adapters and provider
  adapters for Kimi, not required for core operation.

Original audio and text remain immutable. Compact audio, transcripts,
corrections, titles, and generated speech are versioned derivatives.

## Evidence

### Current machine

The development PC has an RTX 5070 Ti with roughly 16 GB VRAM, CUDA 13 tooling,
and approximately 96 GB RAM. Local Whisper is therefore a practical quality
default rather than a constrained fallback.

### Recognition

OpenAI describes Whisper `turbo` as an optimized large-v3 model with much higher
relative speed and minimal accuracy loss. Whisper.net provides in-process .NET
bindings and current CUDA/Vulkan/CPU runtime choices, avoiding a Python sidecar.

For difficult audio, OpenAI's current `gpt-4o-transcribe` model claims improved
word error rate and language recognition over original Whisper. This is useful
as a manual retry, not a reason to upload every meeting.

### Correction

The included synthetic comparison harness ran eight error-containing English
examples against the currently exposed DeepSeek models:

| Model | Mode | Total time | Result |
| --- | --- | ---: | --- |
| DeepSeek V4 Flash | thinking disabled | 5.263 s | Valid structured output |
| DeepSeek V4 Pro | thinking disabled | 11.964 s | Valid, generally more natural output |

Flash is suitable for interactive default latency. Pro is appropriate when the
user explicitly asks for the best wording.

Both models made at least one potentially unsafe interpretation rather than
flagging the ambiguity. Pro could also normalize a phrase that might have been
an intentional code identifier. Therefore:

- original and edited text must be preserved;
- protected glossary terms are required;
- ambiguity must be a structured result;
- the UI must show a diff;
- the result remains editable.

The Kimi credential currently reaches the provider but the account rejects
requests for insufficient balance. Its current models therefore cannot be
honestly ranked by this evaluation yet.

The sanitized test prompt, limitations, and product implications are recorded
in `research/language-model-evaluation.md`; the runnable harness is
`research/compare-language-models.ps1`.

### Synthesis

KokoroSharp provides a native .NET/ONNX path. MisakiSharp provides English and
Chinese phonemization without a Python or eSpeak runtime and exposes a natural
place for pronunciation overrides. This reduces installation and licensing
complexity for an English-first application.

Technical out-of-vocabulary terms still need a user glossary and preview.
Kokoro is the default, not the only provider.

### OpenAI and Codex

Codex is not an embedded speech service for this application. OpenAI does offer
separate transcription and speech APIs, but OpenAI API usage is managed and
billed separately from consumer ChatGPT/Codex subscriptions. Buddy can support
those APIs after the user supplies a separate OpenAI API key.

The staged OpenAI path is:

```text
audio -> transcription API -> editable text
      -> language model -> editable improvement
      -> speech API -> generated audio
```

It preserves the same user control as the local path.

## Alternatives considered

### Entirely OpenAI cloud

Advantages:

- low local model-management burden;
- strong transcription quality;
- additional voice choices.

Rejected as the default because meeting audio would leave the machine, the app
would depend on network/quota, long recordings create ongoing cost, and the
user's local GPU would be unused. It remains an opt-in retry path.

### `faster-whisper` Python sidecar

Advantages:

- mature and fast CTranslate2 implementation;
- broad community usage.

Rejected for the primary Windows build because packaging Python, native
dependencies, process supervision, IPC, update compatibility, and diagnostics
add failure modes that Whisper.net avoids.

### Kimi as the correction default

Deferred because the current credential cannot complete a live request due to
account balance. A provider name alone is not enough evidence to choose it over
the measured DeepSeek path.

### Kimi 3 for interactive correction

Not preferred because its current API behavior is reasoning-oriented and cannot
be made a simple non-thinking request in the same way as the lighter model
paths. Grammar correction should not pay extended reasoning latency by default.

### Direct realtime speech-to-speech

Rejected as the primary Trainer workflow because it bypasses the required
editable recognition checkpoint. It may later become an optional conversational
practice mode.

### Full large-v3 before turbo

Deferred. The quality difference should be measured on Buddy's audio golden set
before asking users to download and run a slower model. Turbo is the stronger
initial speed/quality balance on the target machine.

### ffmpeg for audio storage

Rejected as a required runtime dependency. Managed capture and Opus handling
make deployment smaller and reduce external-process failure and update risks.

## Consequences

Positive:

- recordings and recognition work offline;
- meeting audio remains private by default;
- cloud cost is limited mostly to short text correction;
- each stage is independently testable and replaceable;
- the application uses the current PC's GPU effectively;
- users can inspect and edit every textual stage.

Costs:

- first-run model downloads and model lifecycle UI are required;
- GPU/CPU fallback behavior must be tested;
- native model packages add installer size;
- two audio derivatives and time mapping add storage/schema complexity;
- provider adapters and version provenance need ongoing maintenance.

## Acceptance

The user confirmed:

- English is the first trainer/voice language;
- microphone-channel capture is the intended meaning of “my remarks”;
- bring-your-own provider keys are acceptable for the personal Windows release.

Validate the chosen versions with a compile spike:

1. MAUI + H.NotifyIcon double-click and dynamic `r` icon.
2. Concurrent shared-mode microphone use.
3. Ten-minute journaled capture and Opus round trip.
4. Whisper.net CUDA 13 transcription of the golden fixture.
5. KokoroSharp/MisakiSharp synthesis and playback.
6. DeepSeek structured correction using the production schema.

## References

- OpenAI Whisper:
  <https://github.com/openai/whisper>
- Whisper.net:
  <https://github.com/sandrohanea/whisper.net>
- OpenAI `gpt-4o-transcribe`:
  <https://developers.openai.com/api/docs/models/gpt-4o-transcribe>
- KokoroSharp:
  <https://github.com/Lyrcaxis/KokoroSharp>
- MisakiSharp:
  <https://github.com/Lyrcaxis/MisakiSharp>
- OpenAI API billing separation:
  <https://help.openai.com/en/articles/8156019>
- OpenAI API data controls:
  <https://platform.openai.com/docs/models/default-usage-policies-by-endpoint>
