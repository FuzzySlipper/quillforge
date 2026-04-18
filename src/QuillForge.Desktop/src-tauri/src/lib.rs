use std::env;
use std::fs;
use std::io::Write;
use std::net::{IpAddr, TcpListener, UdpSocket};
use std::path::{Path, PathBuf};
use std::process::Command as ProcessCommand;
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use atomicwrites::{AtomicFile, OverwriteBehavior};
use dirs::{document_dir, home_dir};
use reqwest::StatusCode;
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
        }
    }
}

struct RuntimeState {
    generation: u64,
    child: Option<CommandChild>,
    status: DesktopShellStatus,
    settings: DesktopShellSettings,
    shutting_down: bool,
}

impl Default for RuntimeState {
    fn default() -> Self {
        Self {
            generation: 0,
            child: None,
            status: DesktopShellStatus::default(),
            settings: DesktopShellSettings::default(),
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
            open_workspace
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

        let args = build_backend_args(&workspace_text, port, &desktop_instance_id, bind_mode);
        let (mut events, child) = match sidecar.args(args).spawn() {
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

        match wait_for_backend_startup(&backend_url, &mut events).await {
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
                        if let CommandEvent::Terminated(terminated) = event {
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

        state.status = DesktopShellStatus::ready(workspace_path, port, backend_url, bind_mode);
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

        state.status = DesktopShellStatus::failed(workspace_path, port, bind_mode, message);
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
            state.status = DesktopShellStatus::stopped(workspace_path, bind_mode);
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
            state.status =
                DesktopShellStatus::exited(workspace_path, Some(port), bind_mode, message);
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

        if let Some(child) = state.child.take() {
            let _ = child.kill();
        }

        state.status = DesktopShellStatus::stopped(
            state.status.workspace_path.clone(),
            state.settings.bind_mode,
        );
        state.status.clone()
    };

    emit_status(&app, &status);
}

fn emit_status(app: &AppHandle, status: &DesktopShellStatus) {
    let _ = app.emit(STATUS_EVENT, status.clone());
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

        state.status = DesktopShellStatus::starting(workspace_path, port, bind_mode, message);
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
) -> Vec<String> {
    vec![
        "--desktop-mode".to_string(),
        "--content-root".to_string(),
        workspace_path.to_string(),
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
