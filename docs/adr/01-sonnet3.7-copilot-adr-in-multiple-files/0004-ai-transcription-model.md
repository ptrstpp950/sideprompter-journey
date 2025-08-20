# Local AI Model for Audio Transcription Selection

* Status: proposed
* Deciders: [list everyone involved in the decision]
* Date: 2025-08-19

## Context and Problem Statement

Our application needs to transcribe audio to text using AI models that run locally on the user's machine. We need to select the most appropriate model that balances accuracy, performance, resource usage, and integration with our technology stack.

## Decision Drivers

* Transcription accuracy
* Processing speed on consumer hardware
* Resource usage (memory, CPU/GPU)
* Ease of integration with Rust/Tauri
* Cross-platform compatibility
* Model size and distribution considerations
* Open-source availability and licensing
* Support for multiple languages (if required)

## Considered Options

* OpenAI Whisper (via local implementation)
* Mozilla DeepSpeech
* Vosk
* Silero
* Nvidia Riva (locally deployed)

## Decision Outcome

Chosen option: "OpenAI Whisper with whisper.cpp", because it provides superior transcription quality, wide language support, acceptable performance on consumer hardware via optimized C++ implementation, and can be effectively integrated with our Rust backend.

### Positive Consequences

* State-of-the-art transcription accuracy
* Support for multiple languages
* Active community development and improvements
* Optimized implementation available via whisper.cpp
* Straightforward integration with Rust via FFI
* Reasonable performance on consumer hardware
* Clear open-source licensing (MIT)

### Negative Consequences

* Larger model sizes compared to some alternatives
* Higher resource requirements than lighter models
* May require optimization for real-time performance
* Requires distributing model files with the application or downloading on first use

## Pros and Cons of the Options

### OpenAI Whisper (via local implementation)

* Good, because it has superior transcription accuracy across languages
* Good, because it provides robust performance in noisy environments
* Good, because it has wide language support
* Good, because it offers optimized implementations (whisper.cpp) for performance
* Good, because it has clear MIT licensing
* Good, because it has strong community support and continuous improvements
* Bad, because it requires larger model files (multiple sizes available)
* Bad, because it has higher resource requirements than some alternatives
* Bad, because it may struggle with real-time transcription without optimization

### Mozilla DeepSpeech

* Good, because it has open-source implementation with Apache 2.0 license
* Good, because it provides good integration with Rust ecosystem
* Good, because it has smaller model size options
* Good, because it offers established project with documentation
* Bad, because it has lower accuracy than Whisper
* Bad, because it has limited language support
* Bad, because it has reduced community development (project less active)
* Bad, because it may struggle with noisy audio environments

### Vosk

* Good, because it focuses on real-time performance
* Good, because it has smaller footprint models available
* Good, because it supports multiple languages
* Good, because it offers offline operation
* Bad, because it has lower transcription accuracy than Whisper
* Bad, because it has less active development compared to alternatives
* Bad, because it has less documentation for Rust integration
* Bad, because it may require more work for tight integration with our stack

### Silero

* Good, because it has good balance of speed and accuracy
* Good, because it provides efficient models for edge devices
* Good, because it supports multiple languages
* Good, because it offers multiple model sizes
* Bad, because it has less community adoption than Whisper
* Bad, because it has more complex licensing terms
* Bad, because it has less documentation for Rust integration
* Bad, because it may have lower accuracy in challenging audio conditions

### Nvidia Riva (locally deployed)

* Good, because it has high performance with GPU acceleration
* Good, because it offers real-time capabilities
* Good, because it provides high accuracy
* Good, because it has good documentation and support
* Bad, because it requires NVIDIA GPUs for optimal performance
* Bad, because it has higher complexity for local deployment
* Bad, because it has larger resource footprint
* Bad, because it may have licensing limitations for distribution

## Links

* [OpenAI Whisper GitHub](https://github.com/openai/whisper)
* [whisper.cpp Optimization](https://github.com/ggerganov/whisper.cpp)
* [Mozilla DeepSpeech](https://github.com/mozilla/DeepSpeech)
* [Vosk Speech Recognition Toolkit](https://alphacephei.com/vosk/)
* [Silero Models](https://github.com/snakers4/silero-models)
* [Nvidia Riva](https://developer.nvidia.com/riva)
