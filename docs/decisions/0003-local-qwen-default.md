# ADR 0003: Local Qwen 3.6 27B is the default language provider

- Status: superseded as the default by ADR 0004; local implementation retained
- Date: 2026-08-04

## Context

Buddy's correction, title, dialog, and contextual-definition contracts were
implemented against DeepSeek. Audio already stayed local, but using those
features still sent transcript text to a cloud provider. The target machine has
an RTX 5070 Ti with 16 GB VRAM, 96 GB system RAM, and ample space on `D:`, so a
27-billion-parameter local model is practical when it is quantized and only
partially offloaded.

The user requested a Qwen 27B option, loaded on this machine and enabled by
default, while retaining DeepSeek as an alternative.

## Decision

Use the official Qwen 3.6 27B model through the llama.cpp-maintained Q4_K_M GGUF
conversion:

- model revision `4c8d89a3b10d66695ded02bacee44f9dcf64848b`;
- model file `Qwen3.6-27B-Q4_K_M.gguf`, 19,095,766,304 bytes;
- model SHA-256 `65b753ea835627f7b511143c6ceb976525c7f21f5df8c664bc0a9c23d1c49921`;
- matching draft file `dflash-Qwen3.6-27B-Q8_0.gguf`, 1,849,481,440 bytes;
- draft SHA-256 `a31adddb37adaca315b94a18d96d124135ee15b76b7249986e77057267b01909`;
- llama.cpp release `b10243`, CUDA 13.3 x64;
- 32,768-token runtime context, Q8 KV cache, Flash Attention, non-thinking mode,
  DFlash speculative decoding with three draft tokens, 24 target GPU layers,
  and a CPU-resident draft on this machine.

Qwen is the default when no `language.provider-id` setting exists. This is a
normal key/value setting, so no database migration is introduced. Settings
offers Qwen local and DeepSeek cloud, persists an explicit choice, and exposes
Qwen warm-load and unload actions.

The existing schema-validated prompt and parsing engine remains shared. Qwen
omits DeepSeek's request-specific `thinking` field and uses the local
OpenAI-compatible endpoint. Before a dialog request, Buddy obtains an exact raw
token count from llama.cpp, adds response and template reserves, and rejects an
oversized complete history. llama.cpp context shifting is disabled.

The server binds only to loopback and requires a random 256-bit bearer key that
is generated for each Buddy process. It is assigned to a Windows
kill-on-job-close object, stops on explicit application exit, and sleeps after
two idle minutes to release inference resources. Models and runtime stay outside the
single executable under `D:\ai\Buddy`; `BUDDY_AI_ROOT` can override that root.

## Consequences

- Correction, titles, dialog history, and clicked-word context remain local by
  default and work without an API account.
- The selected 27B model favors quality over latency. DFlash retains the target
  model while measured speculative decoding roughly doubled short-response
  generation throughput. DeepSeek remains available when lower latency matters
  more than local privacy.
- The 19.1 GB target and 1.85 GB draft cannot be embedded in `Buddy.exe`; they
  are separately verified local assets.
- Fixed partial offload leaves headroom for Whisper and does not change model
  quantization or output quality, but it uses system RAM and CPU for remaining
  layers.
