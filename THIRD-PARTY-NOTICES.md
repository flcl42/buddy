# Buddy third-party notices

This file documents the principal third-party components in the Buddy desktop
builds. It is not legal advice and does not replace the license texts supplied
by their authors.

## Speech models and runtimes

| Component | Use | License | Source |
| --- | --- | --- | --- |
| Kokoro 82M model and selected voices | Local text-to-speech | Apache-2.0 | <https://github.com/hexgrad/kokoro> |
| KokoroSharp 0.8.0 | Kokoro inference | MIT | <https://github.com/Lyrcaxis/KokoroSharp> |
| MisakiSharp 1.0.0 | English grapheme-to-phoneme processing | Apache-2.0 | <https://github.com/Lyrcaxis/MisakiSharp> |
| eSpeak NG | Native phonemizer used by KokoroSharp | GPL-3.0-or-later | <https://github.com/espeak-ng/espeak-ng> |
| Whisper.net 1.9.1 | Whisper and Silero inference | MIT | <https://github.com/sandrohanea/whisper.net> |
| whisper.cpp model/runtime | Local speech recognition | MIT | <https://github.com/ggml-org/whisper.cpp> |
| ONNX Runtime 1.22.0 | Kokoro inference runtime | MIT | <https://github.com/microsoft/onnxruntime> |
| Qwen 3.6 27B | Default local language model | Apache-2.0 | <https://huggingface.co/Qwen/Qwen3.6-27B> |
| llama.cpp b10243 | External local Qwen runtime | MIT | <https://github.com/ggml-org/llama.cpp> |

The installed application includes the full Apache-2.0 text beside the selected
Kokoro voices at `voices/LICENSE`, and the full GPL version 3 text beside the
eSpeak runtime at `espeak/LICENSE`.

The Qwen GGUF and llama.cpp runtime are separately installed under
`H:\BuddyAI` when that drive is available, or under Buddy's per-user data root;
they are not embedded in the single-file Buddy executable. `BUDDY_AI_ROOT` can
override that location. Their source pages and license identifiers are recorded
above for provenance.

Important: eSpeak NG is GPL-3.0-or-later software. Anyone redistributing a Buddy
binary that includes it must independently satisfy the GPL's source,
corresponding-source, notice, and combined-work requirements. Do not treat the
personal installation script in this repository as a cleared public
distribution package.

## Audio, UI, and data libraries

| Component | License | Source |
| --- | --- | --- |
| NAudio 2.3.0 | MIT | <https://github.com/naudio/NAudio> |
| MiniAudioExNET 3.3.5 | MIT | <https://github.com/japajoe/MiniAudioExNET> |
| miniaudio | Public domain or MIT-0, at the user's option | <https://github.com/mackron/miniaudio> |
| Concentus 2.2.2 / Opus | 3-clause BSD-style Opus license | <https://github.com/lostromb/concentus> |
| Concentus.Oggfile 1.0.7 | MIT | <https://github.com/lostromb/concentus.oggfile> |
| CommunityToolkit.Mvvm 8.4.2 | MIT | <https://github.com/CommunityToolkit/dotnet> |
| H.NotifyIcon 2.4.1 | MIT | <https://github.com/HavenDV/H.NotifyIcon> |
| Markdig 1.3.2 | BSD-2-Clause | <https://github.com/xoofx/markdig> |
| .NET MAUI and Microsoft.Extensions | MIT | <https://github.com/dotnet/maui> |
| Microsoft MAUI Linux GTK4 preview | MIT | <https://github.com/dotnet/maui-labs/tree/main/platforms/Linux.Gtk4> |
| Tmds.DBus.Protocol 0.94.2 | MIT | <https://github.com/tmds/Tmds.DBus> |
| Tmds.DBus.Generator 0.94.2 | MIT | <https://github.com/tmds/Tmds.DBus> |
| SQLite | Public domain | <https://www.sqlite.org/copyright.html> |
| SQLitePCLRaw 3.0.5 | Apache-2.0 | <https://github.com/ericsink/SQLitePCL.raw> |

### Concentus / Opus notice

Copyright (c) by various holding parties, including (but not limited to):
Skype Limited, Xiph.Org Foundation, CSIRO, Microsoft Corporation,
Jean-Marc Valin, Gregory Maxwell, Mark Borgerding, Timothy B. Terriberry,
Logan Stromberg. All rights are reserved by their respective holders.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

- Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.
- Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.
- Neither the name of Internet Society, IETF or IETF Trust, nor the names of
  specific contributors, may be used to endorse or promote products derived
  from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

### MIT license text

MIT attributions in this build include the .NET Foundation and contributors;
Mark Heath and NAudio contributors; Logan Stromberg and Andrew Ward;
HavenDV/H.NotifyIcon contributors; japajoe/MiniAudioExNET contributors;
Tmds.DBus contributors;
the dotnet/maui-labs contributors; Lyrcaxis/KokoroSharp contributors;
sandrohanea/Whisper.net contributors; Microsoft/ONNX Runtime contributors;
ggml-org/llama.cpp contributors; and
the respective contributors to other MIT packages listed above.

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
