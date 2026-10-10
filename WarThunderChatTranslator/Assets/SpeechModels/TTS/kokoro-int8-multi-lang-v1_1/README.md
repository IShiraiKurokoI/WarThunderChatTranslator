# Kokoro v1.1 INT8 TTS Model

This directory contains the bundled Kokoro text-to-speech model used by WarThunderChatTranslator for lightweight local speech synthesis.

## Model information

- **Package name:** `kokoro-int8-multi-lang-v1_1`
- **Model family:** Kokoro 82M v1.1 Chinese/English
- **Runtime:** sherpa-onnx
- **Format:** ONNX, INT8 quantized
- **Languages:** Chinese and English
- **Speakers:** 103
- **Purpose in this application:** Local chat text-to-speech playback
- **Network requirement:** None during synthesis

## Upstream sources

- **sherpa-onnx model package:** https://github.com/k2-fsa/sherpa-onnx/releases/tag/tts-models
- **sherpa model documentation:** https://github.com/k2-fsa/sherpa/blob/master/docs/source/onnx/tts/pretrained_models/kokoro.rst
- **Upstream Kokoro model:** https://huggingface.co/hexgrad/Kokoro-82M-v1.1-zh

## Distributed model package

The bundled sherpa-onnx package is:

`kokoro-int8-multi-lang-v1_1.tar.bz2`

Archive SHA-256:

`a1e94694776049035c4f2c6529f003aaece993c76aae9a78995831c3c4dcafc6`

The runtime expects the model directory to contain the ONNX model and its associated voice, tokenization, pronunciation, and text-normalization resources from the official sherpa-onnx package.

## License

The model package is distributed under the **Apache License 2.0**. The `LICENSE` file in this directory is kept as the unmodified Apache-2.0 license file distributed by the upstream sherpa-onnx Kokoro export package.

The `[yyyy] [name of copyright owner]` text near the end of `LICENSE` is part of the official Apache License 2.0 Appendix and is not a project-specific placeholder.

