import asyncio
import csv
import os
import edge_tts

VOICE = "en-US-AriaNeural"

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
WORDS_DIR = os.path.join(BASE_DIR, "words")
EXAMPLES_DIR = os.path.join(BASE_DIR, "examples")
CSV_FILE = os.path.join(BASE_DIR, "vocabulary.csv")


def safe_filename(word):
    return (
        word.strip()
        .lower()
        .replace(" ", "-")
        .replace("/", "-")
    )


async def save_audio(text, path):
    if os.path.exists(path):
        print("Skip:", os.path.basename(path))
        return

    communicate = edge_tts.Communicate(text, VOICE)
    await communicate.save(path)
    print("Created:", os.path.basename(path))


async def main():
    os.makedirs(WORDS_DIR, exist_ok=True)
    os.makedirs(EXAMPLES_DIR, exist_ok=True)

    with open(CSV_FILE, "r", encoding="utf-8-sig") as file:
        reader = csv.DictReader(file)

        for row in reader:
            word = row["word"].strip()
            example = row["example"].strip()

            if not word or not example:
                continue

            filename = safe_filename(word)

            await save_audio(
                word,
                os.path.join(WORDS_DIR, f"{filename}.mp3")
            )

            await save_audio(
                example,
                os.path.join(EXAMPLES_DIR, f"{filename}-example.mp3")
            )


asyncio.run(main())