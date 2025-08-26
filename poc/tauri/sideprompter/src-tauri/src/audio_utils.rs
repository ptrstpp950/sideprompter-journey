// Audio utilities for whisper integration
use std::path::Path;

/// Load audio file and convert to the format required by whisper-rs
/// Whisper requires 16kHz, mono, f32 samples
pub fn load_audio_file(path: &str) -> Result<Vec<f32>, String> {
    let path = Path::new(path);
    
    // Check if file exists
    if !path.exists() {
        return Err(format!("Audio file not found: {}", path.display()));
    }
    
    // Get file extension to determine format
    let extension = path.extension()
        .and_then(|ext| ext.to_str())
        .unwrap_or("")
        .to_lowercase();
    
    match extension.as_str() {
        "wav" => load_wav_file(path),
        "mp3" | "m4a" | "aac" | "ogg" | "flac" => {
            Err(format!("Audio format '{}' not yet supported. Please implement using a audio decoding library like symphonia or ffmpeg-next.", extension))
        }
        _ => Err(format!("Unsupported audio format: {}", extension))
    }
}

fn load_wav_file(path: &Path) -> Result<Vec<f32>, String> {
    let mut reader = hound::WavReader::open(path)
        .map_err(|e| format!("Failed to open WAV file: {}", e))?;
    
    let spec = reader.spec();
    
    // Read samples based on bit depth
    let samples: Result<Vec<f32>, _> = match spec.bits_per_sample {
        16 => {
            reader.samples::<i16>()
                .map(|s| s.map(|s| s as f32 / 32768.0))
                .collect()
        },
        32 => {
            if spec.sample_format == hound::SampleFormat::Float {
                reader.samples::<f32>().collect()
            } else {
                reader.samples::<i32>()
                    .map(|s| s.map(|s| s as f32 / 2147483648.0))
                    .collect()
            }
        },
        _ => return Err(format!("Unsupported bit depth: {}", spec.bits_per_sample))
    };
    
    let mut audio_data = samples.map_err(|e| format!("Failed to read samples: {}", e))?;
    
    // Convert stereo to mono if needed
    if spec.channels == 2 {
        audio_data = stereo_to_mono(&audio_data);
    }
    
    // Resample to 16kHz if needed
    if spec.sample_rate != 16000 {
        audio_data = resample_audio(&audio_data, spec.sample_rate, 16000);
    }
    
    Ok(audio_data)
}

/// Convert stereo audio to mono by averaging channels
pub fn stereo_to_mono(stereo_samples: &[f32]) -> Vec<f32> {
    stereo_samples
        .chunks_exact(2)
        .map(|chunk| (chunk[0] + chunk[1]) / 2.0)
        .collect()
}

/// Resample audio to target sample rate (basic implementation)
/// For production use, consider using a proper resampling library
pub fn resample_audio(samples: &[f32], from_rate: u32, to_rate: u32) -> Vec<f32> {
    if from_rate == to_rate {
        return samples.to_vec();
    }
    
    // Simple linear interpolation resampling
    let ratio = from_rate as f64 / to_rate as f64;
    let output_len = (samples.len() as f64 / ratio) as usize;
    
    (0..output_len)
        .map(|i| {
            let src_index = i as f64 * ratio;
            let src_index_floor = src_index.floor() as usize;
            let src_index_ceil = (src_index.ceil() as usize).min(samples.len() - 1);
            
            if src_index_floor == src_index_ceil {
                samples[src_index_floor]
            } else {
                let fraction = src_index - src_index_floor as f64;
                let sample1 = samples[src_index_floor];
                let sample2 = samples[src_index_ceil];
                sample1 + (sample2 - sample1) * fraction as f32
            }
        })
        .collect()
}
