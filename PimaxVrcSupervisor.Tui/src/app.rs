use std::{
    panic::{AssertUnwindSafe, catch_unwind},
    sync::mpsc::{Receiver, Sender, channel},
    thread,
    time::{Duration, Instant},
};

use color_eyre::eyre::Result;
use ratatui::layout::Rect;

use crate::{
    bridge::{SupervisorBridge, backend_endpoint},
    console_close,
    models::{
        CommandResult, CommandSummary, LogLine, StatusSummary, TuiAction, commands_from_response,
        logs_from_response, status_from_response,
    },
};

pub const REFRESH_INTERVAL: Duration = Duration::from_secs(3);
pub const MAX_LOG_LINES: usize = 80;
pub const LOG_PAGE_SIZE: usize = 8;
pub const SHUTDOWN_WAIT_TIMEOUT: Duration = Duration::from_secs(60);
pub const SHUTDOWN_TIMEOUT_NOTICE_DELAY: Duration = Duration::from_secs(2);

#[derive(Debug, Clone, Copy, Eq, PartialEq)]
pub enum ConnectionState {
    Connected,
    Disconnected,
}

#[derive(Debug, Clone, Copy, Eq, PartialEq)]
pub enum ActionOutcome {
    Succeeded,
    Failed,
    Cancelled,
    Rejected,
    BackendOff,
}

#[derive(Debug, Clone)]
pub struct RunningAction {
    pub action: TuiAction,
    pub command: String,
    pub started_at: Instant,
}

#[derive(Debug, Clone)]
pub struct CompletedActionResult {
    pub command: String,
    pub completed_at: Instant,
    pub outcome: ActionOutcome,
    pub message: String,
}

#[derive(Debug, Clone)]
pub struct ShutdownRequestResult {
    pub completed_at: Instant,
    pub accepted: bool,
    pub message: String,
}

#[derive(Debug, Clone, Copy, Eq, PartialEq)]
pub enum ClickAction {
    OpenHelp,
    Refresh,
    QuitTui,
    SelectAction(TuiAction),
    ConfirmModal,
    CancelModal,
}

#[derive(Debug, Clone, Copy, Eq, PartialEq)]
pub struct ClickRegion {
    pub area: Rect,
    pub action: ClickAction,
}

pub struct App {
    pub connection: ConnectionState,
    pub status: StatusSummary,
    pub commands: Vec<CommandSummary>,
    pub logs: Vec<LogLine>,
    pub backend_endpoint: String,
    pub last_success: Option<Instant>,
    pub last_error_at: Option<Instant>,
    pub last_error: Option<String>,
    pub last_attempt: Option<Instant>,
    pub refresh_in_progress: bool,
    pub help_visible: bool,
    pub log_scroll: usize,
    pub log_follow: bool,
    pub confirmation: Option<TuiAction>,
    pub shutdown_confirmation: bool,
    pub shutdown_in_progress: bool,
    pub shutdown_accepted: bool,
    pub shutdown_started_at: Option<Instant>,
    pub shutdown_exit_after: Option<Instant>,
    pub shutdown_message: Option<String>,
    pub shutdown_error: Option<String>,
    pub running_actions: Vec<RunningAction>,
    pub last_action_completed_at: Option<Instant>,
    pub last_action_command: Option<String>,
    pub last_action_outcome: Option<ActionOutcome>,
    pub last_action_result: Option<String>,
    pub last_action_error: Option<String>,
    pub click_regions: Vec<ClickRegion>,
    pub mouse_enabled: bool,
    pub mouse_notice: Option<String>,
    pub console_close_enabled: bool,
    pub console_close_notice: Option<String>,
    action_result_tx: Sender<CompletedActionResult>,
    action_result_rx: Receiver<CompletedActionResult>,
    shutdown_result_tx: Sender<ShutdownRequestResult>,
    shutdown_result_rx: Receiver<ShutdownRequestResult>,
}

impl App {
    pub fn new() -> Self {
        let (action_result_tx, action_result_rx) = channel();
        let (shutdown_result_tx, shutdown_result_rx) = channel();
        Self::with_channels(
            action_result_tx,
            action_result_rx,
            shutdown_result_tx,
            shutdown_result_rx,
        )
    }

