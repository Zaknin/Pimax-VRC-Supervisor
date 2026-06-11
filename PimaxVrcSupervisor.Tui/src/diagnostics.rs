use std::{
    ffi::OsString,
    fs::{self, OpenOptions},
    io::Write,
    path::{Path, PathBuf},
    sync::{Arc, Mutex},
    time::{Duration, Instant},
};

use serde_json::{Value, json};

const DEFAULT_INTERVAL: Duration = Duration::from_secs(20);
const LOG_FILE_NAME: &str = "PimaxVrcSupervisorTui.diagnostics.log";

#[derive(Debug, Clone)]
pub struct DiagnosticsHandle {
    inner: Arc<Mutex<DiagnosticsInner>>,
}

#[derive(Debug)]
pub struct TuiDiagnostics {
    handle: DiagnosticsHandle,
}

#[derive(Debug)]
struct DiagnosticsInner {
    enabled: bool,
    log_path: PathBuf,
    interval: Duration,
    interval_started_at: Instant,
    connected: bool,
    counters: DiagnosticsCounters,
}

#[derive(Debug, Default)]
struct DiagnosticsCounters {
    renders: u64,
    refreshes: u64,
    input_wakeups: u64,
    bridge_calls: u64,
    bridge_failures: u64,
    bridge_timeouts: u64,
    bridge_ms_min: Option<u128>,
    bridge_ms_sum: u128,
    bridge_ms_max: u128,
    actions_started: u64,
    lifecycle_requests: u64,
    connection_changes: u64,
}

impl TuiDiagnostics {
    pub fn from_args<I>(args: I) -> Self
    where
        I: IntoIterator<Item = OsString>,
    {
        let config_path = resolve_config_path(args);
        let Some(config_path) = config_path else {
            return Self::disabled();
        };

        let Some(config) = load_config(&config_path) else {
            return Self::disabled();
        };

        if !config.enabled {
            return Self::disabled();
        }

        Self::enabled(config)
    }

    pub fn disabled() -> Self {
        let inner = DiagnosticsInner {
            enabled: false,
            log_path: PathBuf::new(),
            interval: DEFAULT_INTERVAL,
            interval_started_at: Instant::now(),
            connected: false,
            counters: DiagnosticsCounters::default(),
        };

        Self {
            handle: DiagnosticsHandle {
                inner: Arc::new(Mutex::new(inner)),
            },
        }
    }

    fn enabled(config: DiagnosticsConfig) -> Self {
        let inner = DiagnosticsInner {
            enabled: true,
            log_path: config.log_path,
            interval: config.interval,
            interval_started_at: Instant::now(),
            connected: false,
            counters: DiagnosticsCounters::default(),
        };

        Self {
            handle: DiagnosticsHandle {
                inner: Arc::new(Mutex::new(inner)),
            },
        }
    }

    pub fn handle(&self) -> DiagnosticsHandle {
        self.handle.clone()
    }

    pub fn record_render(&self) {
        self.handle.record_render();
    }

    pub fn record_refresh(&self) {
        self.handle.record_refresh();
    }

    pub fn record_input_wakeup(&self) {
        self.handle.record_input_wakeup();
    }

    pub fn record_action_started(&self) {
        self.handle.record_action_started();
    }

    pub fn record_lifecycle_request(&self) {
        self.handle.record_lifecycle_request();
    }

    pub fn record_connection(&self, connected: bool) {
        self.handle.record_connection(connected);
    }

    pub fn maybe_write(&self, now: Instant) {
        self.handle.maybe_write(now);
    }
}

impl DiagnosticsHandle {
    pub fn record_render(&self) {
        self.with_inner(|inner| {
            inner.counters.renders = inner.counters.renders.saturating_add(1);
        });
    }

    pub fn record_refresh(&self) {
        self.with_inner(|inner| {
            inner.counters.refreshes = inner.counters.refreshes.saturating_add(1);
        });
    }

    pub fn record_input_wakeup(&self) {
        self.with_inner(|inner| {
            inner.counters.input_wakeups = inner.counters.input_wakeups.saturating_add(1);
        });
    }

    pub fn record_bridge_call(&self, elapsed: Duration, success: bool, timeout: bool) {
        self.with_inner(|inner| {
            let counters = &mut inner.counters;
            counters.bridge_calls = counters.bridge_calls.saturating_add(1);
            if !success {
                counters.bridge_failures = counters.bridge_failures.saturating_add(1);
            }
            if timeout {
                counters.bridge_timeouts = counters.bridge_timeouts.saturating_add(1);
            }

            let elapsed_ms = elapsed.as_millis();
            counters.bridge_ms_min = Some(
                counters
                    .bridge_ms_min
                    .map(|current| current.min(elapsed_ms))
                    .unwrap_or(elapsed_ms),
            );
            counters.bridge_ms_sum = counters.bridge_ms_sum.saturating_add(elapsed_ms);
            counters.bridge_ms_max = counters.bridge_ms_max.max(elapsed_ms);
        });
    }

    pub fn record_action_started(&self) {
        self.with_inner(|inner| {
            inner.counters.actions_started = inner.counters.actions_started.saturating_add(1);
        });
    }

    pub fn record_lifecycle_request(&self) {
        self.with_inner(|inner| {
            inner.counters.lifecycle_requests = inner.counters.lifecycle_requests.saturating_add(1);
        });
    }

