# Quick Translation Human Speech Test Audio

These WAV files contain **real human speech**, not synthesized/TTS audio. They are intended for repeatable Soundpad, virtual-cable, microphone-capture, and local-ASR tests.

All clips are PCM signed 16-bit little-endian, mono, 16 kHz.

| File | Approx. spoken content | Duration |
|---|---|---:|
| `en_human_hello.wav` | Hello? Hello? Oh, hello. I didn't know you were there. Neither did I. | 4.20 s |
| `en_human_intro.wav` | This is Diane in New Jersey. And I'm Sheila in Texas, originally from Chicago. | 5.33 s |
| `en_human_chicago.wav` | Oh, I'm originally from Chicago also. I'm in New Jersey now though. | 3.79 s |
| `en_human_yankee.wav` | Well, there isn't that much difference. At least you know, they all call me a Yankee down here, so what can I say? | 6.60 s |

## Source and license

The clips were cut from `pyannote.audio`'s bundled `sample/sample.wav` human conversation recording (package version 4.0.4). The upstream package is distributed under the MIT License; a copy is included as `LICENSE-pyannote.audio.txt` in this directory.

The clips are intentionally kept as real conversational speech so they are more useful than deterministic synthetic voices when testing microphone routing and ASR behavior.
