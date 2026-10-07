import asyncio
import csv
import os
import edge_tts

VOICE = "en-US-AriaNeural"

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
AUDIO_DIR = os.path.join(BASE_DIR, "audio")
CSV_FILE = os.path.join(BASE_DIR, "audio.csv")

PAUSE = "800ms"


def convert_to_ssml(text):
    # Thay [PAUSE] bằng khoảng nghỉ 0.8 giây
    parts = text.split("[PAUSE]")

    ssml = "<speak>"

    for i, part in enumerate(parts):
        ssml += part.strip()

        if i < len(parts) - 1:
            ssml += f'<break time="{PAUSE}"/>'

    ssml += "</speak>"

    return ssml


async def save_audio(content, path):
    if os.path.exists(path):
        print("Skip:", os.path.basename(path))
        return

    ssml = convert_to_ssml(content)

    communicate = edge_tts.Communicate(
        ssml,
        VOICE
    )

    await communicate.save(path)

    print("Created:", os.path.basename(path))


async def main():
    os.makedirs(AUDIO_DIR, exist_ok=True)

    with open(CSV_FILE, "r", encoding="utf-8-sig") as file:
        reader = csv.DictReader(file)

        for row in reader:
            content = row["content"].strip()
            filename = row["filename"].strip()

            if not content or not filename:
                continue

            if not filename.lower().endswith(".mp3"):
                filename += ".mp3"

            audio_path = os.path.join(
                AUDIO_DIR,
                filename
            )

            await save_audio(
                content,
                audio_path
            )


asyncio.run(main())