use cpal::traits::{DeviceTrait, HostTrait, StreamTrait};
use crossbeam_channel::{Receiver, Sender, bounded};
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::{Duration, Instant};

pub struct AudioCapture {
    pub mic_receiver: Receiver<Vec<f32>>,
    pub speaker_receiver: Receiver<Vec<f32>>,
    _mic_stream: cpal::Stream,
    _speaker_stream: Option<cpal::Stream>,
}

impl AudioCapture {
    pub fn new() -> Result<Self, String> {
        let host = cpal::default_host();
        
        // Get microphone device
        let mic_device = host.default_input_device()
            .ok_or("No default input device available")?;
            
        // Try to get speaker/loopback device
        let speaker_device = Self::get_loopback_device(&host);
        
        // Setup microphone capture
        let mic_config = mic_device.default_input_config()
            .map_err(|e| format!("Failed to get default input config: {}", e))?;
            
        let (mic_sender, mic_receiver) = bounded(10);
        let mic_stream = Self::create_input_stream(&mic_device, &mic_config, mic_sender)?;
        
        // Setup speaker capture (if available)
        let (speaker_sender, speaker_receiver) = bounded(10);
        let speaker_stream = match speaker_device {
            Some(device) => {
                match device.default_input_config() {
                    Ok(config) => {
                        match Self::create_input_stream(&device, &config, speaker_sender) {
                            Ok(stream) => Some(stream),
                            Err(e) => {
                                println!("Warning: Failed to create speaker stream: {}", e);
                                None
                            }
                        }
                    },
                    Err(e) => {
                        println!("Warning: Failed to get speaker config: {}", e);
                        None
                    }
                }
            },
            None => {
                println!("Warning: No loopback device found for speaker capture");
                None
            }
        };
        
        // Start streams
        mic_stream.play().map_err(|e| format!("Failed to start mic stream: {}", e))?;
        if let Some(ref stream) = speaker_stream {
            stream.play().map_err(|e| format!("Failed to start speaker stream: {}", e))?;
        }
        
        Ok(AudioCapture {
            mic_receiver,
            speaker_receiver,
            _mic_stream: mic_stream,
            _speaker_stream: speaker_stream,
        })
    }
    
    fn get_loopback_device(host: &cpal::Host) -> Option<cpal::Device> {
        // On Windows, try to find a loopback device
        #[cfg(target_os = "windows")]
        {
            // Look for devices with "Stereo Mix" or similar loopback capability
            host.input_devices().ok()?.find(|device| {
                if let Ok(name) = device.name() {
                    let name_lower = name.to_lowercase();
                    name_lower.contains("stereo mix") || 
                    name_lower.contains("what u hear") ||
                    name_lower.contains("loopback")
                } else {
                    false
                }
            })
        }
        
        #[cfg(not(target_os = "windows"))]
        {
            // On other platforms, this might require different approaches
            // For now, return None
            None
        }
    }
    
    fn create_input_stream(
        device: &cpal::Device,
        config: &cpal::SupportedStreamConfig,
        sender: Sender<Vec<f32>>,
    ) -> Result<cpal::Stream, String> {
        let sample_rate = config.sample_rate().0;
        let channels = config.channels() as usize;
        
        // Buffer for collecting audio data
        let buffer = Arc::new(Mutex::new(Vec::<f32>::new()));
        let buffer_clone = buffer.clone();
        
        // Start processing thread
        let sender_clone = sender.clone();
        thread::spawn(move || {
            Self::process_audio_buffer(buffer_clone, sender_clone, sample_rate, channels);
        });
        
        let stream = match config.sample_format() {
            cpal::SampleFormat::F32 => {
                device.build_input_stream(
                    &config.config(),
                    move |data: &[f32], _: &cpal::InputCallbackInfo| {
                        let mut buf = buffer.lock().unwrap();
                        buf.extend_from_slice(data);
                    },
                    |err| eprintln!("Audio stream error: {}", err),
                    None,
                ).map_err(|e| format!("Failed to build input stream: {}", e))?
            },
            cpal::SampleFormat::I16 => {
                device.build_input_stream(
                    &config.config(),
                    move |data: &[i16], _: &cpal::InputCallbackInfo| {
                        let mut buf = buffer.lock().unwrap();
                        for &sample in data {
                            let sample_f32 = sample as f32 / 32768.0;
                            buf.push(sample_f32);
                        }
                    },
                    |err| eprintln!("Audio stream error: {}", err),
                    None,
                ).map_err(|e| format!("Failed to build input stream: {}", e))?
            },
            _ => return Err("Unsupported sample format".to_string()),
        };
        
        Ok(stream)
    }
    
