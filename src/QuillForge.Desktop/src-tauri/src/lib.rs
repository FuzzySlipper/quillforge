use std::env;
use std::fs;
use std::io::Write;
use std::net::{IpAddr, TcpListener, UdpSocket};
use std::path::{Path, PathBuf};
use std::process::Command as ProcessCommand;
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use atomicwrites::{AtomicFile, OverwriteBehavior};
use dirs::{document_dir, home_dir};
use reqwest::{StatusCode, Url};
use serde::{Deserialize, Serialize};
use tauri::async_runtime::{spawn, Mutex, Receiver};
use tauri::{AppHandle, Emitter, Manager, RunEvent, State};
use tauri_plugin_shell::process::{CommandChild, CommandEvent};
use tauri_plugin_shell::ShellExt;
use tokio::sync::mpsc::error::TryRecvError;
use tokio::time::sleep;

const MAIN_WINDOW_LABEL: &str = "main";
const STATUS_EVENT: &str = "desktop://status";
const READY_POLL_INTERVAL: Duration = Duration::from_millis(500);
const READY_TIMEOUT: Duration = Duration::from_secs(30);
const BACKEND_START_ATTEMPTS: usize = 3;
const BACKEND_START_RETRY_DELAY: Duration = Duration::from_millis(250);
const SETTINGS_FILE_NAME: &str = "desktop-settings.json";
const MAX_DIAGNOSTIC_ENTRIES: usize = 200;
const TARGET_TRIPLE: &str = env!("TAURI_ENV_TARGET_TRIPLE");

#[derive(Clone, Copy, Debug, Serialize, Deserialize, PartialEq, Eq, Default)]
#[serde(rename_all = "lowercase")]
enum DesktopBindMode {
    #[default]
    Loopback,
    Lan,
}

impl DesktopBindMode {
    fn as_str(self) -> &'static str {
        match self {
            Self::Loopback => "loopback",
            Self::Lan => "lan",
        }
    }
}

#[derive(Clone, Debug, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
struct DesktopShellSettings {
    #[serde(default)]
    bind_mode: DesktopBindMode,
}

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct DesktopDiagnosticEntry {
    level: &'static str,
    source: &'static str,
    message: String,
}

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct DesktopShellStatus {
    phase: &'static str,
    message: Option<String>,
    backend_url: Option<String>,
    workspace_path: String,
    port: Option<u16>,
    bind_mode: String,
    loopback_url: Option<String>,
    lan_url: Option<String>,
    restart_available: bool,
    diagnostics: Vec<DesktopDiagnosticEntry>,
}

impl DesktopShellStatus {
    fn starting(
        workspace_path: String,
        port: u16,
        bind_mode: DesktopBindMode,
        message: impl Into<String>,
    ) -> Self {
        Self::for_phase(
            "starting",
            Some(message.into()),
            None,
            workspace_path,
            Some(port),
            bind_mode,
            true,
        )
    }

    fn ready(
        workspace_path: String,
        port: u16,
        backend_url: String,
        bind_mode: DesktopBindMode,
    ) -> Self {
        let message = if bind_mode == DesktopBindMode::Lan {
            if resolve_lan_url(bind_mode, port).is_some() {
                "LAN/mobile access is enabled for this run.".to_string()
            } else {
                "LAN/mobile access is enabled, but no non-loopback address was detected yet."
                    .to_string()
            }
        } else {
            "The local QuillForge backend is ready.".to_string()
        };

        Self::for_phase(
            "ready",
            Some(message),
            Some(backend_url),
            workspace_path,
            Some(port),
            bind_mode,
            true,
        )
    }

    fn failed(
        workspace_path: String,
        port: Option<u16>,
        bind_mode: DesktopBindMode,
        message: impl Into<String>,
    ) -> Self {
        Self::for_phase(
            "failed",
            Some(message.into()),
            None,
            workspace_path,
            port,
            bind_mode,
            true,
        )
    }

    fn exited(
        workspace_path: String,
        port: Option<u16>,
        bind_mode: DesktopBindMode,
        message: impl Into<String>,
    ) -> Self {
        Self::for_phase(
            "exited",
            Some(message.into()),
            None,
            workspace_path,
            port,
            bind_mode,
            true,
        )
    }

