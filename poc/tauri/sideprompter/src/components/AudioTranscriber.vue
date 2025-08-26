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

    <!-- Live Transcription -->
    <div class="live-section">
      <h3>Live Transcription</h3>
      <div class="live-controls">
        <input 
          type="number" 
          v-model="transcriptionDuration" 
          min="5" 
          max="300" 
          placeholder="Duration (seconds)"
          style="width: 150px; margin-right: 10px;"
        />
        <button 
          @click="testAudioSetup" 
          :disabled="!modelPath"
        >
          Test Audio Setup
        </button>
        <button 
          @click="startLiveTranscription" 
          :disabled="!modelPath || isTranscribing"
        >
          {{ isTranscribing ? `Transcribing... (${timeRemaining}s)` : 'Start Live Transcription' }}
        </button>
        <button 
          v-if="isTranscribing"
          @click="stopLiveTranscription"
        >
          Stop
        </button>
      </div>
      
      <!-- Live Results -->
      <div v-if="liveResults.length > 0" class="live-results">
        <h4>Live Transcription Results</h4>
        <div class="results-container">
          <div 
            v-for="result in liveResults" 
            :key="result.timestamp"
            class="transcription-item"
            :class="{ 'mic-result': result.source === 'microphone', 'speaker-result': result.source === 'speaker' }"
          >
            <span class="timestamp">{{ formatTimestamp(result.timestamp) }}</span>
            <span class="source-badge">{{ result.source === 'microphone' ? '🎤' : '🔊' }}</span>
            <span class="text">{{ result.text }}</span>
          </div>
        </div>
      </div>
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

// Live transcription state
const transcriptionDuration = ref(30)
const isTranscribing = ref(false)
const timeRemaining = ref(0)
const liveResults = ref<Array<{text: string, source: string, timestamp: number}>>([])
let transcriptionTimer: number | null = null
let countdownTimer: number | null = null

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
    const result = await invoke<string>('test_audio_capture_with_transcription', {
      modelPath: modelPath.value
    })
    status.value = result
    isError.value = false
  } catch (error) {
    status.value = `Audio setup test failed: ${error}`
    isError.value = true
  }
}

async function testAudioSetup() {
  if (!modelPath.value) {
    status.value = 'Please download a model first'
    isError.value = true
    return
  }
  
  try {
    const result = await invoke<string>('test_audio_capture_with_transcription', {
      modelPath: modelPath.value
    })
    status.value = result
    isError.value = false
  } catch (error) {
    status.value = `Audio setup test failed: ${error}`
    isError.value = true
  }
}

async function startLiveTranscription() {
  if (!modelPath.value) {
    status.value = 'Please download a model first'
    isError.value = true
    return
  }
  
  isTranscribing.value = true
  timeRemaining.value = transcriptionDuration.value
  liveResults.value = []
  status.value = `Starting live transcription for ${transcriptionDuration.value} seconds...`
  isError.value = false
  
  // Start countdown timer
  countdownTimer = setInterval(() => {
    timeRemaining.value--
    if (timeRemaining.value <= 0) {
      if (countdownTimer) clearInterval(countdownTimer)
    }
  }, 1000)
  
  try {
    const results = await invoke<Array<{text: string, source: string, timestamp: number}>>(
      'start_live_transcription',
      {
        modelPath: modelPath.value,
        durationSeconds: transcriptionDuration.value
      }
    )
    
    liveResults.value = results
    status.value = `Live transcription completed. Found ${results.length} transcriptions.`
  } catch (error) {
    status.value = `Live transcription failed: ${error}`
    isError.value = true
  } finally {
    isTranscribing.value = false
    timeRemaining.value = 0
    if (countdownTimer) {
      clearInterval(countdownTimer)
      countdownTimer = null
    }
  }
}

function stopLiveTranscription() {
  isTranscribing.value = false
  timeRemaining.value = 0
  if (countdownTimer) {
    clearInterval(countdownTimer)
    countdownTimer = null
  }
  status.value = 'Live transcription stopped'
}

function formatTimestamp(timestamp: number): string {
  const date = new Date(timestamp)
  return date.toLocaleTimeString()
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

.live-controls {
  display: flex;
  gap: 10px;
  align-items: center;
  margin-bottom: 20px;
  flex-wrap: wrap;
}

.live-results {
  margin-top: 20px;
}

.results-container {
  max-height: 400px;
  overflow-y: auto;
  border: 1px solid #ddd;
  border-radius: 4px;
  padding: 10px;
}

.transcription-item {
  display: flex;
  align-items: center;
  padding: 8px 0;
  border-bottom: 1px solid #eee;
}

.transcription-item:last-child {
  border-bottom: none;
}

.transcription-item.mic-result {
  background-color: #f0f8ff;
}

.transcription-item.speaker-result {
  background-color: #f5f5dc;
}

.timestamp {
  font-size: 0.8em;
  color: #666;
  margin-right: 10px;
  min-width: 80px;
}

.source-badge {
  font-size: 1.2em;
  margin-right: 10px;
}

.text {
  flex: 1;
  font-family: monospace;
}
</style>
