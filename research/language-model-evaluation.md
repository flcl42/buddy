# Language improvement provider evaluation

Date: 2026-07-30
Purpose: choose a first interactive provider for Buddy's speech trainer

## Method

`compare-language-models.ps1` sends the same eight synthetic English speech
samples to each candidate. The samples exercise:

- ordinary grammar errors;
- filler and awkward spoken phrasing;
- protected numbers and dates;
- uncertainty;
- a double negative with ambiguous intent;
- technical names and terms;
- nullability and code-like wording.

The request asks for schema-shaped JSON containing a minimally corrected
version, a polished spoken version, and brief change notes. No personal
recordings, private transcripts, or secrets are included in the prompt.

This is a small product-oriented probe, not a statistically significant model
benchmark.

## Results

One successful non-thinking run produced:

| Model | Elapsed | Prompt tokens | Completion tokens | Parse |
| --- | ---: | ---: | ---: | --- |
| `deepseek-v4-flash` | 5.263 s | 367 | 748 | Valid JSON |
| `deepseek-v4-pro` | 11.964 s | 367 | 1,065 | Valid JSON |

An earlier request that did not explicitly disable thinking exhausted its
2,000-token allowance and failed to return a usable structured result. Provider
configuration must therefore be explicit, tested, and stored with each run.

The Kimi credential was recognized by its endpoint, but the account currently
rejects completion requests for insufficient balance. No Kimi quality or
latency claim is made.

## Qualitative observations

- Pro was generally more natural and grammatically careful.
- Flash was fast enough to be the better interactive default.
- Both models silently chose an interpretation for a deliberately ambiguous
  double negative.
- A technical phrase around a padded cache-line type was normalized in a way
  that could erase an intentional identifier.
- Both preserved the explicitly protected server counts, date, version, devnet,
  block number, and uncertainty reasonably well in this small sample.

## Product consequences

The provider result cannot be treated as truth or replace user content. Buddy
must:

- preserve every original and user-edited revision;
- let the user edit recognition before improvement;
- show meaningful changes as a diff;
- support protected vocabulary and code identifiers;
- ask the model for structured ambiguities and alternatives;
- validate the response schema and reject truncated output;
- let the user edit improved text before synthesis;
- delimit the transcript as untrusted quoted data;
- have a deterministic fallback when the provider is unavailable.

## Initial provider policy

- Default: DeepSeek V4 Flash, thinking disabled.
- Optional quality action: DeepSeek V4 Pro, thinking disabled.
- Kimi: adapter can be implemented, but leave disabled until a live evaluation
  is possible.
- OpenAI: optional adapter after the user supplies a separately billed API key.

Repeat this evaluation with a larger consented/synthetic corpus and human
ratings before release. Latency must also be measured from inside the app using
the production request schema.
