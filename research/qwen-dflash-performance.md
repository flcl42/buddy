# Qwen DFlash performance check

Date: 2026-08-04

Hardware: Ryzen 9 9950X3D, RTX 5070 Ti 16 GB, 96 GB system RAM.
Runtime: llama.cpp b10243 CUDA 13.3. Target:
`Qwen3.6-27B-Q4_K_M.gguf`. Draft: `dflash-Qwen3.6-27B-Q8_0.gguf`.
Context: 32,768 tokens, one slot, Q8 KV cache, Flash Attention, reasoning off.

The production trace without speculative decoding generated at 3.83-3.90
tokens per second. A 1,283-token answer took 338.46 seconds, and a later
1,420-token answer took 374.33 seconds. Model wake-up added 6-8 seconds whenever
the old 20-second idle threshold expired.

Synthetic structured-answer probes used the same concise prompt and JSON shape:

| Profile | Target GPU layers | Draft GPU layers | Generation | Acceptance | Free VRAM | Decision |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Maximum offload | auto | auto | 9.41 tok/s | 56.1% | 128 MiB | Reject: no Whisper headroom |
| Target-heavy | 28 | 0 | 8.03 tok/s | 66.7% | 2.5 GiB | Reject: marginal speech headroom |
| Balanced | 24 | 0 | 8.17 tok/s | 72.9% | 3.4-3.7 GiB | Accept |

The balanced long-prompt prefill measured 148.61 tokens per second, versus
roughly 260-330 tokens per second in recent production traces. Generation is
still the dominant cost for Buddy's speaker-aware answers because the formatted
and pronunciation-ready representations are both produced by the target model.
For the observed long answers, the generation gain materially outweighs the
prefill regression.

The DFlash model card warns that the drafter and engine support remain new. The
target Qwen model, quantization, response contract, and schema validation remain
unchanged; omitting the draft path and returning to 28 target GPU layers is the
rollback. Raw benchmark logs are in `research/qwen-performance/`.
