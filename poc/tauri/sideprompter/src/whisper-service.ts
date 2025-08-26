// Tauri command bindings for whisper functionality
import { invoke } from '@tauri-apps/api/core';

export interface WhisperService {
  /**
   * Transcribe an audio file to text using Whisper
   * @param audioPath - Path to the audio file
   * @param modelPath - Path to the Whisper model file (.bin)
   * @returns Promise with the transcribed text
   */
  transcribeAudio(audioPath: string, modelPath: string): Promise<string>;
  
  /**
   * Check if a Whisper model file is valid
   * @param modelPath - Path to the Whisper model file
   * @returns Promise with boolean indicating if model is valid
   */
  checkWhisperModel(modelPath: string): Promise<boolean>;
}

export const whisperService: WhisperService = {
  async transcribeAudio(audioPath: string, modelPath: string): Promise<string> {
    try {
      return await invoke('transcribe_audio', { audioPath, modelPath });
    } catch (error) {
      throw new Error(`Transcription failed: ${error}`);
    }
  },
  
  async checkWhisperModel(modelPath: string): Promise<boolean> {
    try {
      return await invoke('check_whisper_model', { modelPath });
    } catch (error) {
      console.error('Model check failed:', error);
      return false;
    }
  }
};

// Example usage:
// import { whisperService } from './whisper-service';
// 
// try {
//   const isValidModel = await whisperService.checkWhisperModel('/path/to/model.bin');
//   if (isValidModel) {
//     const transcription = await whisperService.transcribeAudio('/path/to/audio.wav', '/path/to/model.bin');
//     console.log('Transcription:', transcription);
//   }
// } catch (error) {
//   console.error('Error:', error);
// }
