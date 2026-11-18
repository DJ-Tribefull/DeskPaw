#![windows_subsystem = "windows"]

use serde::Deserialize;
use serde_json::Value;
use std::io::{self, BufRead, Write};
use std::sync::{mpsc, Mutex};
use tauri::{Manager, State};

struct Panel {
    initial: Value,
    replies: Mutex<mpsc::Receiver<String>>,
}

#[derive(Deserialize, serde::Serialize)]
#[serde(rename_all = "camelCase")]
struct Changes {
    mods: u32,
    key: u32,
    pin: String,
    confirm: String,
    fade: bool,
    startup: Option<bool>,
}

#[tauri::command]
fn get_settings(panel: State<'_, Panel>) -> Value {
    panel.initial.clone()
}

#[tauri::command]
async fn save_settings(app: tauri::AppHandle, changes: Changes) -> Result<(), String> {
    tauri::async_runtime::spawn_blocking(move || {
        let disconnected = || "The tray app closed. Reopen Settings from the tray.".to_string();
        let panel = app.state::<Panel>();
        let replies = panel.replies.lock().map_err(|_| disconnected())?;
        {
            let mut output = io::stdout().lock();
            serde_json::to_writer(&mut output, &changes).map_err(|_| disconnected())?;
            output.write_all(b"\n").and_then(|_| output.flush()).map_err(|_| disconnected())?;
        }
        let reply = replies.recv().map_err(|_| disconnected())?;
        let error: Option<String> = serde_json::from_str(&reply).map_err(|_| disconnected())?;
        match error { Some(message) => Err(message), None => Ok(()) }
    }).await.map_err(|_| "Could not save the settings.".to_string())?
}

#[tauri::command]
fn close_panel(window: tauri::Window) -> Result<(), String> {
    window.close().map_err(|e| e.to_string())
}

fn main() {
    // Only the tray app owns the settings file, hotkey registration and startup entry.
    // The first line contains display data. Subsequent lines acknowledge edits from this panel.
    let mut line = String::new();
    if io::stdin().read_line(&mut line).is_err() { return; }
    let Ok(initial) = serde_json::from_str::<Value>(&line) else { return };
    let (tx, rx) = mpsc::channel();
    tauri::Builder::default()
        .manage(Panel { initial, replies: Mutex::new(rx) })
        .setup(move |app| {
            let handle = app.handle().clone();
            std::thread::spawn(move || {
                for line in io::stdin().lock().lines() {
                    let Ok(value) = line else { break };
                    if tx.send(value).is_err() { break; }
                }
                handle.exit(0);
            });
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![get_settings, save_settings, close_panel])
        .run(tauri::generate_context!())
        .expect("DeskPause settings could not open");
}