    fn with_channels(
        action_result_tx: Sender<CompletedActionResult>,
        action_result_rx: Receiver<CompletedActionResult>,
        shutdown_result_tx: Sender<ShutdownRequestResult>,
        shutdown_result_rx: Receiver<ShutdownRequestResult>,
    ) -> Self {
        Self {
            connection: ConnectionState::Disconnected,
            status: StatusSummary::default(),
            commands: Vec::new(),
            logs: Vec::new(),
            backend_endpoint: backend_endpoint(),
            last_success: None,
            last_error_at: None,
            last_error: None,
            last_attempt: None,
            refresh_in_progress: false,
            help_visible: false,
            log_scroll: 0,
            log_follow: true,
            confirmation: None,
            shutdown_confirmation: false,
            shutdown_in_progress: false,
            shutdown_accepted: false,
            shutdown_started_at: None,
            shutdown_exit_after: None,
            shutdown_message: None,
            shutdown_error: None,
            running_actions: Vec::new(),
            last_action_completed_at: None,
            last_action_command: None,
            last_action_outcome: None,
            last_action_result: None,
            last_action_error: None,
            click_regions: Vec::new(),
            mouse_enabled: false,
            mouse_notice: None,
            console_close_enabled: false,
            console_close_notice: None,
            action_result_tx,
            action_result_rx,
            shutdown_result_tx,
            shutdown_result_rx,
        }
    }

    pub fn refresh(&mut self, now: Instant) {
        if self.refresh_in_progress {
            return;
        }

        self.refresh_in_progress = true;
        self.last_attempt = Some(now);

        let bridge = SupervisorBridge::default();

        match Self::load(&bridge) {
            Ok((status, commands, logs)) => {
                self.connection = ConnectionState::Connected;
                self.status = status;
                self.commands = commands;
                self.logs = logs;
                self.last_success = Some(now);
                self.last_error = None;
                self.last_error_at = None;
            }
            Err(error) => {
                self.connection = ConnectionState::Disconnected;
                self.last_error = Some(error.to_string());
                self.last_error_at = Some(now);
            }
        }

        self.refresh_in_progress = false;
        self.clamp_log_scroll();
    }

    pub fn drain_action_results(&mut self) {
        let mut completed_results = Vec::new();
        while let Ok(result) = self.action_result_rx.try_recv() {
            completed_results.push(result);
        }

        if completed_results.is_empty() {
            return;
        }

        let mut should_refresh = false;
        for result in completed_results {
            self.running_actions
                .retain(|running| !running.command.eq_ignore_ascii_case(&result.command));
            self.record_action_result(
                result.command.as_str(),
                result.outcome,
                result.message,
                result.completed_at,
            );
            should_refresh = true;
        }

        if should_refresh {
            self.refresh(Instant::now());
        }
    }

    pub fn drain_shutdown_result(&mut self) {
        let mut latest = None;
        while let Ok(result) = self.shutdown_result_rx.try_recv() {
            latest = Some(result);
        }

        let Some(result) = latest else {
            return;
        };

        if result.accepted {
            self.shutdown_accepted = true;
            self.shutdown_in_progress = true;
            self.shutdown_exit_after = None;
            self.shutdown_message = Some(result.message);
            self.shutdown_error = None;
        } else {
            self.shutdown_in_progress = false;
            self.shutdown_accepted = false;
            self.shutdown_exit_after = None;
            self.shutdown_message = None;
            self.shutdown_error = Some(result.message.clone());
            self.record_action_error(
                "request-graceful-shutdown",
                ActionOutcome::Failed,
                result.message,
                result.completed_at,
            );
        }
    }

    pub fn should_auto_refresh(&self, now: Instant) -> bool {
        if self.refresh_in_progress {
            return false;
        }

        self.last_attempt
            .map(|attempt| now.duration_since(attempt) >= REFRESH_INTERVAL)
            .unwrap_or(true)
    }

    pub fn poll_timeout(&self, now: Instant) -> Duration {
        const MAX_POLL: Duration = Duration::from_millis(200);

        if self.refresh_in_progress {
            return MAX_POLL;
        }

        let Some(last_attempt) = self.last_attempt else {
            return Duration::ZERO;
        };

        let elapsed = now.duration_since(last_attempt);
        if elapsed >= REFRESH_INTERVAL {
            Duration::ZERO
        } else {
            (REFRESH_INTERVAL - elapsed).min(MAX_POLL)
        }
    }

    pub fn toggle_help(&mut self) {
        self.help_visible = !self.help_visible;
    }

    pub fn close_help(&mut self) {
        self.help_visible = false;
    }

