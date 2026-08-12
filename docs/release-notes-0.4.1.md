# Chitchat Buddy 0.4.1

Buddy 0.4.1 is a packaging, portability, and release-trust update across all
three desktop builds.

## Highlights

- The Windows installer now installs for the current user without an
  administrator prompt, uses the operating system's standard per-user program
  location, and preserves recordings and settings during upgrades.
- Recordings, settings, speech models, and AI models now default to the standard
  per-user application-data location on each operating system. Explicit
  environment overrides remain available for advanced and portable setups.
- Chitchat Buddy source and first-party binaries are now distributed under the
  MIT License. Bundled third-party speech components retain their own licenses,
  documented in `THIRD-PARTY-NOTICES.md`.
- The multilingual website now includes a captioned end-to-end product
  walkthrough covering setup, Dialog, lazy word guidance, and recording review.
- Release automation validates machine-neutral paths and packages the license
  and third-party notices into the Windows, Linux, and macOS builds.

## Downloads

- `Buddy-Setup.exe` is the recommended Windows x64 installer.
- `Buddy.exe` is the portable Windows x64 single-file build.
- `Buddy-Linux-x64-preview.deb` and
  `Buddy-Linux-x64-preview.tar.gz` are Linux x64 previews.
- `Buddy-macOS-arm64-beta.zip` is the Apple Silicon Mac Catalyst beta.

Windows is the stable desktop release. Linux tray integration remains a preview,
and the macOS build remains beta and uses the Dock instead of a menu-bar status
item. Microphone audio and speech recognition stay local on every platform.

The binaries are currently unsigned or ad-hoc signed, so Windows SmartScreen and
macOS Gatekeeper can still show publisher warnings. Verify downloads against
`SHA256SUMS.txt`.