    fn stopped(workspace_path: String, bind_mode: DesktopBindMode) -> Self {
        Self::for_phase(
            "stopped",
            Some("Shutting down the QuillForge backend.".to_string()),
            None,
            workspace_path,
            None,
            bind_mode,
            false,
        )
    }

    fn for_phase(
        phase: &'static str,
        message: Option<String>,
        backend_url: Option<String>,
        workspace_path: String,
        port: Option<u16>,
        bind_mode: DesktopBindMode,
        restart_available: bool,
    ) -> Self {
        let loopback_url = port.map(loopback_url_for_port);
        let lan_url = port.and_then(|value| resolve_lan_url(bind_mode, value));

        Self {
            phase,
            message,
            backend_url,
            workspace_path,
            port,
            bind_mode: bind_mode.as_str().to_string(),
            loopback_url,
            lan_url,
            restart_available,
            diagnostics: Vec::new(),
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
            bind_mode: DesktopBindMode::Loopback.as_str().to_string(),
            loopback_url: None,
            lan_url: None,
            restart_available: true,
            diagnostics: Vec::new(),
        }
    }
}

struct RuntimeState {
    generation: u64,
    child: Option<CommandChild>,
    status: DesktopShellStatus,
    settings: DesktopShellSettings,
    diagnostics: Vec<DesktopDiagnosticEntry>,
    shutting_down: bool,
}

