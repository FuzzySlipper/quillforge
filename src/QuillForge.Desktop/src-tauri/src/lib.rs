use std::env;
use std::net::TcpListener;
use std::path::{Path, PathBuf};
use std::process::Command as ProcessCommand;
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use dirs::{document_dir, home_dir};
use reqwest::StatusCode;
use serde::Serialize;
use tauri::async_runtime::{spawn, Mutex};
use tauri::{AppHandle, Emitter, Manager, RunEvent, State};
use tauri_plugin_shell::process::{CommandChild, CommandEvent};
use tauri_plugin_shell::ShellExt;
use tokio::time::sleep;

const MAIN_WINDOW_LABEL: &str = "main";
const STATUS_EVENT: &str = "desktop://status";
const READY_POLL_INTERVAL: Duration = Duration::from_millis(500);
const READY_TIMEOUT: Duration = Duration::from_secs(30);

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct DesktopShellStatus {
    phase: &'static str,
    message: Option<String>,
    backend_url: Option<String>,
    workspace_path: String,
    port: Option<u16>,
    bind_mode: &'static str,
    restart_available: bool,
}

impl DesktopShellStatus {
    fn starting(workspace_path: String, port: u16, message: impl Into<String>) -> Self {
        Self {
            phase: "starting",
            message: Some(message.into()),
            backend_url: None,
            workspace_path,
            port: Some(port),
            bind_mode: "loopback",
            restart_available: true,
        }
    }

    fn ready(workspace_path: String, port: u16, backend_url: String) -> Self {
        Self {
            phase: "ready",
            message: Some("The local QuillForge backend is ready.".to_string()),
            backend_url: Some(backend_url),
            workspace_path,
            port: Some(port),
            bind_mode: "loopback",
            restart_available: true,
        }
    }

    fn failed(workspace_path: String, port: Option<u16>, message: impl Into<String>) -> Self {
        Self {
            phase: "failed",
            message: Some(message.into()),
            backend_url: None,
            workspace_path,
            port,
            bind_mode: "loopback",
            restart_available: true,
        }
    }

    fn exited(workspace_path: String, port: Option<u16>, message: impl Into<String>) -> Self {
        Self {
            phase: "exited",
            message: Some(message.into()),
            backend_url: None,
            workspace_path,
            port,
            bind_mode: "loopback",
            restart_available: true,
        }
    }

    fn stopped(workspace_path: String) -> Self {
        Self {
            phase: "stopped",
            message: Some("Shutting down the QuillForge backend.".to_string()),
            backend_url: None,
            workspace_path,
            port: None,
            bind_mode: "loopback",
            restart_available: false,
        }
    }
}

impl Default for DesktopShellStatus {
    fn default() -> Self {
        Self {
            phase: "starting",
            message: Some("Preparing QuillForge desktop startup.".to_string()),
            backend_url: None,
            workspace_path: resolve_workspace_path().display().to_string(),
            port: None,
            bind_mode: "loopback",
            restart_available: true,
        }
    }
}

#[derive(Default)]
struct RuntimeState {
    generation: u64,
    child: Option<CommandChild>,
    status: DesktopShellStatus,
    shutting_down: bool,
}

#[derive(Default)]
struct DesktopRuntime {
    inner: Mutex<RuntimeState>,
}

#[tauri::command]
async fn get_shell_status(state: State<'_, DesktopRuntime>) -> Result<DesktopShellStatus, String> {
    Ok(state.inner.lock().await.status.clone())
}

#[tauri::command]
async fn restart_backend(app: AppHandle) -> Result<(), String> {
    start_backend(app, "Restarting the QuillForge backend...").await
}

