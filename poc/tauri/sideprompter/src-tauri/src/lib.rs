// Learn more about Tauri commands at https://tauri.app/develop/calling-rust/
use std::path::Path;
use whisper_rs::{WhisperContext, WhisperContextParameters, FullParams, SamplingStrategy};
use serde::{Deserialize, Serialize};

mod audio_utils;
mod audio_capture;
mod whisper_downloader;

use whisper_downloader::{WhisperModelDownloader, WhisperModelType};

#[derive(Serialize, Deserialize, Clone)]
pub struct TranscriptionResult {
    pub text: String,
    pub source: String, // "mic" or "speaker"
    pub timestamp: u64,
}

#[tauri::command]
fn greet(name: &str) -> String {
    format!("Hello, {}! You've been greeted from Rust!", name)
}

#[tauri::command]
async fn download_whisper_model(model_type: String, models_dir: String) -> Result<String, String> {
    let model_enum = WhisperModelType::from_string(&model_type)
        .ok_or_else(|| format!("Invalid model type: {}", model_type))?;
    
    WhisperModelDownloader::download_model(model_enum, &models_dir).await
}

#[tauri::command]
async fn test_audio_capture() -> Result<String, String> {
    use cpal::traits::{DeviceTrait, HostTrait};
    
    let host = cpal::default_host();
    
    // Get default input device (microphone)
    if let Some(device) = host.default_input_device() {
        let name = device.name().unwrap_or("Unknown".to_string());
        return Ok(format!("Found microphone: {}", name));
    }
    
    Err("No input device found".to_string())
}

#[tauri::command]
async fn list_audio_devices() -> Result<Vec<String>, String> {
    use cpal::traits::{DeviceTrait, HostTrait};
    
    let host = cpal::default_host();
    let mut devices = Vec::new();
    
    // List input devices
    if let Ok(input_devices) = host.input_devices() {
        for device in input_devices {
            if let Ok(name) = device.name() {
                devices.push(format!("Input: {}", name));
            }
        }
    }
    
    // List output devices (for reference)
    if let Ok(output_devices) = host.output_devices() {
        for device in output_devices {
            if let Ok(name) = device.name() {
                devices.push(format!("Output: {}", name));
            }
        }
    }
    
    Ok(devices)
}

#[tauri::command]
fn set_window_protection(window: tauri::Window, enable: bool) -> Result<String, String> {
    #[cfg(target_os = "macos")]
    {
        /* use cocoa::appkit::NSWindow;
        use cocoa::base::id;
        use objc::{msg_send, sel, sel_impl, runtime::YES};
        unsafe {
            match window.ns_window() {
                Ok(ns_window) => {
                    let ns_window: id = ns_window as id;
                    // Example: setLevel 5 for enable, 0 for disable (customize as needed)
                    let level = if enable { 5 } else { 0 };
                    let _: () = msg_send![ns_window, setLevel: level];
                    Ok(format!("Window level set to {} (macOS)", level))
                },
                Err(e) => Err(format!("Failed to get ns_window (macOS): {}", e)),
            }
        }
        */
        Ok("Not implemented on macOS yet".to_string())
    }
    #[cfg(target_os = "windows")]
    {
        use windows::Win32::UI::WindowsAndMessaging::{SetWindowDisplayAffinity, WDA_EXCLUDEFROMCAPTURE, WDA_NONE};
        unsafe {
            match window.hwnd() {
                Ok(hwnd) => {
                    let affinity = if enable { WDA_EXCLUDEFROMCAPTURE } else { WDA_NONE };
                    let result = SetWindowDisplayAffinity(hwnd, affinity);
                    if result.is_err() {
                        Err("SetWindowDisplayAffinity failed (Windows)".to_string())
                    } else {
                        Ok(format!("{}", if enable { "WDA_EXCLUDEFROMCAPTURE" } else { "WDA_NONE" }))
                    }
                },
                Err(e) => Err(format!("Failed to get hwnd (Windows): {}", e)),
            }
        }
    }
    #[cfg(not(any(target_os = "macos", target_os = "windows")))]
    {
        Err("Not supported on this platform".to_string())
    }
}

#[tauri::command]
async fn transcribe_audio(audio_path: String, model_path: String) -> Result<String, String> {
    // Validate paths exist
    if !Path::new(&audio_path).exists() {
        return Err(format!("Audio file not found: {}", audio_path));
    }
    if !Path::new(&model_path).exists() {
        return Err(format!("Model file not found: {}", model_path));
    }

    // Run whisper transcription in a blocking task to avoid blocking the async runtime
    let result = tokio::task::spawn_blocking(move || -> Result<String, String> {
        // Create whisper context
        let ctx = WhisperContext::new_with_params(
            &model_path,
            WhisperContextParameters::default()
        ).map_err(|e| format!("Failed to create whisper context: {}", e))?;

        // Create a state for the whisper context
        let mut state = ctx.create_state()
            .map_err(|e| format!("Failed to create whisper state: {}", e))?;

        // Set up parameters for full transcription
        let mut params = FullParams::new(SamplingStrategy::Greedy { best_of: 1 });
        params.set_print_special(false);
        params.set_print_progress(false);
        params.set_print_realtime(false);
        params.set_print_timestamps(false);

        // Load and process audio file
        let audio_data = audio_utils::load_audio_file(&audio_path)?;
        
        // Ensure audio is in the correct format for whisper (16kHz, mono)
        let audio_data = if audio_data.len() % 2 == 0 {
            // Assume stereo, convert to mono
            audio_utils::stereo_to_mono(&audio_data)
        } else {
            audio_data
        };
        
        // Perform transcription
        state.full(params, &audio_data)
            .map_err(|e| format!("Transcription failed: {}", e))?;
        
        let num_segments = state.full_n_segments()
            .map_err(|e| format!("Failed to get segments: {}", e))?;
        
        let mut result = String::new();
        for i in 0..num_segments {
            let segment = state.full_get_segment_text(i)
                .map_err(|e| format!("Failed to get segment text: {}", e))?;
            result.push_str(&segment);
        }
        
        Ok(result)
    }).await.map_err(|e| format!("Task execution failed: {}", e))??;

    Ok(result)
}

#[tauri::command]
async fn check_whisper_model(model_path: String) -> Result<bool, String> {
    let path = Path::new(&model_path);
    if !path.exists() {
        return Ok(false);
    }

    // Try to load the model to verify it's valid
    tokio::task::spawn_blocking(move || -> Result<bool, String> {
        match WhisperContext::new_with_params(&model_path, WhisperContextParameters::default()) {
            Ok(_) => Ok(true),
            Err(e) => Err(format!("Invalid whisper model: {}", e)),
        }
    }).await.map_err(|e| format!("Task execution failed: {}", e))?
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .invoke_handler(tauri::generate_handler![
            greet, 
            set_window_protection, 
            transcribe_audio, 
            check_whisper_model,
            download_whisper_model,
            test_audio_capture,
            list_audio_devices
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
