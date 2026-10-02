# Quick Translation Test Audio

These synthetic WAV files are intended for repeatable microphone/STT testing with Soundpad or a virtual audio input.
All files are PCM signed 16-bit little-endian, mono, 16 kHz.

| File | Language | Expected text |
|---|---|---|
| `zh_short.wav` | Chinese | 敌人在右边。 |
| `zh_game.wav` | Chinese | 敌方坦克在右边山后面，我需要A点支援。 |
| `zh_long.wav` | Chinese | 注意侧翼，两辆敌方坦克正在推进B点，请掩护左侧。 |
| `en_short.wav` | English | Enemy on the right side. |
| `en_game.wav` | English | Enemy tank behind the hill on the right. I need backup at point A. |
| `en_mixed.wav` | English | Attention team. Two enemies are pushing point B. Cover the left flank. |

The voices are synthetic and intentionally consistent; real microphone speech may produce different ASR accuracy.