#[tauri::command]
async fn open_workspace(app: AppHandle) -> Result<(), String> {
    let workspace_path = {
        let state = app.state::<DesktopRuntime>();
        let guard = state.inner.lock().await;
        guard.status.workspace_path.clone()
    };

    reveal_in_file_manager(Path::new(&workspace_path))
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    let builder = tauri::Builder::default()
        .plugin(tauri_plugin_log::Builder::default().level(log::LevelFilter::Info).build())
        .plugin(tauri_plugin_shell::init())
        .plugin(tauri_plugin_single_instance::init(|app, _args, _cwd| {
            if let Some(window) = app.get_webview_window(MAIN_WINDOW_LABEL) {
                let _ = window.show();
                let _ = window.set_focus();
            }
        }))
        .manage(DesktopRuntime::default())
        .invoke_handler(tauri::generate_handler![
            get_shell_status,
            restart_backend,
            open_workspace
        ])
        .setup(|app| {
            let app_handle = app.handle().clone();
            spawn(async move {
                let _ = start_backend(app_handle, "Launching the QuillForge backend...").await;
            });
            Ok(())
        });

    let app = builder
        .build(tauri::generate_context!())
        .expect("error while building QuillForge desktop");

    app.run(|app_handle, event| match event {
        RunEvent::ExitRequested { .. } | RunEvent::Exit => {
            tauri::async_runtime::block_on(async {
                shutdown_backend(app_handle.clone()).await;
            });
        }
        _ => {}
    });
}

async fn start_backend(app: AppHandle, startup_message: &str) -> Result<(), String> {
    let workspace_path = resolve_workspace_path();
    let workspace_text = workspace_path.display().to_string();
    let port = reserve_port().map_err(|error| error.to_string())?;
    let backend_url = format!("http://127.0.0.1:{port}");
    let generation;

    {
        let runtime = app.state::<DesktopRuntime>();
        let mut state = runtime.inner.lock().await;
        state.shutting_down = false;
        state.generation += 1;
        generation = state.generation;

        if let Some(existing_child) = state.child.take() {
            let _ = existing_child.kill();
        }

        state.status = DesktopShellStatus::starting(workspace_text.clone(), port, startup_message);
        emit_status(&app, &state.status);
    }

    let desktop_instance_id = format!(
        "{}-{}",
        std::process::id(),
        SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap_or_default()
            .as_millis()
    );

    let args = vec![
        "--desktop-mode".to_string(),
        "--content-root".to_string(),
        workspace_text.clone(),
        "--bind-mode".to_string(),
        "loopback".to_string(),
        "--port".to_string(),
        port.to_string(),
        "--desktop-instance-id".to_string(),
        desktop_instance_id,
        "--open-browser".to_string(),
        "false".to_string(),
    ];

    let sidecar = app
        .shell()
        .sidecar("quillforge-backend")
        .map_err(|error| error.to_string())?;

    let (mut events, child) = match sidecar.args(args).spawn() {
        Ok(result) => result,
        Err(error) => {
            let message = format!("Unable to launch the QuillForge backend sidecar: {error}");
            set_failed_status(&app, generation, workspace_text, Some(port), message.clone()).await;
            return Err(message);
        }
    };

    {
        let runtime = app.state::<DesktopRuntime>();
        let mut state = runtime.inner.lock().await;
        if generation != state.generation {
            let stale_child = child;
            let _ = stale_child.kill();
            return Ok(());
        }

        state.child = Some(child);
    }

    let app_for_exit = app.clone();
    let workspace_for_exit = workspace_text.clone();
    spawn(async move {
        while let Some(event) = events.recv().await {
            if let CommandEvent::Terminated(terminated) = event {
                handle_backend_exit(
                    app_for_exit.clone(),
                    generation,
                    workspace_for_exit.clone(),
                    port,
                    terminated.code,
                    terminated.signal,
                )
                .await;
                break;
            }
        }
    });

    let app_for_readiness = app.clone();
    spawn(async move {
        match wait_for_backend_ready(&backend_url).await {
            Ok(()) => {
                set_ready_status(
                    &app_for_readiness,
                    generation,
                    workspace_text,
                    port,
                    backend_url,
                )
                .await;
            }
            Err(error) => {
                set_failed_status(&app_for_readiness, generation, workspace_text, Some(port), error).await;
            }
        }
    });

    Ok(())
}

async fn wait_for_backend_ready(backend_url: &str) -> Result<(), String> {
    let client = reqwest::Client::builder()
        .timeout(Duration::from_secs(3))
        .build()
        .map_err(|error| format!("Unable to create readiness client: {error}"))?;

    let ready_url = format!("{backend_url}/api/health/ready");
    let deadline = std::time::Instant::now() + READY_TIMEOUT;
    let mut last_error = "QuillForge backend did not report readiness.".to_string();

    loop {
        if std::time::Instant::now() >= deadline {
            return Err(format!(
                "{last_error} Timed out after {} seconds.",
                READY_TIMEOUT.as_secs()
            ));
        }

        match client.get(&ready_url).send().await {
            Ok(response) if response.status() == StatusCode::OK => return Ok(()),
            Ok(response) if response.status() == StatusCode::SERVICE_UNAVAILABLE => {
                last_error = "QuillForge backend is still starting.".to_string();
            }
            Ok(response) => {
                last_error = format!("Unexpected readiness status: {}", response.status());
            }
            Err(error) => {
                last_error = format!("Waiting for backend readiness: {error}");
            }
        }

        sleep(READY_POLL_INTERVAL).await;
    }
}

