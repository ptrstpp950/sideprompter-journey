// Learn more about Tauri commands at https://tauri.app/develop/calling-rust/
#[tauri::command]
fn greet(name: &str) -> String {
    format!("Hello, {}! You've been greeted from Rust!", name)
}

#[tauri::command]
fn set_window_protection(window: tauri::Window, enable: bool) -> Result<String, String> {
    #[cfg(target_os = "macos")]
    {
        use cocoa::appkit::NSWindow;
        use cocoa::base::id;
        use objc::runtime::YES;
        unsafe {
            match window.ns_window() {
                Some(ns_window) => {
                    let ns_window: id = ns_window as id;
                    // Example: setLevel 5 for enable, 0 for disable (customize as needed)
                    let level = if enable { 5 } else { 0 };
                    let _: () = msg_send![ns_window, setLevel: level];
                    Ok(format!("Window level set to {} (macOS)", level))
                },
                None => Err("Failed to get ns_window (macOS)".to_string()),
            }
        }
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

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .invoke_handler(tauri::generate_handler![greet, set_window_protection])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
