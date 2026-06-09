use std::time::{Duration, Instant};

use color_eyre::eyre::Result;

use crate::{
    bridge::{SupervisorBridge, backend_endpoint},
    models::{
        CommandResult, CommandSummary, LogLine, StatusSummary, commands_from_response,
        logs_from_response, status_from_response,
    },
};

pub const REFRESH_INTERVAL: Duration = Duration::from_secs(3);
pub const HELP_TOGGLE_GUARD: Duration = Duration::from_millis(200);
pub const MAX_LOG_LINES: usize = 80;
pub const LOG_PAGE_SIZE: usize = 8;

#[derive(Debug, Clone, Copy, Eq, PartialEq)]
pub enum ConnectionState {
    Connected,
    Disconnected,
}

#[derive(Debug, Clone, Copy, Eq, PartialEq)]
pub enum ConfirmationModal {
    RestartOscRouter,
}

#[derive(Debug, Clone, Copy, Eq, PartialEq)]
pub enum ActionOutcome {
    Succeeded,
    Failed,
    Cancelled,
    Rejected,
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
    pub last_help_toggled_at: Option<Instant>,
    pub log_scroll: usize,
    pub confirmation: Option<ConfirmationModal>,
    pub action_in_progress: bool,
    pub last_action_started_at: Option<Instant>,
    pub last_action_completed_at: Option<Instant>,
    pub last_action_command: Option<String>,
    pub last_action_outcome: Option<ActionOutcome>,
    pub last_action_result: Option<String>,
    pub last_action_error: Option<String>,
}

impl App {
    pub fn new() -> Self {
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
            last_help_toggled_at: None,
            log_scroll: 0,
            confirmation: None,
            action_in_progress: false,
            last_action_started_at: None,
            last_action_completed_at: None,
            last_action_command: None,
            last_action_outcome: None,
            last_action_result: None,
            last_action_error: None,
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

    pub fn toggle_help_guarded(&mut self, now: Instant) {
        if self
            .last_help_toggled_at
            .map(|last| now.duration_since(last) < HELP_TOGGLE_GUARD)
            .unwrap_or(false)
        {
            return;
        }

        self.toggle_help();
        self.last_help_toggled_at = Some(now);
    }

    pub fn close_help(&mut self) {
        self.help_visible = false;
    }

    pub fn restart_osc_router_executable(&self) -> bool {
        self.last_success.is_some()
            && self.commands.iter().any(|command| {
                command.name.eq_ignore_ascii_case("restart-osc-router")
                    && command.action_supported
                    && command.tui_executable
                    && command.requires_confirmation
                    && command
                        .action_safety_category
                        .eq_ignore_ascii_case("LowRisk")
            })
    }

    pub fn request_restart_osc_router_confirmation(&mut self, now: Instant) {
        if self.action_in_progress {
            self.record_action_error(
                "restart-osc-router",
                ActionOutcome::Rejected,
                "Action already in progress.".to_string(),
                now,
            );
            return;
        }

        if self.restart_osc_router_executable() {
            self.help_visible = false;
            self.confirmation = Some(ConfirmationModal::RestartOscRouter);
            return;
        }

        self.record_action_error(
            "restart-osc-router",
            ActionOutcome::Rejected,
            "restart-osc-router is not executable from this TUI yet.".to_string(),
            now,
        );
    }

    pub fn cancel_confirmation(&mut self, now: Instant) {
        self.confirmation = None;
        self.record_action_result(
            "restart-osc-router",
            ActionOutcome::Cancelled,
            "Action cancelled.".to_string(),
            now,
        );
    }

    pub fn confirm_restart_osc_router(&mut self, now: Instant) {
        if self.confirmation != Some(ConfirmationModal::RestartOscRouter) {
            return;
        }

        if self.action_in_progress {
            self.record_action_error(
                "restart-osc-router",
                ActionOutcome::Rejected,
                "Action already in progress.".to_string(),
                now,
            );
            return;
        }

        self.confirmation = None;
        self.action_in_progress = true;
        self.last_action_started_at = Some(now);
        self.last_action_command = Some("restart-osc-router".to_string());
        self.last_action_outcome = None;
        self.last_action_result = None;
        self.last_action_error = None;

        let bridge = SupervisorBridge::default();

        match bridge.execute_restart_osc_router() {
            Ok(result) => {
                let completed = Instant::now();
                self.action_in_progress = false;
                self.record_action_result(
                    "restart-osc-router",
                    ActionOutcome::Succeeded,
                    format_action_result(&result),
                    completed,
                );
                self.refresh(completed);
            }
            Err(error) => {
                let completed = Instant::now();
                self.action_in_progress = false;
                self.record_action_error(
                    "restart-osc-router",
                    ActionOutcome::Failed,
                    error.to_string(),
                    completed,
                );
            }
        }
    }

    pub fn scroll_logs_up(&mut self, amount: usize) {
        self.log_scroll = self.log_scroll.saturating_sub(amount);
    }

    pub fn scroll_logs_down(&mut self, amount: usize) {
        self.log_scroll = self.log_scroll.saturating_add(amount);
        self.clamp_log_scroll();
    }

    pub fn scroll_logs_home(&mut self) {
        self.log_scroll = 0;
    }

    pub fn scroll_logs_end(&mut self) {
        self.log_scroll = self.logs.len().saturating_sub(1);
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

    pub fn last_action_started_label(&self, now: Instant) -> Option<String> {
        self.last_action_started_at
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
        self.log_scroll = self.log_scroll.min(self.logs.len().saturating_sub(1));
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

fn format_action_result(result: &CommandResult) -> String {
    let command = result
        .command
        .as_deref()
        .filter(|value| !value.is_empty())
        .unwrap_or("restart-osc-router");
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