    pub fn set_mouse_status(&mut self, enabled: bool, notice: Option<String>) {
        self.mouse_enabled = enabled;
        self.mouse_notice = notice;
    }

    pub fn set_console_close_status(&mut self, enabled: bool, notice: Option<String>) {
        self.console_close_enabled = enabled;
        self.console_close_notice = notice;
    }

    pub fn clear_click_regions(&mut self) {
        self.click_regions.clear();
    }

    pub fn add_click_region(&mut self, area: Rect, action: ClickAction) {
        if area.width == 0 || area.height == 0 {
            return;
        }

        self.click_regions.push(ClickRegion { area, action });
    }

    pub fn click_action_at(&self, column: u16, row: u16) -> Option<ClickAction> {
        self.click_regions
            .iter()
            .rev()
            .find(|region| rect_contains(region.area, column, row))
            .map(|region| region.action)
    }

    pub fn action_metadata(&self, action: TuiAction) -> Option<&CommandSummary> {
        self.commands
            .iter()
            .find(|command| command.name.eq_ignore_ascii_case(action.command_name()))
    }

    pub fn action_executable(&self, action: TuiAction) -> bool {
        self.connection == ConnectionState::Connected
            && self.action_metadata(action).is_some_and(|command| {
                command.action_supported
                    && command.tui_executable
                    && command.requires_confirmation
                    && !command
                        .action_safety_category
                        .eq_ignore_ascii_case("Blocked")
                    && !command
                        .action_safety_category
                        .eq_ignore_ascii_case("Dangerous")
            })
    }

    pub fn request_action_confirmation(&mut self, action: TuiAction, now: Instant) {
        if self.shutdown_in_progress {
            self.record_action_error(
                action.command_name(),
                ActionOutcome::Rejected,
                "Supervisor shutdown is in progress; actions are disabled.".to_string(),
                now,
            );
            return;
        }

        if self.connection != ConnectionState::Connected {
            self.record_backend_off(action, now);
            return;
        }

        if self.validate_action_start(action).is_ok() {
            self.help_visible = false;
            self.confirmation = Some(action);
            return;
        }

        self.record_action_rejection(action, now);
    }

    pub fn cancel_confirmation(&mut self, now: Instant) {
        let command = self
            .confirmation
            .map(TuiAction::command_name)
            .unwrap_or("action");
        self.confirmation = None;
        self.record_action_result(
            command,
            ActionOutcome::Cancelled,
            "Action cancelled.".to_string(),
            now,
        );
    }

    pub fn confirm_action(&mut self, now: Instant) {
        let Some(action) = self.confirmation else {
            return;
        };

        self.confirmation = None;
        self.request_action_start(action, now);
    }

    pub fn request_action_start(&mut self, action: TuiAction, now: Instant) {
        if self.shutdown_in_progress {
            self.record_action_error(
                action.command_name(),
                ActionOutcome::Rejected,
                "Supervisor shutdown is in progress; actions are disabled.".to_string(),
                now,
            );
            return;
        }

        if self.connection != ConnectionState::Connected {
            self.record_backend_off(action, now);
            return;
        }

        if let Err(message) = self.validate_action_start(action) {
            self.record_action_error(action.command_name(), ActionOutcome::Rejected, message, now);
            return;
        }

        self.last_action_command = Some(action.command_name().to_string());
        self.last_action_outcome = None;
        self.last_action_result = Some(format!("{} started.", action.command_name()));
        self.last_action_error = None;
        self.running_actions.push(RunningAction {
            action,
            command: action.command_name().to_string(),
            started_at: now,
        });
        self.spawn_action_worker(action);
    }

    pub fn request_shutdown_confirmation(&mut self, now: Instant) -> bool {
        if self.connection != ConnectionState::Connected {
            self.shutdown_message = Some("Backend is not running. Exiting TUI.".to_string());
            self.last_action_completed_at = Some(now);
            return true;
        }

        if self.shutdown_in_progress {
            return false;
        }

        self.help_visible = false;
        self.confirmation = None;
        self.shutdown_confirmation = true;
        false
    }

    pub fn cancel_shutdown_confirmation(&mut self) {
        self.shutdown_confirmation = false;
    }

    pub fn confirm_shutdown(&mut self, now: Instant) {
        if self.shutdown_in_progress {
            return;
        }

        self.shutdown_confirmation = false;
        self.confirmation = None;
        self.shutdown_in_progress = true;
        self.shutdown_accepted = false;
        self.shutdown_started_at = Some(now);
        self.shutdown_exit_after = None;
        self.shutdown_message =
            Some("Supervisor shutdown requested. Waiting for cleanup...".to_string());
        self.shutdown_error = None;
        console_close::mark_shutdown_requested();
        self.spawn_shutdown_worker();
    }

