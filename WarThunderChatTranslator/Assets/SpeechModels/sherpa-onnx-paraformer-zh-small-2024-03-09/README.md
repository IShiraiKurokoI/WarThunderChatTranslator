# Paraformer zh-en small INT8 model

Runtime files expected in this directory:

- `model.int8.onnx` (about 79 MiB / 81.8 MB on Hugging Face)
- `tokens.txt`

Model: `csukuangfj/sherpa-onnx-paraformer-zh-small-2024-03-09`.
It is a bilingual Chinese + English offline Paraformer model used through sherpa-onnx.
The source checkpoint is Alibaba DAMO Academy / FunASR `speech_paraformer_asr_nat-zh-cn-16k-common-vocab8358-onnx` (Apache-2.0); the ONNX export is distributed by the sherpa-onnx project.

Expected SHA-256 for `model.int8.onnx`:

`3ef6c19369b912f7caf3cef8e545c5ccd1a33d9d7ec792a46668dc41c4b229ec`

Run `DownloadSpeechModel.ps1` from the repository root before building if the two runtime files are not already present. They are copied into build/publish output by the application project.
