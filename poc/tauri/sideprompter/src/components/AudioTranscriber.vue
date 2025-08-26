<template>
  <div class="audio-transcription">
    <h2>Audio Transcription with Whisper</h2>
    
    <!-- Model Management -->
    <div class="model-section">
      <h3>Whisper Model</h3>
      <div class="model-controls">
        <select v-model="selectedModel">
          <option value="tiny">Tiny (39 MB)</option>
          <option value="base">Base (142 MB)</option>
          <option value="small">Small (244 MB)</option>
          <option value="medium">Medium (769 MB)</option>
        </select>
        <button @click="downloadModel" :disabled="downloading">
          {{ downloading ? 'Downloading...' : 'Download Model' }}
        </button>
      </div>
      <div v-if="modelPath" class="model-info">
        ✅ Model ready: {{ modelPath }}
      </div>
    </div>

    <!-- Audio Devices -->
    <div class="devices-section">
      <h3>Audio Devices</h3>
      <button @click="listDevices">Refresh Devices</button>
      <ul v-if="devices.length">
        <li v-for="device in devices" :key="device">{{ device }}</li>
      </ul>
    </div>

    <!-- File Transcription -->
    <div class="file-section">
      <h3>Transcribe Audio File</h3>
      <input 
        type="file" 
        @change="selectFile" 
        accept=".wav,.mp3,.m4a,.ogg,.flac"
        ref="fileInput"
      />
      <button @click="transcribeFile" :disabled="!selectedFile || !modelPath || transcribing">
        {{ transcribing ? 'Transcribing...' : 'Transcribe' }}
      </button>
    </div>

    <!-- Results -->
    <div class="results-section" v-if="transcriptionResult">
      <h3>Transcription Result</h3>
      <div class="result-text">{{ transcriptionResult }}</div>
    </div>

    <!-- Live Transcription (Future Feature) -->
    <div class="live-section">
      <h3>Live Transcription</h3>
      <p class="info">Live microphone and speaker transcription will be implemented with real-time audio capture.</p>
      <button @click="testAudioCapture">Test Audio Capture</button>
    </div>

    <!-- Status/Logs -->
    <div class="status-section" v-if="status">
      <h3>Status</h3>
      <div class="status-text" :class="{ error: isError }">{{ status }}</div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { invoke } from '@tauri-apps/api/core'

const selectedModel = ref('tiny')
const downloading = ref(false)
const modelPath = ref('')
const devices = ref<string[]>([])
const selectedFile = ref<File | null>(null)
const transcribing = ref(false)
const transcriptionResult = ref('')
const status = ref('')
const isError = ref(false)
const fileInput = ref<HTMLInputElement>()

const modelsDir = 'models' // You might want to use a proper app data directory

onMounted(() => {
  listDevices()
  checkExistingModel()
})

async function downloadModel() {
  if (downloading.value) return
  
  downloading.value = true
  isError.value = false
  status.value = `Downloading ${selectedModel.value} model...`
  
  try {
    const path = await invoke<string>('download_whisper_model', {
      modelType: selectedModel.value,
      modelsDir: modelsDir
    })
    modelPath.value = path
    status.value = `Model downloaded successfully: ${path}`
  } catch (error) {
    status.value = `Error downloading model: ${error}`
    isError.value = true
  } finally {
    downloading.value = false
  }
}

async function checkExistingModel() {
  try {
    const path = `${modelsDir}/ggml-${selectedModel.value}.bin`
    const exists = await invoke<boolean>('check_whisper_model', {
      modelPath: path
    })
    if (exists) {
      modelPath.value = path
      status.value = `Using existing model: ${path}`
    }
  } catch (error) {
    // Model doesn't exist, that's fine
  }
}

async function listDevices() {
  try {
    devices.value = await invoke<string[]>('list_audio_devices')
  } catch (error) {
    status.value = `Error listing devices: ${error}`
    isError.value = true
  }
}

async function testAudioCapture() {
  try {
    const result = await invoke<string>('test_audio_capture')
    status.value = result
    isError.value = false
  } catch (error) {
    status.value = `Audio capture test failed: ${error}`
    isError.value = true
  }
}

function selectFile(event: Event) {
  const target = event.target as HTMLInputElement
  if (target.files && target.files[0]) {
    selectedFile.value = target.files[0]
  }
}

async function transcribeFile() {
  if (!selectedFile.value || !modelPath.value) return
  
  transcribing.value = true
  isError.value = false
  status.value = 'Transcribing audio file...'
  
  try {
    // In a real implementation, you'd need to save the file to a temporary location
    // that the Rust backend can access. For now, we'll show a placeholder.
    status.value = 'File transcription requires implementing file saving to temp directory'
    isError.value = true
    
    // This is how you would call it once file saving is implemented:
    // const result = await invoke<string>('transcribe_audio', {
    //   audioPath: '/path/to/temp/file.wav',
    //   modelPath: modelPath.value
    // })
    // transcriptionResult.value = result
  } catch (error) {
    status.value = `Transcription error: ${error}`
    isError.value = true
  } finally {
    transcribing.value = false
  }
}
</script>

<style scoped>
.audio-transcription {
  max-width: 800px;
  margin: 0 auto;
  padding: 20px;
}

.model-section, .devices-section, .file-section, .results-section, .live-section, .status-section {
  margin-bottom: 30px;
  padding: 20px;
  border: 1px solid #ddd;
  border-radius: 8px;
}

.model-controls {
  display: flex;
  gap: 10px;
  align-items: center;
  margin-bottom: 10px;
}

.model-controls select {
  padding: 8px;
  border: 1px solid #ddd;
  border-radius: 4px;
}

.model-controls button {
  padding: 8px 16px;
  background-color: #007cba;
  color: white;
  border: none;
  border-radius: 4px;
  cursor: pointer;
}

.model-controls button:disabled {
  background-color: #ccc;
  cursor: not-allowed;
}

.model-info {
  color: green;
  font-weight: bold;
}

.devices-section ul {
  list-style-type: none;
  padding: 0;
}

.devices-section li {
  padding: 5px 0;
  border-bottom: 1px solid #eee;
}

.result-text {
  background-color: #f5f5f5;
  padding: 15px;
  border-radius: 4px;
  white-space: pre-wrap;
  font-family: monospace;
}

.status-text {
  padding: 10px;
  border-radius: 4px;
  background-color: #e8f5e8;
}

.status-text.error {
  background-color: #fee;
  color: #d00;
}

.info {
  color: #666;
  font-style: italic;
}

button {
  padding: 8px 16px;
  background-color: #007cba;
  color: white;
  border: none;
  border-radius: 4px;
  cursor: pointer;
  margin: 5px;
}

button:disabled {
  background-color: #ccc;
  cursor: not-allowed;
}

input[type="file"] {
  margin: 10px 0;
}
</style>