    pub fn should_exit_after_shutdown(&mut self, now: Instant) -> bool {
        if !self.shutdown_in_progress || !self.shutdown_accepted {
            return false;
        }

        if self.connection == ConnectionState::Disconnected {
            return true;
        }

        if self
            .shutdown_started_at
            .is_some_and(|started| now.duration_since(started) >= SHUTDOWN_WAIT_TIMEOUT)
        {
            if let Some(exit_after) = self.shutdown_exit_after {
                return now >= exit_after;
            }

            self.shutdown_message = Some(
                "Shutdown was requested, but the supervisor is still reachable. Check the supervisor logs."
                    .to_string(),
            );
            self.shutdown_exit_after = Some(now + SHUTDOWN_TIMEOUT_NOTICE_DELAY);
        }

        false
    }

    pub fn scroll_logs_up(&mut self, amount: usize) {
        self.log_follow = false;
        self.log_scroll = self.log_scroll.saturating_add(amount);
        self.clamp_log_scroll();
    }

    pub fn scroll_logs_down(&mut self, amount: usize) {
        self.log_scroll = self.log_scroll.saturating_sub(amount);
        if self.log_scroll == 0 {
            self.log_follow = true;
        }
        self.clamp_log_scroll();
    }

    pub fn scroll_logs_home(&mut self) {
        self.log_follow = false;
        self.log_scroll = self.logs.len().saturating_sub(1);
        self.clamp_log_scroll();
    }

    pub fn scroll_logs_end(&mut self) {
        self.follow_latest_logs();
    }

    pub fn follow_latest_logs(&mut self) {
        self.log_follow = true;
        self.log_scroll = 0;
        self.clamp_log_scroll();
    }

    pub fn last_success_label(&self, now: Instant) -> String {
        elapsed_label(self.last_success, now, "never")
    }

    pub fn last_error_label(&self, now: Instant) -> Option<String> {
        self.last_error_at
            .map(|at| format!("{} ago", format_duration(now.duration_since(at))))
    }

    pub fn last_action_completed_label(&self, now: Instant) -> Option<String> {
        self.last_action_completed_at
            .map(|at| format!("{} ago", format_duration(now.duration_since(at))))
    }

    fn load(
        bridge: &SupervisorBridge,
    ) -> Result<(StatusSummary, Vec<CommandSummary>, Vec<LogLine>)> {
        let status_response = bridge.query_status()?;
        let commands_response = bridge.query_commands()?;
        let log_response = bridge.query_log(MAX_LOG_LINES)?;

        Ok((
            status_from_response(&status_response),
            commands_from_response(&commands_response),
            logs_from_response(&log_response),
        ))
    }

    fn clamp_log_scroll(&mut self) {
        if self.log_follow {
            self.log_scroll = 0;
        } else {
            self.log_scroll = self.log_scroll.min(self.logs.len().saturating_sub(1));
            if self.log_scroll == 0 {
                self.log_follow = true;
            }
        }
    }

    fn action_conflict_message(&self, candidate: TuiAction) -> Option<String> {
        let candidate_command = candidate.command_name();
        if self
            .running_actions
            .iter()
            .any(|running| running.command.eq_ignore_ascii_case(candidate_command))
        {
            return Some(format!("Action already running: {candidate_command}"));
        }

        let base_station_power_conflict = matches!(
            candidate,
            TuiAction::BaseStationsOn | TuiAction::BaseStationsOff
        ) && self.running_actions.iter().any(|running| {
            matches!(
                running.action,
                TuiAction::BaseStationsOn | TuiAction::BaseStationsOff
            )
        });

        if base_station_power_conflict {
            return Some(
                "Base station power action already running: base-stations-on/base-stations-off"
                    .to_string(),
            );
        }

        None
    }

    fn validate_action_start(&self, action: TuiAction) -> std::result::Result<(), String> {
        if self.shutdown_in_progress {
            return Err("Supervisor shutdown is in progress; actions are disabled.".to_string());
        }

        if self.connection != ConnectionState::Connected {
            return Err(format!(
                "Backend unavailable; cannot start {}.",
                action.command_name()
            ));
        }

        if let Some(message) = self.action_conflict_message(action) {
            return Err(message);
        }

        if self.action_executable(action) {
            Ok(())
        } else {
            Err(format!(
                "{} is not executable from this TUI yet.",
                action.command_name()
            ))
        }
    }