    fn process_audio_buffer(
        buffer: Arc<Mutex<Vec<f32>>>,
        sender: Sender<Vec<f32>>,
        sample_rate: u32,
        channels: usize,
    ) {
        let target_sample_rate = 16000; // Whisper expects 16kHz
        let chunk_duration = Duration::from_secs(3); // Process 3-second chunks
        let samples_per_chunk = target_sample_rate * 3;
        
        let mut processed_buffer = Vec::with_capacity(samples_per_chunk as usize);
        let mut last_process = Instant::now();
        
        loop {
            thread::sleep(Duration::from_millis(100));
            
            // Get data from buffer
            let mut data_chunk = {
                let mut buf = buffer.lock().unwrap();
                if buf.is_empty() {
                    continue;
                }
                let chunk = buf.clone();
                buf.clear();
                chunk
            };
            
            processed_buffer.extend_from_slice(&data_chunk);
            
            // Process buffer when we have enough data or enough time has passed
            if processed_buffer.len() >= samples_per_chunk as usize || 
               (last_process.elapsed() >= chunk_duration && !processed_buffer.is_empty()) {
                
                // Convert to mono if stereo
                let mono_buffer = if channels == 2 {
                    processed_buffer.chunks_exact(2)
                        .map(|chunk| (chunk[0] + chunk[1]) / 2.0)
                        .collect::<Vec<f32>>()
                } else {
                    processed_buffer.clone()
                };
                
                // Resample to 16kHz if needed
                let resampled = if sample_rate != target_sample_rate {
                    crate::audio_utils::resample_audio(&mono_buffer, sample_rate, target_sample_rate)
                } else {
                    mono_buffer
                };
                
                // Send processed audio
                if resampled.len() > 1600 { // At least 0.1 seconds of audio
                    if sender.send(resampled).is_err() {
                        break; // Receiver dropped
                    }
                }
                
                processed_buffer.clear();
                last_process = Instant::now();
            }
        }
    }
}

pub struct AudioTranscriber {
    whisper_context: whisper_rs::WhisperContext,
}

impl AudioTranscriber {
    pub fn new(model_path: &str) -> Result<Self, String> {
        let context = whisper_rs::WhisperContext::new_with_params(
            model_path,
            whisper_rs::WhisperContextParameters::default()
        ).map_err(|e| format!("Failed to load Whisper model: {}", e))?;
        
        Ok(AudioTranscriber {
            whisper_context: context,
        })
    }
    
    pub fn transcribe(&self, audio_data: &[f32]) -> Result<String, String> {
        let mut state = self.whisper_context.create_state()
            .map_err(|e| format!("Failed to create whisper state: {}", e))?;
        
        let mut params = whisper_rs::FullParams::new(whisper_rs::SamplingStrategy::Greedy { best_of: 1 });
        params.set_language(Some("en"));
        params.set_print_special(false);
        params.set_print_progress(false);
        params.set_print_realtime(false);
        params.set_print_timestamps(false);
        
        state.full(params, audio_data)
            .map_err(|e| format!("Transcription failed: {}", e))?;
        
        let num_segments = state.full_n_segments()
            .map_err(|e| format!("Failed to get segments: {}", e))?;
        
        let mut result = String::new();
        for i in 0..num_segments {
            let segment = state.full_get_segment_text(i)
                .map_err(|e| format!("Failed to get segment text: {}", e))?;
            if !segment.trim().is_empty() {
                result.push_str(&segment.trim());
                if i < num_segments - 1 {
                    result.push(' ');
                }
            }
        }
        
        Ok(result.trim().to_string())
    }
}