    pub fn record_connection(&self, connected: bool) {
        self.with_inner(|inner| {
            if inner.connected != connected {
                inner.connected = connected;
                inner.counters.connection_changes =
                    inner.counters.connection_changes.saturating_add(1);
            }
        });
    }

    pub fn maybe_write(&self, now: Instant) {
        let summary = {
            let Ok(mut inner) = self.inner.lock() else {
                return;
            };

            if !inner.enabled || now.duration_since(inner.interval_started_at) < inner.interval {
                return;
            }

            let elapsed = now.duration_since(inner.interval_started_at);
            let summary = inner.build_summary(elapsed);
            inner.interval_started_at = now;
            inner.counters = DiagnosticsCounters::default();
            Some((inner.log_path.clone(), summary))
        };

        let Some((path, summary)) = summary else {
            return;
        };

        if let Some(parent) = path.parent() {
            let _ = fs::create_dir_all(parent);
        }

        if let Ok(mut file) = OpenOptions::new().create(true).append(true).open(path) {
            let _ = writeln!(file, "{summary}");
        }
    }

    fn with_inner(&self, action: impl FnOnce(&mut DiagnosticsInner)) {
        let Ok(mut inner) = self.inner.lock() else {
            return;
        };

        if inner.enabled {
            action(&mut inner);
        }
    }
}

impl DiagnosticsInner {
    fn build_summary(&self, elapsed: Duration) -> String {
        let counters = &self.counters;
        let bridge_avg = if counters.bridge_calls == 0 {
            0
        } else {
            counters.bridge_ms_sum / u128::from(counters.bridge_calls)
        };

        json!({
            "interval_seconds": elapsed.as_secs_f64(),
            "pid": std::process::id(),
            "connected": self.connected,
            "renders": counters.renders,
            "refreshes": counters.refreshes,
            "input_wakeups": counters.input_wakeups,
            "bridge_calls": counters.bridge_calls,
            "bridge_failures": counters.bridge_failures,
            "bridge_timeouts": counters.bridge_timeouts,
            "bridge_ms_min": counters.bridge_ms_min.unwrap_or(0),
            "bridge_ms_avg": bridge_avg,
            "bridge_ms_max": counters.bridge_ms_max,
            "actions_started": counters.actions_started,
            "lifecycle_requests": counters.lifecycle_requests,
            "connection_changes": counters.connection_changes
        })
        .to_string()
    }
}

#[derive(Debug)]
struct DiagnosticsConfig {
    enabled: bool,
    interval: Duration,
    log_path: PathBuf,
}

fn load_config(path: &Path) -> Option<DiagnosticsConfig> {
    let text = fs::read_to_string(path).ok()?;
    let root = serde_json::from_str::<Value>(&text).ok()?;
    let enabled = root
        .get("DiagnosticsLogDesktopTui")
        .and_then(Value::as_bool)
        .unwrap_or(false);

    if !enabled {
        return Some(DiagnosticsConfig {
            enabled: false,
            interval: DEFAULT_INTERVAL,
            log_path: PathBuf::new(),
        });
    }

    let interval_seconds = root
        .get("DiagnosticsSummaryIntervalSeconds")
        .and_then(Value::as_u64)
        .filter(|value| *value > 0)
        .unwrap_or(DEFAULT_INTERVAL.as_secs());
    let log_directory = root
        .get("DiagnosticsLogDirectory")
        .and_then(Value::as_str)
        .filter(|value| !value.trim().is_empty())
        .map(expand_windows_env_vars)
        .map(PathBuf::from)
        .unwrap_or_else(default_diagnostics_dir);

    Some(DiagnosticsConfig {
        enabled,
        interval: Duration::from_secs(interval_seconds),
        log_path: log_directory.join(LOG_FILE_NAME),
    })
}

fn resolve_config_path<I>(args: I) -> Option<PathBuf>
where
    I: IntoIterator<Item = OsString>,
{
    let mut args = args.into_iter().skip(1);
    while let Some(arg) = args.next() {
        if arg == "--config" {
            return args.next().map(PathBuf::from);
        }
    }

    std::env::current_exe()
        .ok()
        .and_then(|path| {
            path.parent()
                .map(|parent| parent.join("supervisor.config.json"))
        })
        .filter(|path| path.exists())
        .or_else(|| {
            std::env::current_dir()
                .ok()
                .map(|directory| directory.join("supervisor.config.json"))
                .filter(|path| path.exists())
        })
}

fn default_diagnostics_dir() -> PathBuf {
    std::env::temp_dir().join("PimaxVrcSupervisorDiagnostics")
}

fn expand_windows_env_vars(value: &str) -> String {
    let mut output = String::with_capacity(value.len());
    let mut chars = value.chars().peekable();

    while let Some(ch) = chars.next() {
        if ch != '%' {
            output.push(ch);
            continue;
        }

        let mut name = String::new();
        while let Some(&next) = chars.peek() {
            chars.next();
            if next == '%' {
                break;
            }
            name.push(next);
        }

        if name.is_empty() {
            output.push('%');
        } else if let Ok(replacement) = std::env::var(&name) {
            output.push_str(&replacement);
        } else {
            output.push('%');
            output.push_str(&name);
            output.push('%');
        }
    }

    output
}
