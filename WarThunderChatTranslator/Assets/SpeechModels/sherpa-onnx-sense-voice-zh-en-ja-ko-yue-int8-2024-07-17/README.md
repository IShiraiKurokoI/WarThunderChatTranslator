# SenseVoiceSmall INT8 ONNX model

This directory contains the files used by WarThunderChatTranslator for local/offline speech recognition.

## Model

- Packaged artifact: `sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17`
- Converted model repository: `csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17`
- Upstream model: `iic/SenseVoiceSmall` / SenseVoice
- Original model project / author organization: FunAudioLLM (Alibaba Group / FunASR ecosystem)
- ONNX conversion / artifact maintainer: Fangjun Kuang (`csukuangfj`) / k2-fsa sherpa-onnx
- Model file: `model.int8.onnx`
- Token file: `tokens.txt`
- Quantization: INT8
- Audio: 16 kHz
- Languages: Mandarin Chinese, Cantonese, English, Japanese, and Korean
- App inference settings: automatic language detection, inverse text normalization, CPU provider, one inference thread

The sherpa-onnx model artifact dated 2024-07-17 was converted from `iic/SenseVoiceSmall`. Fangjun Kuang (`csukuangfj`) is credited here for the ONNX conversion / published sherpa-onnx artifact; this does **not** mean that he is the original author of SenseVoiceSmall.

## Required files before Build / Publish / MSIX

- `model.int8.onnx`
- `tokens.txt`

`model.int8.onnx` is tracked with Git LFS in this project. Ensure the LFS object is present before Build/Publish/MSIX. The model file is intentionally not duplicated in source archives that omit large LFS objects.

## License and attribution

The model weights and the inference runtime have **different licenses**:

- **SenseVoiceSmall model weights / this converted model artifact:** **FunASR Model Open Source License Agreement v1.1**. Follow the controlling terms referenced by the upstream model and by the `LICENSE` file shipped with the sherpa-onnx model artifact. Preserve any required attribution and model-name notices when using or redistributing the model.
- **sherpa-onnx runtime:** Apache License 2.0.
- **WarThunderChatTranslator:** governed separately by this repository's own license. The app license does not replace the model-weight license.

Do **not** describe the SenseVoiceSmall weights as Apache-2.0 merely because sherpa-onnx itself is Apache-2.0. The model terms and runtime terms are separate. Before redistribution or commercial release, verify the current upstream model-license text in case it has changed.

The `LICENSE` file in this model directory is intentionally kept as the pointer distributed with the upstream sherpa-onnx model artifact.

### Upstream references

- SenseVoice project: https://github.com/QwenAudio/SenseVoice
- SenseVoiceSmall model card: https://www.modelscope.cn/models/iic/SenseVoiceSmall
- Converted model repository: https://huggingface.co/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17
- sherpa-onnx SenseVoice model documentation: https://k2-fsa.github.io/sherpa/onnx/sense-voice/pretrained.html
- FunASR model license: https://github.com/modelscope/FunASR/blob/main/MODEL_LICENSE
- sherpa-onnx: https://github.com/k2-fsa/sherpa-onnx

## Integrity

The application currently expects:

- `model.int8.onnx` SHA-256: `c71f0ce00bec95b07744e116345e33d8cbbe08cef896382cf907bf4b51a2cd51`

The application also rejects obviously missing/partial model files during its prerequisite check.
