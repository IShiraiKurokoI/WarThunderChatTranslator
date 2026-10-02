# Local ASR human-speech test audio

This directory is for repeatable real-human-speech ASR and Soundpad tests.

English clips already stored in the repository:

| File | Approx. spoken content | Duration |
|---|---|---:|
| `en_human_hello.wav` | Hello? Hello? Oh, hello. I didn't know you were there. Neither did I. | 4.20 s |
| `en_human_intro.wav` | This is Diane in New Jersey. And I'm Sheila in Texas, originally from Chicago. | 5.33 s |
| `en_human_chicago.wav` | Oh, I'm originally from Chicago also. I'm in New Jersey now though. | 3.79 s |
| `en_human_yankee.wav` | Well, there isn't that much difference... they all call me a Yankee down here... | 6.60 s |

Mandarin human clips expected in this same directory:

| File | Spoken content | Source |
|---|---|---|
| `zh_human_nihao.wav` | 你好 | Lingua Libre / Wikimedia Commons |
| `zh_human_kebukeyi.wav` | 可不可以 | Lingua Libre / Wikimedia Commons |
| `zh_human_zhongguocai.wav` | 中国菜 | Lingua Libre / Wikimedia Commons |

The Mandarin recordings are actual Lingua Libre speaker recordings by `Luilui6666`, not TTS. Their Commons pages offer CC0 1.0 as a licensing option. See `LICENSE-LinguaLibre-CC0.txt`.

The four English clips were cut from `pyannote.audio`'s bundled `sample/sample.wav` human conversation recording (package version 4.0.4). See `LICENSE-pyannote.audio.txt`.
