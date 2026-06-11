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
    config_path: Option<PathBuf>,
    log_path: PathBuf,
    interval: Duration,
    interval_started_at: Instant,
    connected: bool,
    counters: DiagnosticsCounters,
    process_metrics: ProcessMetricsSampler,
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
            config_path: None,
            log_path: PathBuf::new(),
            interval: DEFAULT_INTERVAL,
            interval_started_at: Instant::now(),
            connected: false,
            counters: DiagnosticsCounters::default(),
            process_metrics: ProcessMetricsSampler::new(),
        };

        Self {
            handle: DiagnosticsHandle {
                inner: Arc::new(Mutex::new(inner)),
            },
        }
    }

    fn enabled(mut config: DiagnosticsConfig) -> Self {
        let Some(log_path) = resolve_writable_log_path(&config.log_path) else {
            return Self::disabled();
        };
        config.log_path = log_path;

        let inner = DiagnosticsInner {
            enabled: true,
            config_path: Some(config.config_path),
            log_path: config.log_path,
            interval: config.interval,
            interval_started_at: Instant::now(),
            connected: false,
            counters: DiagnosticsCounters::default(),
            process_metrics: ProcessMetricsSampler::new(),
        };

        let diagnostics = Self {
            handle: DiagnosticsHandle {
                inner: Arc::new(Mutex::new(inner)),
            },
        };
        diagnostics.write_startup_marker();
        diagnostics
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

    fn write_startup_marker(&self) {
        self.handle.write_startup_marker();
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

        let _ = append_line(&path, &summary);
    }

    pub fn write_startup_marker(&self) {
        let marker = {
            let Ok(inner) = self.inner.lock() else {
                return;
            };

            if !inner.enabled {
                return;
            }

            let marker = json!({
                "event": "desktop_tui_diagnostics_started",
                "config_path": inner
                    .config_path
                    .as_ref()
                    .map(path_to_string)
                    .unwrap_or_default(),
                "log_path": path_to_string(&inner.log_path),
                "interval_seconds": inner.interval.as_secs()
            })
            .to_string();
            Some((inner.log_path.clone(), marker))
        };

        let Some((path, marker)) = marker else {
            return;
        };

        let _ = append_line(&path, &marker);
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
    fn build_summary(&mut self, elapsed: Duration) -> String {
        let counters = &self.counters;
        let bridge_avg = if counters.bridge_calls == 0 {
            0
        } else {
            counters.bridge_ms_sum / u128::from(counters.bridge_calls)
        };
        let process = self.process_metrics.sample(elapsed);

        json!({
            "event": "desktop_tui_diagnostics_summary",
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
            "connection_changes": counters.connection_changes,
            "tui_cpu_percent": process.cpu_percent,
            "tui_cpu_time_delta_ms": process.cpu_time_delta_ms,
            "tui_cpu_time_total_ms": process.cpu_time_total_ms,
            "tui_working_set_mb": process.working_set_mb,
            "tui_private_memory_mb": process.private_memory_mb,
            "tui_thread_count": process.thread_count,
            "tui_handle_count": process.handle_count
        })
        .to_string()
    }
}

#[derive(Debug, Default)]
struct ProcessMetricsSampler {
    previous_cpu_total_100ns: Option<u64>,
    logical_cpu_count: f64,
}

#[derive(Debug, Default)]
struct ProcessMetrics {
    cpu_percent: Option<f64>,
    cpu_time_delta_ms: Option<u64>,
    cpu_time_total_ms: Option<u64>,
    working_set_mb: Option<f64>,
    private_memory_mb: Option<f64>,
    thread_count: Option<u32>,
    handle_count: Option<u32>,
}

impl ProcessMetricsSampler {
    fn new() -> Self {
        Self {
            previous_cpu_total_100ns: platform_process_metrics::cpu_total_100ns(),
            logical_cpu_count: std::thread::available_parallelism()
                .map(|value| value.get().max(1) as f64)
                .unwrap_or(1.0),
        }
    }

    fn sample(&mut self, elapsed: Duration) -> ProcessMetrics {
        let snapshot = platform_process_metrics::snapshot();
        let cpu_total_100ns = snapshot.cpu_total_100ns;
        let previous_cpu_total_100ns = self.previous_cpu_total_100ns;
        if cpu_total_100ns.is_some() {
            self.previous_cpu_total_100ns = cpu_total_100ns;
        }

        let cpu_delta_100ns = match (previous_cpu_total_100ns, cpu_total_100ns) {
            (Some(previous), Some(current)) if current >= previous => Some(current - previous),
            _ => None,
        };
        let cpu_time_delta_ms = cpu_delta_100ns.map(|value| value / 10_000);
        let cpu_percent = cpu_delta_100ns
            .and_then(|delta| {
                let denominator = elapsed.as_secs_f64() * self.logical_cpu_count;
                if denominator <= 0.0 {
                    return None;
                }

                Some(((delta as f64 / 10_000_000.0) / denominator) * 100.0)
            })
            .and_then(sanitize_f64);

        ProcessMetrics {
            cpu_percent,
            cpu_time_delta_ms,
            cpu_time_total_ms: cpu_total_100ns.map(|value| value / 10_000),
            working_set_mb: snapshot.working_set_bytes.and_then(bytes_to_mb),
            private_memory_mb: snapshot.private_memory_bytes.and_then(bytes_to_mb),
            thread_count: snapshot.thread_count,
            handle_count: snapshot.handle_count,
        }
    }
}

fn bytes_to_mb(value: u64) -> Option<f64> {
    sanitize_f64(value as f64 / 1_048_576.0)
}

fn sanitize_f64(value: f64) -> Option<f64> {
    if value.is_finite() && value >= 0.0 {
        Some(value)
    } else {
        None
    }
}

#[derive(Debug, Default)]
struct PlatformProcessMetrics {
    cpu_total_100ns: Option<u64>,
    working_set_bytes: Option<u64>,
    private_memory_bytes: Option<u64>,
    thread_count: Option<u32>,
    handle_count: Option<u32>,
}

#[cfg(windows)]
mod platform_process_metrics {
    use std::{ffi::c_void, mem};

    use super::PlatformProcessMetrics;

    const TH32CS_SNAPTHREAD: u32 = 0x0000_0004;
    const INVALID_HANDLE_VALUE: isize = -1isize;

    #[repr(C)]
    #[derive(Debug, Clone, Copy, Default)]
    struct FileTime {
        low_date_time: u32,
        high_date_time: u32,
    }

    #[repr(C)]
    #[derive(Debug, Clone, Copy)]
    struct ProcessMemoryCountersEx {
        cb: u32,
        page_fault_count: u32,
        peak_working_set_size: usize,
        working_set_size: usize,
        quota_peak_paged_pool_usage: usize,
        quota_paged_pool_usage: usize,
        quota_peak_non_paged_pool_usage: usize,
        quota_non_paged_pool_usage: usize,
        pagefile_usage: usize,
        peak_pagefile_usage: usize,
        private_usage: usize,
    }

    impl Default for ProcessMemoryCountersEx {
        fn default() -> Self {
            Self {
                cb: mem::size_of::<Self>() as u32,
                page_fault_count: 0,
                peak_working_set_size: 0,
                working_set_size: 0,
                quota_peak_paged_pool_usage: 0,
                quota_paged_pool_usage: 0,
                quota_peak_non_paged_pool_usage: 0,
                quota_non_paged_pool_usage: 0,
                pagefile_usage: 0,
                peak_pagefile_usage: 0,
                private_usage: 0,
            }
        }
    }

    #[repr(C)]
    #[derive(Debug, Clone, Copy)]
    struct ThreadEntry32 {
        dw_size: u32,
        cnt_usage: u32,
        th32_thread_id: u32,
        th32_owner_process_id: u32,
        tp_base_pri: i32,
        tp_delta_pri: i32,
        dw_flags: u32,
    }

    impl Default for ThreadEntry32 {
        fn default() -> Self {
            Self {
                dw_size: mem::size_of::<Self>() as u32,
                cnt_usage: 0,
                th32_thread_id: 0,
                th32_owner_process_id: 0,
                tp_base_pri: 0,
                tp_delta_pri: 0,
                dw_flags: 0,
            }
        }
    }

    pub fn cpu_total_100ns() -> Option<u64> {
        process_times()
            .map(|(kernel, user)| filetime_to_u64(kernel).saturating_add(filetime_to_u64(user)))
    }

    pub fn snapshot() -> PlatformProcessMetrics {
        let process = unsafe { GetCurrentProcess() };
        let cpu_total_100ns = cpu_total_100ns();
        let (working_set_bytes, private_memory_bytes) = memory_info(process);
        let handle_count = handle_count(process);
        let thread_count = thread_count(unsafe { GetCurrentProcessId() });

        PlatformProcessMetrics {
            cpu_total_100ns,
            working_set_bytes,
            private_memory_bytes,
            thread_count,
            handle_count,
        }
    }

    fn process_times() -> Option<(FileTime, FileTime)> {
        let mut creation_time = FileTime::default();
        let mut exit_time = FileTime::default();
        let mut kernel_time = FileTime::default();
        let mut user_time = FileTime::default();
        let result = unsafe {
            GetProcessTimes(
                GetCurrentProcess(),
                &mut creation_time,
                &mut exit_time,
                &mut kernel_time,
                &mut user_time,
            )
        };

        if result == 0 {
            None
        } else {
            Some((kernel_time, user_time))
        }
    }

    fn memory_info(process: *mut c_void) -> (Option<u64>, Option<u64>) {
        let mut counters = ProcessMemoryCountersEx::default();
        let result = unsafe {
            GetProcessMemoryInfo(
                process,
                &mut counters,
                mem::size_of::<ProcessMemoryCountersEx>() as u32,
            )
        };

        if result == 0 {
            (None, None)
        } else {
            (
                Some(counters.working_set_size as u64),
                Some(counters.private_usage as u64),
            )
        }
    }

    fn handle_count(process: *mut c_void) -> Option<u32> {
        let mut count = 0u32;
        let result = unsafe { GetProcessHandleCount(process, &mut count) };
        if result == 0 { None } else { Some(count) }
    }

    fn thread_count(process_id: u32) -> Option<u32> {
        let snapshot = unsafe { CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0) };
        if snapshot as isize == INVALID_HANDLE_VALUE {
            return None;
        }

        let mut entry = ThreadEntry32::default();
        let mut count = 0u32;
        let mut ok = unsafe { Thread32First(snapshot, &mut entry) } != 0;
        while ok {
            if entry.th32_owner_process_id == process_id {
                count = count.saturating_add(1);
            }
            entry.dw_size = mem::size_of::<ThreadEntry32>() as u32;
            ok = unsafe { Thread32Next(snapshot, &mut entry) } != 0;
        }

        unsafe {
            CloseHandle(snapshot);
        }
        Some(count)
    }

    fn filetime_to_u64(value: FileTime) -> u64 {
        (u64::from(value.high_date_time) << 32) | u64::from(value.low_date_time)
    }

    #[link(name = "kernel32")]
    unsafe extern "system" {
        fn GetCurrentProcess() -> *mut c_void;
        fn GetCurrentProcessId() -> u32;
        fn GetProcessTimes(
            process: *mut c_void,
            creation_time: *mut FileTime,
            exit_time: *mut FileTime,
            kernel_time: *mut FileTime,
            user_time: *mut FileTime,
        ) -> i32;
        fn GetProcessHandleCount(process: *mut c_void, handle_count: *mut u32) -> i32;
        fn CreateToolhelp32Snapshot(flags: u32, process_id: u32) -> *mut c_void;
        fn Thread32First(snapshot: *mut c_void, entry: *mut ThreadEntry32) -> i32;
        fn Thread32Next(snapshot: *mut c_void, entry: *mut ThreadEntry32) -> i32;
        fn CloseHandle(handle: *mut c_void) -> i32;
    }

    #[link(name = "psapi")]
    unsafe extern "system" {
        fn GetProcessMemoryInfo(
            process: *mut c_void,
            counters: *mut ProcessMemoryCountersEx,
            size: u32,
        ) -> i32;
    }
}

