#!/usr/bin/env python3
"""Seed an empty Buddy data root with deterministic website screenshot content."""

from __future__ import annotations

import argparse
import hashlib
import json
import sqlite3
import wave
from datetime import datetime, timedelta, timezone
from pathlib import Path


TICKS_PER_SECOND = 10_000_000
MINSK = timezone(timedelta(hours=3))


def iso(value: datetime) -> str:
    return value.isoformat(timespec="microseconds")


def sha256_text(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def create_silent_wave(path: Path, seconds: float = 0.25) -> tuple[int, str]:
    path.parent.mkdir(parents=True, exist_ok=True)
    sample_rate = 16_000
    frame_count = round(sample_rate * seconds)
    with wave.open(str(path), "wb") as stream:
        stream.setnchannels(1)
        stream.setsampwidth(2)
        stream.setframerate(sample_rate)
        stream.writeframes(b"\0\0" * frame_count)
    payload = path.read_bytes()
    return len(payload), hashlib.sha256(payload).hexdigest()


def insert_setting(
    connection: sqlite3.Connection,
    key: str,
    value: str,
    updated_at: datetime,
) -> None:
    connection.execute(
        "INSERT INTO app_setting (key, value_json, updated_at) VALUES (?, ?, ?)",
        (key, json.dumps(value), iso(updated_at)),
    )


def insert_recording(
    connection: sqlite3.Connection,
    *,
    recording_id: str,
    kind: int,
    started_at: datetime,
    duration_seconds: int,
    speech_seconds: int,
    title: str,
) -> None:
    connection.execute(
        """
        INSERT INTO recording (
            id, kind, created_at, capture_started_at, capture_ended_at,
            wall_duration_ticks, speech_duration_ticks, input_device_id,
            status, display_title, generated_title, last_error_code,
            last_error_message, deleted_at, version
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, 7, ?, ?, NULL, NULL, NULL, 4)
        """,
        (
            recording_id,
            kind,
            iso(started_at),
            iso(started_at),
            iso(started_at + timedelta(seconds=duration_seconds)),
            duration_seconds * TICKS_PER_SECOND,
            speech_seconds * TICKS_PER_SECOND,
            "Default microphone",
            title,
            title,
        ),
    )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--data-root", required=True, type=Path)
    args = parser.parse_args()

    data_root = args.data_root.resolve()
    database = data_root / "buddy.db"
    if not database.is_file():
        raise SystemExit(f"Buddy database does not exist: {database}")

    dialog_recording_id = "11111111-1111-4111-8111-111111111111"
    trainer_recording_id = "22222222-2222-4222-8222-222222222222"
    dialog_session_id = "33333333-3333-4333-8333-333333333333"
    user_message_id = "44444444-4444-4444-8444-444444444444"
    assistant_message_id = "55555555-5555-4555-8555-555555555555"
    source_revision_id = "66666666-6666-4666-8666-666666666666"
    improved_revision_id = "77777777-7777-4777-8777-777777777777"
    original_artifact_id = "88888888-8888-4888-8888-888888888888"
    generated_artifact_id = "99999999-9999-4999-8999-999999999999"

    dialog_started = datetime(2026, 8, 11, 10, 8, tzinfo=MINSK)
    trainer_started = datetime(2026, 8, 11, 10, 22, tzinfo=MINSK)
    seeded_at = datetime(2026, 8, 11, 10, 30, tzinfo=MINSK)

    user_text = "How can I sound confident without sounding rehearsed?"
    assistant_text = (
        "Lead with the **outcome**, then give one concrete reason.\n\n"
        "A little **nuance** makes you sound thoughtful, not uncertain: "
        "acknowledge the trade-off and finish with a clear recommendation."
    )
    source_text = (
        "I think we should probably launch the simple version first because "
        "it will let us to understand what users actually need, and then later "
        "we can improve the complicated parts."
    )
    improved_text = (
        "I recommend launching the simpler version first. It will show us what "
        "users actually need, so we can improve the complex parts with real evidence."
    )

    relative_directory = Path("2026") / "08" / trainer_recording_id
    original_relative = relative_directory / "practice-take.wav"
    generated_relative = relative_directory / "better-version.wav"
    original_length, original_hash = create_silent_wave(
        data_root / "recordings" / original_relative
    )
    generated_length, generated_hash = create_silent_wave(
        data_root / "recordings" / generated_relative
    )

    with sqlite3.connect(database) as connection:
        connection.execute("PRAGMA foreign_keys = ON")
        count = connection.execute("SELECT COUNT(*) FROM recording").fetchone()[0]
        if count:
            raise SystemExit("The screenshot fixture requires an empty Buddy library.")

        for key, value in (
            ("onboarding.completed", "true"),
            ("language.interface-id", "en"),
            ("language.dialog-id", "en"),
            ("language.provider-id", "buddy-proxy"),
            ("dialog.allowed-pause-milliseconds", "3000"),
        ):
            insert_setting(connection, key, value, seeded_at)

        insert_recording(
            connection,
            recording_id=dialog_recording_id,
            kind=2,
            started_at=dialog_started,
            duration_seconds=52,
            speech_seconds=31,
            title="Speaking with clarity",
        )
        insert_recording(
            connection,
            recording_id=trainer_recording_id,
            kind=1,
            started_at=trainer_started,
            duration_seconds=24,
            speech_seconds=18,
            title="A clearer product recommendation",
        )

        connection.execute(
            """
            INSERT INTO dialog_session (
                id, recording_id, status, started_at, ended_at,
                system_instruction, provider, model, last_error, version
            ) VALUES (?, ?, 2, ?, ?, ?, ?, ?, NULL, 2)
            """,
            (
                dialog_session_id,
                dialog_recording_id,
                iso(dialog_started),
                iso(dialog_started + timedelta(seconds=52)),
                "Be concise, supportive, and speaker-aware.",
                "Buddy free DeepSeek",
                "deepseek-chat",
            ),
        )
        connection.executemany(
            """
            INSERT INTO dialog_message (
                id, session_id, sequence, role, text, created_at,
                provider, model, latency_ticks, prompt_tokens,
                completion_tokens, audio_artifact_id
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, NULL)
            """,
            (
                (
                    user_message_id,
                    dialog_session_id,
                    0,
                    0,
                    user_text,
                    iso(dialog_started + timedelta(seconds=9)),
                    "Whisper.net",
                    "large-v3-turbo",
                    None,
                    None,
                    None,
                ),
                (
                    assistant_message_id,
                    dialog_session_id,
                    1,
                    1,
                    assistant_text,
                    iso(dialog_started + timedelta(seconds=12)),
                    "Buddy free DeepSeek",
                    "deepseek-chat",
                    12_000_000,
                    164,
                    42,
                ),
            ),
        )

        user_words = [
            ("How", 0.94),
            ("can", 0.96),
            ("I", 0.99),
            ("sound", 0.93),
            ("confident", 0.84),
            ("without", 0.90),
            ("sounding", 0.88),
            ("rehearsed", 0.67),
        ]
        connection.execute(
            """
            INSERT INTO dialog_pronunciation_assessment (
                message_id, transcript, phonetic_transcript, created_at,
                model, schema_version
            ) VALUES (?, ?, ?, ?, ?, ?)
            """,
            (
                user_message_id,
                user_text,
                "haʊ kæn aɪ saʊnd ˈkɑnfədənt wɪˈðaʊt ˈsaʊndɪŋ rɪˈhɜrst",
                iso(dialog_started + timedelta(seconds=10)),
                "large-v3-turbo",
                "buddy.pronunciation.v1",
            ),
        )
        connection.executemany(
            """
            INSERT INTO dialog_pronunciation_word (
                message_id, sequence, text, start_ticks, end_ticks, confidence
            ) VALUES (?, ?, ?, ?, ?, ?)
            """,
            [
                (
                    user_message_id,
                    index,
                    word,
                    index * 4_200_000,
                    index * 4_200_000 + 3_600_000,
                    confidence,
                )
                for index, (word, confidence) in enumerate(user_words)
            ],
        )

        connection.executemany(
            """
            INSERT INTO transcript_revision (
                id, recording_id, parent_revision_id, kind, text,
                content_sha256, created_at, provider, model,
                schema_version, is_current
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                (
                    source_revision_id,
                    trainer_recording_id,
                    None,
                    0,
                    source_text,
                    sha256_text(source_text),
                    iso(trainer_started + timedelta(seconds=26)),
                    "Whisper.net",
                    "large-v3-turbo",
                    "buddy.transcript.v1",
                    0,
                ),
                (
                    improved_revision_id,
                    trainer_recording_id,
                    source_revision_id,
                    3,
                    improved_text,
                    sha256_text(improved_text),
                    iso(trainer_started + timedelta(seconds=31)),
                    "Buddy free DeepSeek",
                    "deepseek-chat",
                    "buddy.transcript.v1",
                    1,
                ),
            ),
        )

        trainer_words = [
            ("I", 0.99),
            ("think", 0.91),
            ("we", 0.96),
            ("should", 0.89),
            ("probably", 0.49),
            ("launch", 0.91),
            ("the", 0.97),
            ("simple", 0.82),
            ("version", 0.90),
            ("first", 0.93),
            ("because", 0.72),
            ("understand", 0.53),
            ("actually", 0.68),
            ("complicated", 0.51),
        ]
        connection.execute(
            """
            INSERT INTO pronunciation_assessment (
                recording_id, transcript, created_at, model,
                schema_version, phonetic_transcript
            ) VALUES (?, ?, ?, ?, ?, ?)
            """,
            (
                trainer_recording_id,
                source_text,
                iso(trainer_started + timedelta(seconds=27)),
                "large-v3-turbo",
                "buddy.pronunciation.v1",
                "aɪ θɪŋk wi ʃʊd ˈprɑbəbli lɔntʃ ðə ˈsɪmpəl ˈvɜrʒən fɜrst",
            ),
        )
        connection.executemany(
            """
            INSERT INTO pronunciation_word (
                recording_id, sequence, text, start_ticks, end_ticks, confidence
            ) VALUES (?, ?, ?, ?, ?, ?)
            """,
            [
                (
                    trainer_recording_id,
                    index,
                    word,
                    index * 5_200_000,
                    index * 5_200_000 + 4_400_000,
                    confidence,
                )
                for index, (word, confidence) in enumerate(trainer_words)
            ],
        )

        connection.executemany(
            """
            INSERT INTO audio_artifact (
                id, recording_id, kind, relative_path, container,
                sample_rate, channels, duration_ticks, byte_length,
                sha256, generator, created_at
            ) VALUES (?, ?, ?, ?, 0, 16000, 1, ?, ?, ?, ?, ?)
            """,
            (
                (
                    original_artifact_id,
                    trainer_recording_id,
                    1,
                    str(original_relative),
                    18 * TICKS_PER_SECOND,
                    original_length,
                    original_hash,
                    "Buddy compact speech",
                    iso(trainer_started + timedelta(seconds=25)),
                ),
                (
                    generated_artifact_id,
                    trainer_recording_id,
                    2,
                    str(generated_relative),
                    14 * TICKS_PER_SECOND,
                    generated_length,
                    generated_hash,
                    "Kokoro local voice",
                    iso(trainer_started + timedelta(seconds=33)),
                ),
            ),
        )

        connection.commit()

    print(f"Seeded Buddy website demo data at {data_root}")


if __name__ == "__main__":
    main()
