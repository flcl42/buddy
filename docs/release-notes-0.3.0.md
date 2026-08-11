# Chitchat Buddy 0.3.0

Buddy 0.3.0 focuses on a calmer first run and a clearer everyday workspace.

## Highlights

- Speak now opens with two large choices: start a contextual AI Dialog or record
  a focused Monologue. Nothing begins recording until the selected action is
  pressed.
- First-run downloads wait for the explicit Download and set up action. The
  welcome screen explains this, keeps every selector editable, and shows local
  setup progress only after consent.
- The default 1,260 by 830 window is centered on first launch, fits the complete
  setup wizard without scrolling on the tested 1,600 by 952 work area, and
  leaves later window geometry under the user's control.
- Native Windows entries, editors, and selectors now share clean rounded input
  chrome with distinct focus and disabled states. Settings close when the user
  clicks outside the panel.
- A new Feedback surface sends a written report and one optional PNG, JPEG, or
  WebP screenshot through the authenticated Buddy proxy. Audio and transcripts
  are never attached automatically.
- The multilingual website now shows the real setup, mode chooser, dialog word
  guide, and Monologue improvement screens and resolves downloads from the
  latest GitHub release.

## Installation

`Buddy-Setup.exe` is the recommended guided installer. `Buddy.exe` is the
portable single-file build. Both target Windows x64 and are self-contained.

The binaries are currently unsigned, so Windows may show an unknown-publisher
warning. Verify the accompanying `SHA256SUMS.txt` before installation.