#[cfg(not(windows))]
mod platform_process_metrics {
    use super::PlatformProcessMetrics;

    pub fn cpu_total_100ns() -> Option<u64> {
        None
    }

    pub fn snapshot() -> PlatformProcessMetrics {
        PlatformProcessMetrics::default()
    }
}

#[derive(Debug)]
struct DiagnosticsConfig {
    enabled: bool,
    config_path: PathBuf,
    interval: Duration,
    log_path: PathBuf,
}

fn load_config(path: &Path) -> Option<DiagnosticsConfig> {
    let text = fs::read_to_string(path).ok()?;
    let text = strip_json_line_comments(text.trim_start_matches('\u{feff}'));
    let text = strip_json_trailing_commas(&text);
    let root = serde_json::from_str::<Value>(&text).ok()?;
    let master_enabled = root
        .get("DiagnosticsEnabled")
        .and_then(Value::as_bool)
        .unwrap_or(true);
    let enabled = master_enabled
        && root
            .get("DiagnosticsLogDesktopTui")
            .and_then(Value::as_bool)
            .unwrap_or(false);

    if !enabled {
        return Some(DiagnosticsConfig {
            enabled: false,
            config_path: path.to_path_buf(),
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
        .filter(|value| !value.contains('%'))
        .map(PathBuf::from)
        .unwrap_or_else(default_diagnostics_dir);

    Some(DiagnosticsConfig {
        enabled,
        config_path: path.to_path_buf(),
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

        if let Some(value) = arg
            .to_str()
            .and_then(|value| value.strip_prefix("--config="))
        {
            return Some(PathBuf::from(value));
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

fn resolve_writable_log_path(configured_path: &Path) -> Option<PathBuf> {
    if can_append_to_path(configured_path) {
        return Some(configured_path.to_path_buf());
    }

    let fallback_path = default_diagnostics_dir().join(LOG_FILE_NAME);
    if fallback_path != configured_path && can_append_to_path(&fallback_path) {
        Some(fallback_path)
    } else {
        None
    }
}

fn can_append_to_path(path: &Path) -> bool {
    append_line(path, "").is_ok()
}

fn append_line(path: &Path, line: &str) -> std::io::Result<()> {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)?;
    }

    let mut file = OpenOptions::new().create(true).append(true).open(path)?;
    if line.is_empty() {
        return Ok(());
    }

    writeln!(file, "{line}")
}

fn path_to_string(path: &PathBuf) -> String {
    path.display().to_string()
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

fn strip_json_line_comments(value: &str) -> String {
    let mut output = String::with_capacity(value.len());
    let mut chars = value.chars().peekable();
    let mut in_string = false;
    let mut escaped = false;

    while let Some(ch) = chars.next() {
        if in_string {
            output.push(ch);
            if escaped {
                escaped = false;
            } else if ch == '\\' {
                escaped = true;
            } else if ch == '"' {
                in_string = false;
            }
            continue;
        }

        if ch == '"' {
            in_string = true;
            output.push(ch);
            continue;
        }

        if ch == '/' && chars.peek().is_some_and(|next| *next == '/') {
            chars.next();
            for next in chars.by_ref() {
                if next == '\n' {
                    output.push('\n');
                    break;
                }
            }
            continue;
        }

        output.push(ch);
    }

    output
}

fn strip_json_trailing_commas(value: &str) -> String {
    let mut output = String::with_capacity(value.len());
    let chars: Vec<char> = value.chars().collect();
    let mut index = 0;
    let mut in_string = false;
    let mut escaped = false;

    while index < chars.len() {
        let ch = chars[index];

        if in_string {
            output.push(ch);
            if escaped {
                escaped = false;
            } else if ch == '\\' {
                escaped = true;
            } else if ch == '"' {
                in_string = false;
            }
            index += 1;
            continue;
        }

        if ch == '"' {
            in_string = true;
            output.push(ch);
            index += 1;
            continue;
        }

        if ch == ',' {
            let mut lookahead = index + 1;
            while lookahead < chars.len() && chars[lookahead].is_whitespace() {
                lookahead += 1;
            }

            if lookahead < chars.len() && matches!(chars[lookahead], '}' | ']') {
                index += 1;
                continue;
            }
        }

        output.push(ch);
        index += 1;
    }

    output
}
