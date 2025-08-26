<template>
  <div class="whisper-demo">
    <h2>Whisper Speech-to-Text Demo</h2>
    
    <div class="section">
      <h3>Model Configuration</h3>
      <div class="input-group">
        <label for="model-path">Whisper Model Path:</label>
        <input 
          id="model-path"
          v-model="modelPath" 
          type="text" 
          placeholder="/path/to/whisper-model.bin"
          class="input-field"
        />
        <button @click="checkModel" :disabled="!modelPath" class="btn">
          Check Model
        </button>
      </div>
      <div v-if="modelStatus" class="status" :class="{ valid: modelValid, invalid: !modelValid }">
        {{ modelStatus }}
      </div>
    </div>

    <div class="section">
      <h3>Audio Transcription</h3>
      <div class="input-group">
        <label for="audio-path">Audio File Path:</label>
        <input 
          id="audio-path"
          v-model="audioPath" 
          type="text" 
          placeholder="/path/to/audio.wav"
          class="input-field"
        />
        <button 
          @click="transcribeAudio" 
          :disabled="!audioPath || !modelPath || !modelValid || isTranscribing" 
          class="btn primary"
        >
          {{ isTranscribing ? 'Transcribing...' : 'Transcribe' }}
        </button>
      </div>
    </div>

    <div class="section" v-if="transcription || error">
      <h3>Results</h3>
      <div v-if="error" class="error">
        {{ error }}
      </div>
      <div v-if="transcription" class="transcription">
        <h4>Transcription:</h4>
        <p>{{ transcription }}</p>
      </div>
    </div>

    <div class="section info">
      <h3>Setup Instructions</h3>
      <ol>
        <li>Download a Whisper model (e.g., from <a href="https://huggingface.co/ggerganov/whisper.cpp" target="_blank">Hugging Face</a>)</li>
        <li>Place the model file (e.g., ggml-base.en.bin) in a known location</li>
        <li>Enter the full path to the model file above</li>
        <li>Provide an audio file path (currently supports WAV format planning)</li>
        <li>Click "Transcribe" to convert speech to text</li>
      </ol>
      
      <div class="note">
        <strong>Note:</strong> This is a demo implementation. For production use:
        <ul>
          <li>Add proper audio format support (MP3, M4A, etc.)</li>
          <li>Implement file dialogs for easier file selection</li>
          <li>Add progress indicators for long transcriptions</li>
          <li>Handle different model sizes and languages</li>
        </ul>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { whisperService } from '../whisper-service'

const modelPath = ref('')
const audioPath = ref('')
const modelStatus = ref('')
const modelValid = ref(false)
const transcription = ref('')
const error = ref('')
const isTranscribing = ref(false)

const checkModel = async () => {
  if (!modelPath.value) return
  
  try {
    modelStatus.value = 'Checking model...'
    const isValid = await whisperService.checkWhisperModel(modelPath.value)
    modelValid.value = isValid
    modelStatus.value = isValid ? 'Model is valid ✓' : 'Invalid model file ✗'
  } catch (err) {
    modelValid.value = false
    modelStatus.value = `Error checking model: ${err}`
  }
}

const transcribeAudio = async () => {
  if (!audioPath.value || !modelPath.value) return
  
  error.value = ''
  transcription.value = ''
  isTranscribing.value = true
  
  try {
    const result = await whisperService.transcribeAudio(audioPath.value, modelPath.value)
    transcription.value = result
  } catch (err) {
    error.value = `Transcription failed: ${err}`
  } finally {
    isTranscribing.value = false
  }
}
</script>

<style scoped>
.whisper-demo {
  max-width: 800px;
  margin: 0 auto;
  padding: 20px;
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
}

.section {
  margin-bottom: 30px;
  padding: 20px;
  border: 1px solid #e0e0e0;
  border-radius: 8px;
}

.section h3 {
  margin-top: 0;
  color: #333;
}

.input-group {
  display: flex;
  gap: 10px;
  align-items: center;
  margin-bottom: 10px;
  flex-wrap: wrap;
}

.input-group label {
  min-width: 150px;
  font-weight: 500;
}

.input-field {
  flex: 1;
  min-width: 250px;
  padding: 8px 12px;
  border: 1px solid #ccc;
  border-radius: 4px;
  font-size: 14px;
}

.btn {
  padding: 8px 16px;
  border: 1px solid #ccc;
  border-radius: 4px;
  background: white;
  cursor: pointer;
  font-size: 14px;
}

.btn:hover:not(:disabled) {
  background: #f5f5f5;
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn.primary {
  background: #007acc;
  color: white;
  border-color: #007acc;
}

.btn.primary:hover:not(:disabled) {
  background: #0066aa;
}

.status {
  padding: 8px 12px;
  border-radius: 4px;
  font-size: 14px;
}

.status.valid {
  background: #d4edda;
  color: #155724;
  border: 1px solid #c3e6cb;
}

.status.invalid {
  background: #f8d7da;
  color: #721c24;
  border: 1px solid #f5c6cb;
}

.error {
  padding: 12px;
  background: #f8d7da;
  color: #721c24;
  border: 1px solid #f5c6cb;
  border-radius: 4px;
  margin-bottom: 10px;
}

.transcription {
  padding: 12px;
  background: #d4edda;
  border: 1px solid #c3e6cb;
  border-radius: 4px;
}

.transcription h4 {
  margin-top: 0;
  color: #155724;
}

.transcription p {
  margin-bottom: 0;
  white-space: pre-wrap;
  font-family: monospace;
}

.info {
  background: #f8f9fa;
}

.info ol, .info ul {
  margin: 10px 0;
  padding-left: 20px;
}

.info li {
  margin: 5px 0;
}

.note {
  margin-top: 15px;
  padding: 10px;
  background: #fff3cd;
  border: 1px solid #ffeaa7;
  border-radius: 4px;
}

.note ul {
  margin: 10px 0 0 0;
}
</style>
