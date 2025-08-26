use std::path::Path;
use std::fs;
use reqwest;
use tokio::io::AsyncWriteExt;

pub struct WhisperModelDownloader;

impl WhisperModelDownloader {
    const BASE_URL: &'static str = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";
    
    pub async fn download_model(model_type: WhisperModelType, models_dir: &str) -> Result<String, String> {
        let model_filename = model_type.filename();
        let model_path = Path::new(models_dir).join(&model_filename);
        
        // Create models directory if it doesn't exist
        if let Some(parent) = model_path.parent() {
            fs::create_dir_all(parent)
                .map_err(|e| format!("Failed to create models directory: {}", e))?;
        }
        
        // Skip download if model already exists
        if model_path.exists() {
            return Ok(model_path.to_string_lossy().to_string());
        }
        
        let url = format!("{}{}", Self::BASE_URL, model_filename);
        println!("Downloading Whisper model from: {}", url);
        
        let response = reqwest::get(&url).await
            .map_err(|e| format!("Failed to download model: {}", e))?;
        
        if !response.status().is_success() {
            return Err(format!("Failed to download model: HTTP {}", response.status()));
        }
        
        let bytes = response.bytes().await
            .map_err(|e| format!("Failed to read model data: {}", e))?;
        
        let mut file = tokio::fs::File::create(&model_path).await
            .map_err(|e| format!("Failed to create model file: {}", e))?;
        
        file.write_all(&bytes).await
            .map_err(|e| format!("Failed to write model file: {}", e))?;
        
        file.sync_all().await
            .map_err(|e| format!("Failed to sync model file: {}", e))?;
        
        println!("Model downloaded successfully: {}", model_path.display());
        Ok(model_path.to_string_lossy().to_string())
    }
}

#[derive(Clone, Copy, Debug)]
pub enum WhisperModelType {
    Tiny,
    TinyEn,
    Base,
    BaseEn,
    Small,
    SmallEn,
    Medium,
    MediumEn,
    Large,
    LargeV1,
    LargeV2,
    LargeV3,
}

impl WhisperModelType {
    pub fn filename(&self) -> String {
        match self {
            WhisperModelType::Tiny => "ggml-tiny.bin".to_string(),
            WhisperModelType::TinyEn => "ggml-tiny.en.bin".to_string(),
            WhisperModelType::Base => "ggml-base.bin".to_string(),
            WhisperModelType::BaseEn => "ggml-base.en.bin".to_string(),
            WhisperModelType::Small => "ggml-small.bin".to_string(),
            WhisperModelType::SmallEn => "ggml-small.en.bin".to_string(),
            WhisperModelType::Medium => "ggml-medium.bin".to_string(),
            WhisperModelType::MediumEn => "ggml-medium.en.bin".to_string(),
            WhisperModelType::Large => "ggml-large.bin".to_string(),
            WhisperModelType::LargeV1 => "ggml-large-v1.bin".to_string(),
            WhisperModelType::LargeV2 => "ggml-large-v2.bin".to_string(),
            WhisperModelType::LargeV3 => "ggml-large-v3.bin".to_string(),
        }
    }
    
    pub fn from_string(s: &str) -> Option<Self> {
        match s.to_lowercase().as_str() {
            "tiny" => Some(WhisperModelType::Tiny),
            "tiny.en" => Some(WhisperModelType::TinyEn),
            "base" => Some(WhisperModelType::Base),
            "base.en" => Some(WhisperModelType::BaseEn),
            "small" => Some(WhisperModelType::Small),
            "small.en" => Some(WhisperModelType::SmallEn),
            "medium" => Some(WhisperModelType::Medium),
            "medium.en" => Some(WhisperModelType::MediumEn),
            "large" => Some(WhisperModelType::Large),
            "large-v1" => Some(WhisperModelType::LargeV1),
            "large-v2" => Some(WhisperModelType::LargeV2),
            "large-v3" => Some(WhisperModelType::LargeV3),
            _ => None,
        }
    }
}
