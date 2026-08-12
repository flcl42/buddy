#!/usr/bin/env python3
"""Build the public Buddy walkthrough from verified real-app captures.

The checked-in site/video files are the release artifacts. Rebuilding also needs
the private QA captures under artifacts/ and the prepared WAV clips on H:.
"""

from __future__ import annotations

import argparse
import math
import subprocess
import wave
from pathlib import Path

import cv2
import imageio_ffmpeg
import numpy as np
from PIL import Image, ImageDraw, ImageFont


WIDTH = 1284
HEIGHT = 842
FPS = 24
DURATION = 58.0


def font(size: int, semibold: bool = False) -> ImageFont.FreeTypeFont:
    name = "seguisb.ttf" if semibold else "segoeui.ttf"
    return ImageFont.truetype(str(Path("C:/Windows/Fonts") / name), size)


def load_rgb(path: Path) -> np.ndarray:
    image = cv2.imread(str(path), cv2.IMREAD_COLOR)
    if image is None:
        raise FileNotFoundError(path)
    if image.shape[1] != WIDTH or image.shape[0] != HEIGHT:
        image = cv2.resize(image, (WIDTH, HEIGHT), interpolation=cv2.INTER_LANCZOS4)
    return image


def alpha_rect(
    frame: np.ndarray,
    rectangle: tuple[int, int, int, int],
    color: tuple[int, int, int],
    alpha: float,
    radius: int = 14,
) -> np.ndarray:
    overlay = Image.fromarray(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)).convert("RGBA")
    layer = Image.new("RGBA", overlay.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    draw.rounded_rectangle(rectangle, radius=radius, fill=(*color, round(alpha * 255)))
    return cv2.cvtColor(np.asarray(Image.alpha_composite(overlay, layer).convert("RGB")), cv2.COLOR_RGB2BGR)


def draw_text(
    frame: np.ndarray,
    xy: tuple[int, int],
    text: str,
    size: int,
    color: tuple[int, int, int],
    semibold: bool = False,
    anchor: str | None = None,
) -> np.ndarray:
    image = Image.fromarray(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
    draw = ImageDraw.Draw(image)
    draw.text(xy, text, font=font(size, semibold), fill=color, anchor=anchor)
    return cv2.cvtColor(np.asarray(image), cv2.COLOR_RGB2BGR)


def ease(value: float) -> float:
    value = min(1.0, max(0.0, value))
    return value * value * (3.0 - 2.0 * value)


def mix_frames(left: np.ndarray, right: np.ndarray, amount: float) -> np.ndarray:
    amount = ease(amount)
    return cv2.addWeighted(left, 1.0 - amount, right, amount, 0.0)


def add_caption(frame: np.ndarray, kicker: str, title: str) -> np.ndarray:
    frame = alpha_rect(frame, (22, 756, 1262, 824), (18, 22, 38), 0.90, 16)
    frame = draw_text(frame, (44, 776), kicker.upper(), 13, (172, 174, 255), True)
    frame = draw_text(frame, (44, 799), title, 20, (255, 255, 255), True)
    return frame


def add_highlight(
    frame: np.ndarray,
    rectangle: tuple[int, int, int, int],
    pulse: float,
    color: tuple[int, int, int] = (91, 92, 226),
) -> np.ndarray:
    strength = 0.08 + 0.06 * (0.5 + 0.5 * math.sin(pulse * math.tau))
    frame = alpha_rect(frame, rectangle, color, strength, 14)
    overlay = frame.copy()
    cv2.rectangle(overlay, rectangle[:2], rectangle[2:], (226, 192, 91), 2, cv2.LINE_AA)
    return cv2.addWeighted(frame, 0.84, overlay, 0.16, 0.0)


def draw_cursor(frame: np.ndarray, x: float, y: float, click: float = 0.0) -> np.ndarray:
    x = int(round(x))
    y = int(round(y))
    if click > 0:
        radius = int(10 + 22 * click)
        overlay = frame.copy()
        cv2.circle(overlay, (x, y), radius, (226, 92, 91), 3, cv2.LINE_AA)
        frame = cv2.addWeighted(frame, 0.78, overlay, 0.22, 0.0)
    points = np.array([[x, y], [x + 2, y + 25], [x + 8, y + 18], [x + 16, y + 31], [x + 22, y + 27], [x + 14, y + 15], [x + 25, y + 13]], np.int32)
    shadow = points + np.array([2, 3])
    cv2.fillPoly(frame, [shadow], (18, 20, 29), cv2.LINE_AA)
    cv2.fillPoly(frame, [points], (255, 255, 255), cv2.LINE_AA)
    cv2.polylines(frame, [points], True, (28, 30, 40), 2, cv2.LINE_AA)
    return frame


def build_dialog_before(repository: Path, dialog_tooltip: np.ndarray) -> np.ndarray:
    source = cv2.imread(str(repository / "artifacts/rdp-responsive-dialog2.png"), cv2.IMREAD_COLOR)
    if source is None:
        raise FileNotFoundError(repository / "artifacts/rdp-responsive-dialog2.png")
    # The verified RDP capture contains a 1284x842 Buddy window at this exact rectangle.
    clean = source[102:944, 166:1450].copy()
    clean[:, 900:] = dialog_tooltip[:, 900:]
    return clean


def build_recordings(repository: Path, header_source: np.ndarray) -> np.ndarray:
    source = cv2.imread(
        str(repository / "artifacts/verification/waveform-recordings-installed.png"),
        cv2.IMREAD_COLOR,
    )
    if source is None:
        raise FileNotFoundError(repository / "artifacts/verification/waveform-recordings-installed.png")
    body = source[157:772, 8:1164]
    body = cv2.resize(body, (WIDTH, HEIGHT - 103), interpolation=cv2.INTER_LANCZOS4)
    result = header_source.copy()
    result[103:] = body

    # Apply the current two-tab header treatment while retaining the verified waveform body.
    result = alpha_rect(result, (520, 39, 716, 95), (255, 255, 255), 1.0, 0)
    result = draw_text(result, (560, 67), "Speak", 14, (82, 84, 104), True, "mm")
    result = alpha_rect(result, (602, 46, 718, 90), (91, 92, 226), 1.0, 11)
    result = draw_text(result, (660, 68), "All recordings", 13, (255, 255, 255), True, "mm")
    result = alpha_rect(result, (1047, 137, 1244, 184), (91, 92, 226), 1.0, 12)
    result = draw_text(result, (1145, 160), "Start mic recording", 13, (255, 255, 255), True, "mm")
    return result


def scene_frame(
    timestamp: float,
    choose: np.ndarray,
    dialog_before: np.ndarray,
    dialog_tooltip: np.ndarray,
    recordings: np.ndarray,
    monologue: np.ndarray,
) -> np.ndarray:
    if timestamp < 2.2:
        blurred = cv2.GaussianBlur(choose, (0, 0), 14)
        frame = cv2.addWeighted(blurred, 0.28, np.full_like(blurred, (45, 33, 88)), 0.72, 0)
        frame = draw_text(frame, (WIDTH // 2, 330), "Chitchat Buddy", 48, (255, 255, 255), True, "mm")
        frame = draw_text(frame, (WIDTH // 2, 391), "Speak. Improve. Remember.", 25, (221, 222, 255), False, "mm")
        frame = alpha_rect(frame, (527, 442, 757, 496), (91, 92, 226), 0.95, 14)
        frame = draw_text(frame, (642, 469), "A one-minute tour", 16, (255, 255, 255), True, "mm")
        return frame

    if timestamp < 5.5:
        frame = choose.copy()
        frame = add_highlight(frame, (104, 216, 630, 801), timestamp, (91, 92, 226))
        progress = ease((timestamp - 2.2) / 2.2)
        x = 1090 + (366 - 1090) * progress
        y = 260 + (748 - 260) * progress
        click = max(0.0, 1.0 - abs(timestamp - 4.55) / 0.35)
        frame = draw_cursor(frame, x, y, click)
        return add_caption(frame, "Choose a mode", "Start an ongoing dialog or one focused monologue")

    if timestamp < 22.5:
        frame = dialog_before.copy()
        if timestamp < 10.0:
            frame = add_highlight(frame, (47, 269, 889, 469), timestamp, (91, 92, 226))
            # A restrained live microphone level animation in the existing level rail.
            bars = 15
            for index in range(bars):
                value = 0.25 + 0.75 * abs(math.sin(timestamp * 5.2 + index * 0.62))
                x = 950 + index * 16
                height = int(30 * value)
                cv2.rectangle(frame, (x, 322 - height), (x + 7, 322), (91, 92, 226), -1)
            return add_caption(frame, "Live dialog", "Your words appear with IPA and pronunciation confidence")
        frame = add_highlight(frame, (47, 472, 907, 681), timestamp, (91, 92, 226))
        answer_progress = min(1.0, max(0.0, (timestamp - 10.3) / 11.8))
        cv2.line(frame, (62, 671), (62 + int(818 * answer_progress), 671), (91, 92, 226), 4, cv2.LINE_AA)
        return add_caption(frame, "Automatic answer", "Formatted for reading and voiced naturally without another click")

    if timestamp < 29.5:
        transition = min(1.0, max(0.0, (timestamp - 23.9) / 0.45))
        frame = mix_frames(dialog_before, dialog_tooltip, transition)
        move = ease((timestamp - 22.5) / 1.3)
        x = 720 + (143 - 720) * move
        y = 610 + (539 - 610) * move
        click = max(0.0, 1.0 - abs(timestamp - 23.8) / 0.32)
        if timestamp > 25.0:
            play_move = ease((timestamp - 25.0) / 0.9)
            x = 143 + (796 - 143) * play_move
            y = 539 + (585 - 539) * play_move
            click = max(click, max(0.0, 1.0 - abs(timestamp - 26.05) / 0.30))
        frame = add_highlight(frame, (62, 554, 874, 661), timestamp, (238, 183, 48))
        frame = draw_cursor(frame, x, y, click)
        return add_caption(frame, "Word guide", "nuance · /ˈnuː.ɑːns/ · a subtle distinction that adds precision")

    if timestamp < 42.0:
        frame = recordings.copy()
        play_progress = min(1.0, max(0.0, (timestamp - 31.0) / 9.64))
        frame = add_highlight(frame, (36, 302, 1247, 428), timestamp, (91, 92, 226))
        cv2.line(frame, (122, 387), (122 + int(940 * play_progress), 387), (91, 92, 226), 3, cv2.LINE_AA)
        if 30.0 <= timestamp < 32.0:
            x = 660 + (77 - 660) * ease((timestamp - 30.0) / 1.0)
            y = 66 + (365 - 66) * ease((timestamp - 30.0) / 1.0)
            click = max(0.0, 1.0 - abs(timestamp - 31.0) / 0.30)
        else:
            seek = ease((timestamp - 36.0) / 1.2)
            x = 77 + (650 - 77) * seek
            y = 365 + (387 - 365) * seek
            click = max(0.0, 1.0 - abs(timestamp - 37.2) / 0.30)
        frame = draw_cursor(frame, x, y, click)
        if timestamp < 38.5:
            return add_caption(frame, "All recordings", "Replay useful speech with long pauses removed; click the waveform to seek")
        return add_caption(frame, "All recordings", "Open Transcript whenever you want a private, editable local transcription")

    if timestamp < 54.0:
        frame = monologue.copy()
        frame = add_highlight(frame, (650, 198, 1255, 694), timestamp, (91, 92, 226))
        progress = min(1.0, max(0.0, (timestamp - 43.0) / 9.32))
        cv2.line(frame, (698, 684), (698 + int(510 * progress), 684), (91, 92, 226), 4, cv2.LINE_AA)
        x = 1100 + (722 - 1100) * ease((timestamp - 42.0) / 1.0)
        y = 260 + (658 - 260) * ease((timestamp - 42.0) / 1.0)
        click = max(0.0, 1.0 - abs(timestamp - 43.0) / 0.30)
        frame = draw_cursor(frame, x, y, click)
        return add_caption(frame, "Monologue trainer", "Edit recognition, compare a clearer version, and hear the result")

    blurred = cv2.GaussianBlur(monologue, (0, 0), 12)
    frame = cv2.addWeighted(blurred, 0.30, np.full_like(blurred, (45, 33, 88)), 0.70, 0)
    frame = draw_text(frame, (WIDTH // 2, 328), "Your words, remembered.", 44, (255, 255, 255), True, "mm")
    frame = draw_text(frame, (WIDTH // 2, 385), "Your next version, clearer.", 26, (222, 223, 255), False, "mm")
    frame = alpha_rect(frame, (500, 438, 784, 500), (91, 92, 226), 0.97, 16)
    frame = draw_text(frame, (642, 469), "Download Chitchat Buddy", 17, (255, 255, 255), True, "mm")
    return frame


def read_wave(path: Path, target_rate: int) -> np.ndarray:
    with wave.open(str(path), "rb") as source:
        channels = source.getnchannels()
        rate = source.getframerate()
        width = source.getsampwidth()
        frames = source.readframes(source.getnframes())
    if width != 2:
        raise ValueError(f"Expected 16-bit PCM in {path}")
    samples = np.frombuffer(frames, dtype="<i2").astype(np.float32)
    if channels > 1:
        samples = samples.reshape(-1, channels).mean(axis=1)
    samples /= 32768.0
    if rate != target_rate:
        duration = len(samples) / rate
        source_x = np.linspace(0.0, duration, len(samples), endpoint=False)
        target_count = round(duration * target_rate)
        target_x = np.linspace(0.0, duration, target_count, endpoint=False)
        samples = np.interp(target_x, source_x, samples).astype(np.float32)
    return samples


def build_audio(audio_root: Path, output_path: Path) -> None:
    rate = 44100
    mix = np.zeros(round(DURATION * rate), dtype=np.float32)
    clips = [
        (6.0, "user-question.wav", 0.90),
        (10.3, "ai-answer.wav", 0.92),
        (25.3, "word-nuance.wav", 0.94),
        (31.0, "recording-original.wav", 0.88),
        (43.0, "recording-improved.wav", 0.92),
    ]
    for start, name, level in clips:
        samples = read_wave(audio_root / name, rate) * level
        fade = min(len(samples) // 2, round(0.025 * rate))
        if fade:
            samples[:fade] *= np.linspace(0.0, 1.0, fade)
            samples[-fade:] *= np.linspace(1.0, 0.0, fade)
        offset = round(start * rate)
        mix[offset : offset + len(samples)] += samples[: len(mix) - offset]

    for click_time in (4.55, 23.8, 26.05, 31.0, 37.2, 43.0):
        length = round(0.075 * rate)
        envelope = np.exp(-np.linspace(0.0, 7.0, length))
        tone = np.sin(np.linspace(0.0, math.tau * 880 * 0.075, length)) * envelope * 0.12
        offset = round(click_time * rate)
        mix[offset : offset + length] += tone.astype(np.float32)

    peak = float(np.max(np.abs(mix)))
    if peak > 0.97:
        mix *= 0.97 / peak
    pcm = (mix * 32767.0).astype("<i2")
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(output_path), "wb") as destination:
        destination.setnchannels(1)
        destination.setsampwidth(2)
        destination.setframerate(rate)
        destination.writeframes(pcm.tobytes())


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repository", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--work", type=Path, default=Path("H:/BuddyWebsiteCapture/video-work"))
    args = parser.parse_args()
    repository = args.repository.resolve()
    work = args.work.resolve()
    work.mkdir(parents=True, exist_ok=True)

    private_sources = [
        repository / "artifacts/rdp-responsive-dialog2.png",
        repository / "artifacts/verification/waveform-recordings-installed.png",
    ]
    audio_root = Path("H:/BuddyWebsiteCapture/video-audio")
    missing_sources = [path for path in private_sources if not path.exists()]
    missing_sources.extend(audio_root / name for name in (
        "user-question.wav",
        "ai-answer.wav",
        "word-nuance.wav",
        "recording-original.wav",
        "recording-improved.wav",
    ) if not (audio_root / name).exists())
    if missing_sources:
        missing = "\n".join(f"  - {path}" for path in missing_sources)
        raise FileNotFoundError(f"Walkthrough QA sources are missing:\n{missing}")

    choose = load_rgb(repository / "site/screenshots/01-choose-mode.png")
    dialog_tooltip = load_rgb(repository / "site/screenshots/02-dialog-word-guide.png")
    monologue = load_rgb(repository / "site/screenshots/03-monologue-improvement.png")
    dialog_before = build_dialog_before(repository, dialog_tooltip)
    recordings = build_recordings(repository, choose)

    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    silent_video = work / "buddy-walkthrough-silent.mp4"
    audio_mix = work / "buddy-walkthrough-audio.wav"
    final_video = repository / "site/video/buddy-walkthrough.mp4"
    poster = repository / "site/video/buddy-walkthrough-poster.jpg"
    final_video.parent.mkdir(parents=True, exist_ok=True)

    command = [
        ffmpeg,
        "-hide_banner",
        "-loglevel",
        "error",
        "-y",
        "-f",
        "rawvideo",
        "-pix_fmt",
        "bgr24",
        "-s",
        f"{WIDTH}x{HEIGHT}",
        "-r",
        str(FPS),
        "-i",
        "-",
        "-an",
        "-c:v",
        "libx264",
        "-preset",
        "medium",
        "-crf",
        "22",
        "-pix_fmt",
        "yuv420p",
        str(silent_video),
    ]
    encoder = subprocess.Popen(command, stdin=subprocess.PIPE)
    assert encoder.stdin is not None
    poster_frame = None
    try:
        for frame_index in range(round(DURATION * FPS)):
            timestamp = frame_index / FPS
            frame = scene_frame(timestamp, choose, dialog_before, dialog_tooltip, recordings, monologue)
            if poster_frame is None and timestamp >= 25.0:
                poster_frame = frame.copy()
            encoder.stdin.write(frame.tobytes())
    finally:
        encoder.stdin.close()
    if encoder.wait() != 0:
        raise RuntimeError("FFmpeg failed while encoding walkthrough video")

    if poster_frame is None or not cv2.imwrite(str(poster), poster_frame, [cv2.IMWRITE_JPEG_QUALITY, 90]):
        raise RuntimeError("Could not write walkthrough poster")
    build_audio(audio_root, audio_mix)

    subprocess.run(
        [
            ffmpeg,
            "-hide_banner",
            "-loglevel",
            "error",
            "-y",
            "-i",
            str(silent_video),
            "-i",
            str(audio_mix),
            "-c:v",
            "copy",
            "-c:a",
            "aac",
            "-b:a",
            "128k",
            "-movflags",
            "+faststart",
            "-shortest",
            str(final_video),
        ],
        check=True,
    )
    print(final_video)
    print(poster)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