async fn set_ready_status(
    app: &AppHandle,
    generation: u64,
    workspace_path: String,
    port: u16,
    backend_url: String,
) {
    let runtime = app.state::<DesktopRuntime>();
    let mut state = runtime.inner.lock().await;
    if generation != state.generation || state.shutting_down {
        return;
    }

    state.status = DesktopShellStatus::ready(workspace_path, port, backend_url);
    emit_status(app, &state.status);
}

async fn set_failed_status(
    app: &AppHandle,
    generation: u64,
    workspace_path: String,
    port: Option<u16>,
    message: String,
) {
    let runtime = app.state::<DesktopRuntime>();
    let mut state = runtime.inner.lock().await;
    if generation != state.generation {
        return;
    }

    if let Some(child) = state.child.take() {
        let _ = child.kill();
    }

    state.status = DesktopShellStatus::failed(workspace_path, port, message);
    emit_status(app, &state.status);
}

async fn handle_backend_exit(
    app: AppHandle,
    generation: u64,
    workspace_path: String,
    port: u16,
    code: Option<i32>,
    signal: Option<i32>,
) {
    let runtime = app.state::<DesktopRuntime>();
    let mut state = runtime.inner.lock().await;
    if generation != state.generation {
        return;
    }

    state.child = None;
    if state.shutting_down {
        state.status = DesktopShellStatus::stopped(workspace_path);
    } else {
        let message = match (code, signal) {
            (Some(exit_code), _) => format!("The backend exited unexpectedly with code {exit_code}."),
            (_, Some(exit_signal)) => format!("The backend stopped unexpectedly with signal {exit_signal}."),
            _ => "The backend stopped unexpectedly.".to_string(),
        };
        state.status = DesktopShellStatus::exited(workspace_path, Some(port), message);
    }
    emit_status(&app, &state.status);
}

async fn shutdown_backend(app: AppHandle) {
    let runtime = app.state::<DesktopRuntime>();
    let mut state = runtime.inner.lock().await;
    state.shutting_down = true;

    if let Some(child) = state.child.take() {
        let _ = child.kill();
    }

    state.status = DesktopShellStatus::stopped(state.status.workspace_path.clone());
}

fn emit_status(app: &AppHandle, status: &DesktopShellStatus) {
    let _ = app.emit(STATUS_EVENT, status.clone());
}

fn reserve_port() -> Result<u16, std::io::Error> {
    let listener = TcpListener::bind("127.0.0.1:0")?;
    let port = listener.local_addr()?.port();
    drop(listener);
    Ok(port)
}

fn resolve_workspace_path() -> PathBuf {
    if let Ok(explicit_path) = env::var("QUILLFORGE_DESKTOP_CONTENT_ROOT") {
        if !explicit_path.trim().is_empty() {
            return PathBuf::from(explicit_path);
        }
    }

    if let Some(documents_root) = document_dir() {
        return documents_root.join("QuillForge");
    }

    if let Some(home_root) = home_dir() {
        return home_root.join("Documents").join("QuillForge");
    }

    env::current_dir()
        .unwrap_or_else(|_| PathBuf::from("."))
        .join("user")
}

fn reveal_in_file_manager(path: &Path) -> Result<(), String> {
    let program_and_args: (&str, Vec<String>) = if cfg!(target_os = "macos") {
        ("open", vec![path.display().to_string()])
    } else if cfg!(target_os = "windows") {
        ("explorer", vec![path.display().to_string()])
    } else {
        ("xdg-open", vec![path.display().to_string()])
    };

    ProcessCommand::new(program_and_args.0)
        .args(program_and_args.1)
        .spawn()
        .map_err(|error| format!("Unable to open workspace folder: {error}"))?;

    Ok(())
}