    fn record_action_rejection(&mut self, action: TuiAction, now: Instant) {
        let message = self.validate_action_start(action).err().unwrap_or_else(|| {
            format!(
                "{} is not executable from this TUI yet.",
                action.command_name()
            )
        });
        self.record_action_error(action.command_name(), ActionOutcome::Rejected, message, now);
    }

    fn record_backend_off(&mut self, action: TuiAction, now: Instant) {
        self.record_action_error(
            action.command_name(),
            ActionOutcome::BackendOff,
            "backend unavailable".to_string(),
            now,
        );
    }

    fn spawn_action_worker(&self, action: TuiAction) {
        let sender = self.action_result_tx.clone();
        thread::spawn(move || {
            let command = action.command_name().to_string();
            let result = catch_unwind(AssertUnwindSafe(|| {
                let bridge = SupervisorBridge::default();
                bridge.execute_tui_action(action)
            }));

            let completed = match result {
                Ok(Ok(command_result)) => CompletedActionResult {
                    command,
                    completed_at: Instant::now(),
                    outcome: ActionOutcome::Succeeded,
                    message: format_action_result(&command_result),
                },
                Ok(Err(error)) => CompletedActionResult {
                    command,
                    completed_at: Instant::now(),
                    outcome: ActionOutcome::Failed,
                    message: error.to_string(),
                },
                Err(_) => CompletedActionResult {
                    command,
                    completed_at: Instant::now(),
                    outcome: ActionOutcome::Failed,
                    message: "Action worker panicked.".to_string(),
                },
            };

            let _ = sender.send(completed);
        });
    }

    fn spawn_shutdown_worker(&self) {
        let sender = self.shutdown_result_tx.clone();
        thread::spawn(move || {
            let result = catch_unwind(AssertUnwindSafe(|| {
                let bridge = SupervisorBridge::default();
                bridge.request_graceful_shutdown()
            }));

            let completed = match result {
                Ok(Ok(command_result)) => ShutdownRequestResult {
                    completed_at: Instant::now(),
                    accepted: true,
                    message: format_action_result(&command_result),
                },
                Ok(Err(error)) => ShutdownRequestResult {
                    completed_at: Instant::now(),
                    accepted: false,
                    message: error.to_string(),
                },
                Err(_) => ShutdownRequestResult {
                    completed_at: Instant::now(),
                    accepted: false,
                    message: "Shutdown request worker panicked.".to_string(),
                },
            };

            let _ = sender.send(completed);
        });
    }

    fn record_action_result(
        &mut self,
        command: &str,
        outcome: ActionOutcome,
        message: String,
        now: Instant,
    ) {
        self.last_action_command = Some(command.to_string());
        self.last_action_outcome = Some(outcome);
        self.last_action_result = Some(message);
        self.last_action_error = None;
        self.last_action_completed_at = Some(now);
    }

    fn record_action_error(
        &mut self,
        command: &str,
        outcome: ActionOutcome,
        message: String,
        now: Instant,
    ) {
        self.last_action_command = Some(command.to_string());
        self.last_action_outcome = Some(outcome);
        self.last_action_error = Some(message);
        self.last_action_result = None;
        self.last_action_completed_at = Some(now);
    }
}

fn rect_contains(area: Rect, column: u16, row: u16) -> bool {
    column >= area.x
        && column < area.x.saturating_add(area.width)
        && row >= area.y
        && row < area.y.saturating_add(area.height)
}

fn format_action_result(result: &CommandResult) -> String {
    let command = result
        .command
        .as_deref()
        .filter(|value| !value.is_empty())
        .unwrap_or("action");
    let message = result
        .message
        .as_deref()
        .or(result.error.as_deref())
        .unwrap_or("Action completed.");

    format!("{command}: {message}")
}

fn elapsed_label(instant: Option<Instant>, now: Instant, empty: &str) -> String {
    instant
        .map(|at| format!("{} ago", format_duration(now.duration_since(at))))
        .unwrap_or_else(|| empty.to_string())
}

fn format_duration(duration: Duration) -> String {
    let seconds = duration.as_secs();

    if seconds < 60 {
        format!("{seconds}s")
    } else {
        let minutes = seconds / 60;
        let remainder = seconds % 60;
        format!("{minutes}m {remainder}s")
    }
}
