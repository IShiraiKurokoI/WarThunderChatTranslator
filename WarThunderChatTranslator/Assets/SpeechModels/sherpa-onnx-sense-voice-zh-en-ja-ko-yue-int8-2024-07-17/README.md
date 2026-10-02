# SenseVoice INT8 model placeholder

Expected files in this directory before Build/Publish/MSIX:

- `model.int8.onnx`
- `tokens.txt`

Model: `sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17`.
It supports Mandarin Chinese, Cantonese, English, Japanese, and Korean through sherpa-onnx.
The app uses automatic language detection, inverse text normalization, CPU provider, and one inference thread.

`model.int8.onnx` is tracked with Git LFS. Ensure Git LFS objects are pulled before Build/Publish/MSIX.