impl Default for RuntimeState {
    fn default() -> Self {
        Self {
            generation: 0,
            child: None,
            status: DesktopShellStatus::default(),
            settings: DesktopShellSettings::default(),
            diagnostics: Vec::new(),
            shutting_down: false,
        }
    }
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
async fn set_lan_access_enabled(app: AppHandle, enable_lan: bool) -> Result<(), String> {
    let bind_mode = if enable_lan {
        DesktopBindMode::Lan
    } else {
        DesktopBindMode::Loopback
    };

    let settings = DesktopShellSettings { bind_mode };
    save_desktop_settings(&app, &settings)?;
    update_runtime_settings(&app, settings).await;

    let startup_message = if enable_lan {
        "Restarting the QuillForge backend with LAN/mobile access enabled..."
    } else {
        "Restarting the QuillForge backend in local-only mode..."
    };

    start_backend(app, startup_message).await
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

#[allow(deprecated)]
#[tauri::command]
async fn open_external_url(app: AppHandle, url: String) -> Result<(), String> {
    validate_external_url(&url)?;
    app.shell()
        .open(url, None)
        .map_err(|error| format!("Unable to open URL: {error}"))
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    let builder = tauri::Builder::default()
        .plugin(
            tauri_plugin_log::Builder::default()
                .level(log::LevelFilter::Info)
                .build(),
        )
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
            set_lan_access_enabled,
            open_workspace,
            open_external_url
        ])
        .setup(|app| {
            let app_handle = app.handle().clone();
            spawn(async move {
                hydrate_runtime_settings(&app_handle).await;
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
    let generation = begin_backend_start(&app).await?;
    let bind_mode = current_bind_mode(&app).await;
    let backend_payload_dir = match resolve_backend_payload_dir(&app) {
        Ok(path) => path,
        Err(error) => {
            set_failed_status(
                &app,
                generation,
                workspace_text.clone(),
                None,
                bind_mode,
                error.clone(),
            )
            .await;
            return Err(error);
        }
    };

    let desktop_instance_id = format!(
        "{}-{}",
        std::process::id(),
        SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap_or_default()
            .as_millis()
    );

    for attempt in 1..=BACKEND_START_ATTEMPTS {
        if !is_generation_active(&app, generation).await {
            return Ok(());
        }

        let port = reserve_port().map_err(|error| error.to_string())?;
        let backend_url = loopback_url_for_port(port);
        set_starting_status(
            &app,
            generation,
            workspace_text.clone(),
            port,
            bind_mode,
            format_startup_message(startup_message, attempt),
        )
        .await?;

        let sidecar = app
            .shell()
            .sidecar("quillforge-backend")
            .map_err(|error| error.to_string())?;

        let args = build_backend_args(
            &workspace_text,
            port,
            &desktop_instance_id,
            bind_mode,
            &backend_payload_dir,
        );
        let (mut events, child) = match sidecar.current_dir(&backend_payload_dir).args(args).spawn()
        {
            Ok(result) => result,
            Err(error) => {
                let message = format!("Unable to launch the QuillForge backend sidecar: {error}");
                set_failed_status(
                    &app,
                    generation,
                    workspace_text.clone(),
                    Some(port),
                    bind_mode,
                    message.clone(),
                )
                .await;
                return Err(message);
            }
        };

        if !attach_child(&app, generation, child).await {
            return Ok(());
        }

        match wait_for_backend_startup(&app, generation, &backend_url, &mut events).await {
            Ok(()) => {
                set_ready_status(
                    &app,
                    generation,
                    workspace_text.clone(),
                    port,
                    backend_url,
                    bind_mode,
                )
                .await;

                let app_for_exit = app.clone();
                let workspace_for_exit = workspace_text.clone();
                spawn(async move {
                    while let Some(event) = events.recv().await {
                        match event {
                            CommandEvent::Stdout(bytes) => {
                                append_backend_output(
                                    &app_for_exit,
                                    generation,
                                    "backend",
                                    "info",
                                    bytes,
                                )
                                .await;
                            }
                            CommandEvent::Stderr(bytes) => {
                                append_backend_output(
                                    &app_for_exit,
                                    generation,
                                    "backend",
                                    "warning",
                                    bytes,
                                )
                                .await;
                            }
                            CommandEvent::Error(error) => {
                                append_diagnostic(
                                    &app_for_exit,
                                    Some(generation),
                                    "error",
                                    "shell",
                                    format!("Backend stream error: {error}"),
                                )
                                .await;
                            }
                            CommandEvent::Terminated(terminated) => {
                                handle_backend_exit(
                                    app_for_exit.clone(),
                                    generation,
                                    workspace_for_exit.clone(),
                                    port,
                                    bind_mode,
                                    terminated.code,
                                    terminated.signal,
                                )
                                .await;
                                break;
                            }
                            _ => {}
                        }
                    }
                });

                return Ok(());
            }
            Err(error) => {
                clear_child(&app, generation).await;

                if !is_generation_active(&app, generation).await {
                    return Ok(());
                }

                if attempt == BACKEND_START_ATTEMPTS {
                    set_failed_status(
                        &app,
                        generation,
                        workspace_text.clone(),
                        Some(port),
                        bind_mode,
                        error.clone(),
                    )
                    .await;
                    return Err(error);
                }

                sleep(BACKEND_START_RETRY_DELAY).await;
            }
        }
    }

    let message = "Unable to launch the QuillForge backend sidecar.".to_string();
    set_failed_status(
        &app,
        generation,
        workspace_text,
        None,
        bind_mode,
        message.clone(),
    )
    .await;
    Err(message)
}

async fn wait_for_backend_startup(
    app: &AppHandle,
    generation: u64,
    backend_url: &str,
    events: &mut Receiver<CommandEvent>,
) -> Result<(), String> {
    let client = reqwest::Client::builder()
        .timeout(Duration::from_secs(3))
        .build()
        .map_err(|error| format!("Unable to create readiness client: {error}"))?;

    let ready_url = format!("{backend_url}/api/health/ready");
    let deadline = std::time::Instant::now() + READY_TIMEOUT;
    let mut last_error = "QuillForge backend did not report readiness.".to_string();

    loop {
        match events.try_recv() {
            Ok(CommandEvent::Terminated(terminated)) => {
                return Err(format_terminated_message(
                    "during startup",
                    terminated.code,
                    terminated.signal,
                ));
            }
            Ok(CommandEvent::Stdout(bytes)) => {
                append_backend_output(app, generation, "backend", "info", bytes).await;
            }
            Ok(CommandEvent::Stderr(bytes)) => {
                append_backend_output(app, generation, "backend", "warning", bytes).await;
            }
            Ok(CommandEvent::Error(error)) => {
                append_diagnostic(
                    app,
                    Some(generation),
                    "error",
                    "shell",
                    format!("Backend stream error: {error}"),
                )
                .await;
            }
            Ok(_) => {}
            Err(TryRecvError::Empty) => {}
            Err(TryRecvError::Disconnected) => {
                return Err("The backend sidecar exited before reporting readiness.".to_string());
            }
        }

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
    bind_mode: DesktopBindMode,
) {
    let runtime = app.state::<DesktopRuntime>();
    let status = {
        let mut state = runtime.inner.lock().await;
        if generation != state.generation || state.shutting_down {
            return;
        }

        let status_message = if bind_mode == DesktopBindMode::Lan {
            if resolve_lan_url(bind_mode, port).is_some() {
                "LAN/mobile access is enabled for this run.".to_string()
            } else {
                "LAN/mobile access is enabled, but no non-loopback address was detected yet."
                    .to_string()
            }
        } else {
            "The local QuillForge backend is ready.".to_string()
        };
        push_diagnostic(&mut state.diagnostics, "info", "shell", status_message);
        apply_status(
            &mut state,
            DesktopShellStatus::ready(workspace_path, port, backend_url, bind_mode),
        );
        state.status.clone()
    };

    emit_status(app, &status);
}

async fn set_failed_status(
    app: &AppHandle,
    generation: u64,
    workspace_path: String,
    port: Option<u16>,
    bind_mode: DesktopBindMode,
    message: String,
) {
    let runtime = app.state::<DesktopRuntime>();
    let status = {
        let mut state = runtime.inner.lock().await;
        if generation != state.generation {
            return;
        }

        if let Some(child) = state.child.take() {
            let _ = child.kill();
        }

        push_diagnostic(&mut state.diagnostics, "error", "shell", message.clone());
        apply_status(
            &mut state,
            DesktopShellStatus::failed(workspace_path, port, bind_mode, message),
        );
        state.status.clone()
    };

    emit_status(app, &status);
}

async fn handle_backend_exit(
    app: AppHandle,
    generation: u64,
    workspace_path: String,
    port: u16,
    bind_mode: DesktopBindMode,
    code: Option<i32>,
    signal: Option<i32>,
) {
    let runtime = app.state::<DesktopRuntime>();
    let status = {
        let mut state = runtime.inner.lock().await;
        if generation != state.generation {
            return;
        }

        state.child = None;
        if state.shutting_down {
            push_diagnostic(
                &mut state.diagnostics,
                "info",
                "shell",
                "Shutting down the QuillForge backend.",
            );
            apply_status(
                &mut state,
                DesktopShellStatus::stopped(workspace_path, bind_mode),
            );
        } else {
            let message = match (code, signal) {
                (Some(exit_code), _) => {
                    format!("The backend exited unexpectedly with code {exit_code}.")
                }
                (_, Some(exit_signal)) => {
                    format!("The backend stopped unexpectedly with signal {exit_signal}.")
                }
                _ => "The backend stopped unexpectedly.".to_string(),
            };
            push_diagnostic(&mut state.diagnostics, "error", "shell", message.clone());
            apply_status(
                &mut state,
                DesktopShellStatus::exited(workspace_path, Some(port), bind_mode, message),
            );
        }
        state.status.clone()
    };

    emit_status(&app, &status);
}

async fn shutdown_backend(app: AppHandle) {
    let runtime = app.state::<DesktopRuntime>();
    let status = {
        let mut state = runtime.inner.lock().await;
        state.shutting_down = true;
        let workspace_path = state.status.workspace_path.clone();
        let bind_mode = state.settings.bind_mode;

        if let Some(child) = state.child.take() {
            let _ = child.kill();
        }

        push_diagnostic(
            &mut state.diagnostics,
            "info",
            "shell",
            "Shutting down the QuillForge backend.",
        );
        apply_status(
            &mut state,
            DesktopShellStatus::stopped(workspace_path, bind_mode),
        );
        state.status.clone()
    };

    emit_status(&app, &status);
}

fn emit_status(app: &AppHandle, status: &DesktopShellStatus) {
    let _ = app.emit(STATUS_EVENT, status.clone());
}

fn apply_status(state: &mut RuntimeState, mut status: DesktopShellStatus) {
    status.diagnostics = state.diagnostics.clone();
    state.status = status;
}

fn push_diagnostic(
    diagnostics: &mut Vec<DesktopDiagnosticEntry>,
    level: &'static str,
    source: &'static str,
    message: impl Into<String>,
) {
    let message = message.into();
    if message.trim().is_empty() {
        return;
    }

    diagnostics.push(DesktopDiagnosticEntry {
        level,
        source,
        message,
    });

    if diagnostics.len() > MAX_DIAGNOSTIC_ENTRIES {
        let overflow = diagnostics.len() - MAX_DIAGNOSTIC_ENTRIES;
        diagnostics.drain(0..overflow);
    }
}

fn apply_diagnostic_to_status(state: &mut RuntimeState) {
    state.status.diagnostics = state.diagnostics.clone();
}

async fn append_diagnostic(
    app: &AppHandle,
    generation: Option<u64>,
    level: &'static str,
    source: &'static str,
    message: impl Into<String>,
) {
    let runtime = app.state::<DesktopRuntime>();
    let status = {
        let mut state = runtime.inner.lock().await;
        if let Some(expected_generation) = generation {
            if expected_generation != state.generation {
                return;
            }
        }

        push_diagnostic(&mut state.diagnostics, level, source, message);
        apply_diagnostic_to_status(&mut state);
        state.status.clone()
    };

    emit_status(app, &status);
}

async fn append_backend_output(
    app: &AppHandle,
    generation: u64,
    source: &'static str,
    default_level: &'static str,
    bytes: Vec<u8>,
) {
    let rendered = String::from_utf8_lossy(&bytes);
    let lines = rendered
        .lines()
        .map(str::trim)
        .filter(|line| !line.is_empty())
        .map(str::to_string)
        .collect::<Vec<_>>();

    if lines.is_empty() {
        return;
    }

    let runtime = app.state::<DesktopRuntime>();
    let status = {
        let mut state = runtime.inner.lock().await;
        if generation != state.generation {
            return;
        }

        for line in lines {
            let level = classify_diagnostic_level(default_level, &line);
            push_diagnostic(&mut state.diagnostics, level, source, line);
        }

        apply_diagnostic_to_status(&mut state);
        state.status.clone()
    };

    emit_status(app, &status);
}

fn classify_diagnostic_level(default_level: &'static str, line: &str) -> &'static str {
    let trimmed = line.trim_start();
    if trimmed.starts_with("error:") || trimmed.starts_with("fail:") {
        return "error";
    }

    if trimmed.starts_with("warn:") {
        return "warning";
    }

    default_level
}

async fn begin_backend_start(app: &AppHandle) -> Result<u64, String> {
    let runtime = app.state::<DesktopRuntime>();
    let mut state = runtime.inner.lock().await;
    if state.shutting_down {
        return Err("QuillForge desktop is shutting down.".to_string());
    }

    state.generation += 1;
    let generation = state.generation;

    if let Some(existing_child) = state.child.take() {
        let _ = existing_child.kill();
    }

    Ok(generation)
}

async fn set_starting_status(
    app: &AppHandle,
    generation: u64,
    workspace_path: String,
    port: u16,
    bind_mode: DesktopBindMode,
    message: String,
) -> Result<(), String> {
    let runtime = app.state::<DesktopRuntime>();
    let status = {
        let mut state = runtime.inner.lock().await;
        if generation != state.generation || state.shutting_down {
            return Err("QuillForge desktop is shutting down.".to_string());
        }

        push_diagnostic(&mut state.diagnostics, "info", "shell", message.clone());
        apply_status(
            &mut state,
            DesktopShellStatus::starting(workspace_path, port, bind_mode, message),
        );
        state.status.clone()
    };

    emit_status(app, &status);
    Ok(())
}

async fn attach_child(app: &AppHandle, generation: u64, child: CommandChild) -> bool {
    let runtime = app.state::<DesktopRuntime>();
    let mut state = runtime.inner.lock().await;
    if generation != state.generation || state.shutting_down {
        let _ = child.kill();
        return false;
    }

    state.child = Some(child);
    true
}

async fn clear_child(app: &AppHandle, generation: u64) {
    let runtime = app.state::<DesktopRuntime>();
    let mut state = runtime.inner.lock().await;
    if generation != state.generation {
        return;
    }

    if let Some(child) = state.child.take() {
        let _ = child.kill();
    }
}

async fn is_generation_active(app: &AppHandle, generation: u64) -> bool {
    let runtime = app.state::<DesktopRuntime>();
    let state = runtime.inner.lock().await;
    generation == state.generation && !state.shutting_down
}

async fn current_bind_mode(app: &AppHandle) -> DesktopBindMode {
    let runtime = app.state::<DesktopRuntime>();
    let state = runtime.inner.lock().await;
    state.settings.bind_mode
}

async fn hydrate_runtime_settings(app: &AppHandle) {
    let settings = load_desktop_settings(app);
    update_runtime_settings(app, settings).await;
}

async fn update_runtime_settings(app: &AppHandle, settings: DesktopShellSettings) {
    let runtime = app.state::<DesktopRuntime>();
    let mut state = runtime.inner.lock().await;
    state.settings = settings.clone();
    state.status.bind_mode = settings.bind_mode.as_str().to_string();
}

fn load_desktop_settings(app: &AppHandle) -> DesktopShellSettings {
    let settings_path = match desktop_settings_path(app) {
        Ok(path) => path,
        Err(error) => {
            log::warn!("{error}");
            return DesktopShellSettings::default();
        }
    };

    let contents = match fs::read_to_string(&settings_path) {
        Ok(contents) => contents,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
            return DesktopShellSettings::default();
        }
        Err(error) => {
            log::warn!(
                "Unable to read desktop settings from {}: {error}",
                settings_path.display()
            );
            return DesktopShellSettings::default();
        }
    };

    match serde_json::from_str::<DesktopShellSettings>(&contents) {
        Ok(settings) => settings,
        Err(error) => {
            log::warn!(
                "Unable to parse desktop settings from {}: {error}",
                settings_path.display()
            );
            DesktopShellSettings::default()
        }
    }
}

fn save_desktop_settings(app: &AppHandle, settings: &DesktopShellSettings) -> Result<(), String> {
    let settings_path = desktop_settings_path(app)?;
    if let Some(parent) = settings_path.parent() {
        fs::create_dir_all(parent).map_err(|error| {
            format!(
                "Unable to create the desktop settings directory {}: {error}",
                parent.display()
            )
        })?;
    }

    let payload = serde_json::to_vec_pretty(settings)
        .map_err(|error| format!("Unable to serialize desktop settings: {error}"))?;
    let atomic_file = AtomicFile::new(&settings_path, OverwriteBehavior::AllowOverwrite);
    atomic_file
        .write(|file| {
            file.write_all(&payload)?;
            file.write_all(b"\n")?;
            file.sync_all()
        })
        .map_err(|error| format!("Unable to save desktop settings: {error}"))?;

    Ok(())
}

fn desktop_settings_path(app: &AppHandle) -> Result<PathBuf, String> {
    let config_dir = app
        .path()
        .app_config_dir()
        .map_err(|error| format!("Unable to resolve the desktop settings directory: {error}"))?;
    Ok(config_dir.join(SETTINGS_FILE_NAME))
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

fn build_backend_args(
    workspace_path: &str,
    port: u16,
    desktop_instance_id: &str,
    bind_mode: DesktopBindMode,
    runtime_root: &Path,
) -> Vec<String> {
    vec![
        "--desktop-mode".to_string(),
        "--content-root".to_string(),
        workspace_path.to_string(),
        "--runtime-root".to_string(),
        runtime_root.display().to_string(),
        "--bind-mode".to_string(),
        bind_mode.as_str().to_string(),
        "--port".to_string(),
        port.to_string(),
        "--desktop-instance-id".to_string(),
        desktop_instance_id.to_string(),
        "--open-browser".to_string(),
        "false".to_string(),
    ]
}

fn format_startup_message(base_message: &str, attempt: usize) -> String {
    if attempt == 1 {
        return base_message.to_string();
    }

    format!(
        "{base_message} Retrying backend launch on a new port ({attempt}/{BACKEND_START_ATTEMPTS})."
    )
}

fn format_terminated_message(phase: &str, code: Option<i32>, signal: Option<i32>) -> String {
    match (code, signal) {
        (Some(exit_code), _) => format!("The backend exited {phase} with code {exit_code}."),
        (_, Some(exit_signal)) => format!("The backend stopped {phase} with signal {exit_signal}."),
        _ => format!("The backend stopped unexpectedly {phase}."),
    }
}

fn loopback_url_for_port(port: u16) -> String {
    format!("http://127.0.0.1:{port}")
}

fn resolve_backend_payload_dir(app: &AppHandle) -> Result<PathBuf, String> {
    let mut candidates = Vec::new();

    if let Ok(resource_dir) = app.path().resource_dir() {
        candidates.push(
            resource_dir
                .join("resources")
                .join("backend-payload")
                .join(TARGET_TRIPLE),
        );
        candidates.push(resource_dir.join("backend-payload").join(TARGET_TRIPLE));
    }

    candidates.push(
        PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .join("resources")
            .join("backend-payload")
            .join(TARGET_TRIPLE),
    );
    candidates.push(
        PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .join("..")
            .join(".sidecar-publish")
            .join(TARGET_TRIPLE),
    );

    for candidate in &candidates {
        if candidate.join("wwwroot").join("index.html").is_file() {
            return Ok(candidate.clone());
        }
    }

    let searched_paths = candidates
        .iter()
        .map(|path| path.display().to_string())
        .collect::<Vec<_>>()
        .join(", ");
    Err(format!("Unable to locate the bundled QuillForge backend payload for target {TARGET_TRIPLE}. Expected a directory containing wwwroot/index.html. Looked in: {searched_paths}"))
}

fn resolve_lan_url(bind_mode: DesktopBindMode, port: u16) -> Option<String> {
    if bind_mode != DesktopBindMode::Lan {
        return None;
    }

    detect_primary_lan_ip().map(|ip| format!("http://{ip}:{port}"))
}

fn detect_primary_lan_ip() -> Option<IpAddr> {
    for target in ["1.1.1.1:80", "8.8.8.8:80", "192.0.2.1:80"] {
        let socket = UdpSocket::bind("0.0.0.0:0").ok()?;
        if socket.connect(target).is_err() {
            continue;
        }

        let ip = socket.local_addr().ok()?.ip();
        if is_usable_lan_ip(ip) {
            return Some(ip);
        }
    }

    None
}

fn is_usable_lan_ip(ip: IpAddr) -> bool {
    match ip {
        IpAddr::V4(value) => {
            value.is_private()
                && !value.is_loopback()
                && !value.is_link_local()
                && !value.is_unspecified()
        }
        IpAddr::V6(_) => false,
    }
}

fn validate_external_url(value: &str) -> Result<(), String> {
    let url = Url::parse(value).map_err(|error| format!("Invalid URL '{value}': {error}"))?;
    if !matches!(url.scheme(), "http" | "https") {
        return Err(
            "Only http:// and https:// URLs can be opened from the desktop shell.".to_string(),
        );
    }

    if url.host_str().is_none() {
        return Err(format!("URL '{value}' is missing a host."));
    }

    Ok(())
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

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn push_diagnostic_keeps_only_most_recent_entries() {
        let mut diagnostics = Vec::new();
        for index in 0..(MAX_DIAGNOSTIC_ENTRIES + 5) {
            push_diagnostic(&mut diagnostics, "info", "shell", format!("entry {index}"));
        }

        assert_eq!(diagnostics.len(), MAX_DIAGNOSTIC_ENTRIES);
        assert_eq!(
            diagnostics.first().map(|entry| entry.message.as_str()),
            Some("entry 5")
        );
        assert_eq!(
            diagnostics.last().map(|entry| entry.message.clone()),
            Some(format!("entry {}", MAX_DIAGNOSTIC_ENTRIES + 4))
        );
    }

    #[test]
    fn classify_diagnostic_level_prefers_known_log_prefixes() {
        assert_eq!(
            classify_diagnostic_level("info", "warn: static files unavailable"),
            "warning"
        );
        assert_eq!(
            classify_diagnostic_level("warning", "error: backend failed"),
            "error"
        );
        assert_eq!(
            classify_diagnostic_level("warning", "plain output"),
            "warning"
        );
    }
}
