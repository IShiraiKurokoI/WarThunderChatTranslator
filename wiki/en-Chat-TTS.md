# Chat Voice Playback

[⌂ Back to English guide](en-Home) · [Previous: Voice Translation](en-Voice) · [Next: Content Filtering](en-Content-Filter)

> Read translated chat aloud so you can spend less time looking away from the game.

---

## Voice source

Choose between:

- **Microsoft SpeechSynthesizer** — uses voices installed in Windows and synthesizes locally with low overhead.
- **Local model (Kokoro v1.1)** — uses the bundled Kokoro v1.1 INT8 model through sherpa-onnx and offers multiple Chinese and English voices.
- **GPT-SoVITS character voice** — connects to a GPT-SoVITS API service you already started and lets you save a model/reference-audio profile for each character. The app does not install or start GPT-SoVITS itself.

Kokoro is bundled with the app. There is no in-app model download, import, or delete workflow. If the packaged model is missing, the Model Status card reports it and voice selection is disabled.

For GPT-SoVITS, start its API service first (default `http://127.0.0.1:9880`), then add a character profile. GPT and SoVITS model paths are optional; when left blank, the models already loaded by GPT-SoVITS are used. Reference audio is required.

## Playback settings

Choose a voice, speaking rate, and volume, then use Preview to check the result.

## Message queue

- **Maximum queued messages** controls how many not-yet-spoken messages are retained.
- **When a message waits too long** can be set to Never Expire or a custom number of seconds.
- When the queue is full, older waiting messages are discarded first so stale battle information does not keep playing.

Friendly, enemy, and system messages can be enabled separately. [Content Filtering](en-Content-Filter) can also apply a separate TTS-only filter without changing text shown on the Dashboard or Overlay.

---

[← Previous: Voice Translation](en-Voice) · [Back to English guide](en-Home) · [Next: Content Filtering →](en-Content-Filter)
