use std::sync::{Arc, Mutex, mpsc};
use std::thread;
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};
use cpal::traits::{DeviceTrait, HostTrait, StreamTrait};

use crate::audio_capture::{AudioTranscriber};

#[derive(Clone)]
pub struct TranscriptionEvent {
    pub text: String,
    pub source: String,
    pub timestamp: u64,
}

pub struct LiveTranscriptionSession {
    _mic_stream: Option<cpal::Stream>,
    _speaker_stream: Option<cpal::Stream>,
    results: Arc<Mutex<Vec<TranscriptionEvent>>>,
    is_running: Arc<Mutex<bool>>,
}

impl LiveTranscriptionSession {
    pub fn start(model_path: &str, duration_seconds: u64) -> Result<Vec<TranscriptionEvent>, String> {
        let transcriber = Arc::new(AudioTranscriber::new(model_path)?);
        let results = Arc::new(Mutex::new(Vec::new()));
        let is_running = Arc::new(Mutex::new(true));
        
        let host = cpal::default_host();
        
        // Setup microphone capture
        let mic_device = host.default_input_device()
            .ok_or("No default input device available")?;
        
        let mic_config = mic_device.default_input_config()
            .map_err(|e| format!("Failed to get mic config: {}", e))?;
        
        let (mic_sender, mic_receiver) = mpsc::channel::<Vec<f32>>();
        
        // Create mic stream
        let transcriber_mic = transcriber.clone();
        let results_mic = results.clone();
        let is_running_mic = is_running.clone();
        
        thread::spawn(move || {
            Self::process_audio_stream(mic_receiver, transcriber_mic, results_mic, is_running_mic, "microphone");
        });
        
        let mic_stream = match mic_config.sample_format() {
            cpal::SampleFormat::F32 => {
                let sender = mic_sender.clone();
                mic_device.build_input_stream(
                    &mic_config.config(),
                    move |data: &[f32], _: &cpal::InputCallbackInfo| {
                        sender.send(data.to_vec()).ok();
                    },
                    |err| eprintln!("Mic stream error: {}", err),
                    None,
                ).map_err(|e| format!("Failed to build mic stream: {}", e))?
            },
            cpal::SampleFormat::I16 => {
                let sender = mic_sender.clone();
                mic_device.build_input_stream(
                    &mic_config.config(),
                    move |data: &[i16], _: &cpal::InputCallbackInfo| {
                        let samples: Vec<f32> = data.iter().map(|&x| x as f32 / 32768.0).collect();
                        sender.send(samples).ok();
                    },
                    |err| eprintln!("Mic stream error: {}", err),
                    None,
                ).map_err(|e| format!("Failed to build mic stream: {}", e))?
            },
            _ => return Err("Unsupported sample format".to_string()),
        };
        
        mic_stream.play().map_err(|e| format!("Failed to start mic stream: {}", e))?;
        
        // Let it run for the specified duration
        thread::sleep(Duration::from_secs(duration_seconds));
        
        // Stop the session
        *is_running.lock().unwrap() = false;
        
        // Give a moment for any final processing
        thread::sleep(Duration::from_millis(500));
        
        // Return results
        let final_results = results.lock().unwrap().clone();
        Ok(final_results)
    }
    
    fn process_audio_stream(
        receiver: mpsc::Receiver<Vec<f32>>,
        transcriber: Arc<AudioTranscriber>,
        results: Arc<Mutex<Vec<TranscriptionEvent>>>,
        is_running: Arc<Mutex<bool>>,
        source: &'static str,
    ) {
        let mut buffer = Vec::new();
        let mut last_process = Instant::now();
        
        while *is_running.lock().unwrap() {
            // Collect audio data
            match receiver.recv_timeout(Duration::from_millis(100)) {
                Ok(audio_chunk) => {
                    buffer.extend_from_slice(&audio_chunk);
                    
                    // Process every 3 seconds or when buffer is large enough
                    if buffer.len() >= 48000 || last_process.elapsed() >= Duration::from_secs(3) {
                        if buffer.len() > 1600 { // At least 0.1 seconds of audio at 16kHz
                            // Convert to 16kHz mono (simple decimation for demo)
                            let processed: Vec<f32> = buffer.iter().step_by(3).cloned().collect();
                            
                            match transcriber.transcribe(&processed) {
                                Ok(text) if !text.trim().is_empty() => {
                                    let timestamp = SystemTime::now()
                                        .duration_since(UNIX_EPOCH)
                                        .unwrap()
                                        .as_millis() as u64;
                                    
                                    let event = TranscriptionEvent {
                                        text: format!("[{}] {}", if source == "microphone" { "m" } else { "o" }, text),
                                        source: source.to_string(),
                                        timestamp,
                                    };
                                    
                                    results.lock().unwrap().push(event);
                                }
                                Err(_) => {
                                    // Ignore transcription errors for now
                                }
                                _ => {} // Empty result
                            }
                        }
                        
                        buffer.clear();
                        last_process = Instant::now();
                    }
                }
                Err(_) => {
                    // Timeout, continue
                }
            }
        }
    }
}
