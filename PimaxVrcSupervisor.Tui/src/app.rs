use std::{
    panic::{AssertUnwindSafe, catch_unwind},
    sync::mpsc::{Receiver, Sender, channel},
    thread,
    time::{Duration, Instant},
};

use color_eyre::eyre::Result;
use ratatui::layout::Rect;

use crate::{
    bridge::SupervisorBridge,
    console_close,
    diagnostics::{DiagnosticsHandle, TuiDiagnostics},
    models::{
        CommandResult, CommandSummary, LogLine, StatusSummary, TuiAction, commands_from_response,
        logs_from_response, status_from_response,
    },
};

pub const REFRESH_INTERVAL: Duration = Duration::from_secs(3);
pub const DISCONNECTED_REFRESH_INTERVAL: Duration = Duration::from_secs(7);
pub const CONNECTED_HEARTBEAT_INTERVAL: Duration = Duration::from_secs(3);
pub const DISCONNECTED_HEARTBEAT_INTERVAL: Duration = Duration::from_secs(5);
pub const ACTIVE_HEARTBEAT_INTERVAL: Duration = Duration::from_secs(1);
pub const MAX_LOG_LINES: usize = 80;
pub const LOG_PAGE_SIZE: usize = 8;
pub const SHUTDOWN_WAIT_TIMEOUT: Duration = Duration::from_secs(60);
pub const SHUTDOWN_TIMEOUT_NOTICE_DELAY: Duration = Duration::from_secs(2);
pub const SUPERVISOR_DISCONNECT_AUTO_EXIT_DELAY: Duration = Duration::from_secs(7);

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
    pub supervisor_process_notice: Option<String>,
    exit_when_supervisor_exits: bool,
    was_connected_once: bool,
    supervisor_disconnect_seen_at: Option<Instant>,
    auto_exit_after_supervisor_disconnect: Option<Instant>,
    render_needed: bool,
    last_render_at: Option<Instant>,
    diagnostics: TuiDiagnostics,
    action_result_tx: Sender<CompletedActionResult>,
    action_result_rx: Receiver<CompletedActionResult>,
    shutdown_result_tx: Sender<ShutdownRequestResult>,
    shutdown_result_rx: Receiver<ShutdownRequestResult>,
}

impl App {
    pub fn new(diagnostics: TuiDiagnostics, exit_when_supervisor_exits: bool) -> Self {
        let (action_result_tx, action_result_rx) = channel();
        let (shutdown_result_tx, shutdown_result_rx) = channel();
        Self::with_channels(
            diagnostics,
            exit_when_supervisor_exits,
            action_result_tx,
            action_result_rx,
            shutdown_result_tx,
            shutdown_result_rx,
        )
    }

    fn with_channels(
        diagnostics: TuiDiagnostics,
        exit_when_supervisor_exits: bool,
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
            supervisor_process_notice: None,
            exit_when_supervisor_exits,
            was_connected_once: false,
            supervisor_disconnect_seen_at: None,
            auto_exit_after_supervisor_disconnect: None,
            render_needed: true,
            last_render_at: None,
            diagnostics,
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
        self.diagnostics.record_refresh();

        let bridge = SupervisorBridge::with_diagnostics(self.diagnostics_handle());
        let previous_connection = self.connection;

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

        if previous_connection != self.connection {
            self.diagnostics
                .record_connection(self.connection == ConnectionState::Connected);
        }
        self.update_supervisor_disconnect_auto_exit(now);

        self.refresh_in_progress = false;
        self.clamp_log_scroll();
        self.mark_render_needed();
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
        } else {
            self.mark_render_needed();
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

        self.mark_render_needed();
    }

    pub fn should_auto_refresh(&self, now: Instant) -> bool {
        if self.refresh_in_progress {
            return false;
        }

        self.last_attempt
            .map(|attempt| now.duration_since(attempt) >= self.refresh_interval())
            .unwrap_or(true)
    }

    pub fn poll_timeout(&self, now: Instant) -> Duration {
        const MAX_POLL: Duration = Duration::from_millis(200);

        if self.refresh_in_progress {
            return MAX_POLL;
        }

        let refresh_remaining = self
            .last_attempt
            .map(|last_attempt| remaining_until(now, last_attempt, self.refresh_interval()))
            .unwrap_or(Duration::ZERO);

        let heartbeat_remaining = self
            .last_render_at
            .map(|last_render_at| remaining_until(now, last_render_at, self.heartbeat_interval()))
            .unwrap_or(Duration::ZERO);

        let next_due = refresh_remaining.min(heartbeat_remaining);
        if next_due.is_zero() {
            Duration::ZERO
        } else {
            next_due.min(MAX_POLL)
        }
    }

    pub fn should_render(&self, now: Instant) -> bool {
        if self.render_needed {
            return true;
        }

        self.last_render_at
            .map(|last_render_at| now.duration_since(last_render_at) >= self.heartbeat_interval())
            .unwrap_or(true)
    }

    pub fn mark_render_needed(&mut self) {
        self.render_needed = true;
    }

    pub fn toggle_help(&mut self) {
        self.help_visible = !self.help_visible;
        self.mark_render_needed();
    }

    pub fn close_help(&mut self) {
        self.help_visible = false;
        self.mark_render_needed();
    }

    pub fn set_mouse_status(&mut self, enabled: bool, notice: Option<String>) {
        self.mouse_enabled = enabled;
        self.mouse_notice = notice;
        self.mark_render_needed();
    }

    pub fn set_console_close_status(&mut self, enabled: bool, notice: Option<String>) {
        self.console_close_enabled = enabled;
        self.console_close_notice = notice;
        self.mark_render_needed();
    }

    pub fn set_supervisor_process_notice(&mut self, notice: Option<String>) {
        self.supervisor_process_notice = notice;
        self.mark_render_needed();
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
            self.mark_render_needed();
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
        self.last_action_result = Some(format!("{} started.", action.display_name()));
        self.last_action_error = None;
        self.running_actions.push(RunningAction {
            action,
            command: action.command_name().to_string(),
            started_at: now,
        });
        self.diagnostics.record_action_started();
        self.spawn_action_worker(action);
        self.mark_render_needed();
    }

    pub fn request_shutdown_confirmation(&mut self, now: Instant) -> bool {
        if self.connection != ConnectionState::Connected {
            self.shutdown_message = Some("Supervisor is not running. Exiting TUI.".to_string());
            self.last_action_completed_at = Some(now);
            self.mark_render_needed();
            return true;
        }

        if self.shutdown_in_progress {
            return false;
        }

        self.help_visible = false;
        self.confirmation = None;
        self.shutdown_confirmation = true;
        self.mark_render_needed();
        false
    }

    pub fn cancel_shutdown_confirmation(&mut self) {
        self.shutdown_confirmation = false;
        self.mark_render_needed();
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
        self.shutdown_message = Some("Shutdown requested. Closing managed apps...".to_string());
        self.shutdown_error = None;
        console_close::mark_shutdown_requested();
        self.diagnostics.record_lifecycle_request();
        self.spawn_shutdown_worker();
        self.mark_render_needed();
    }

    pub fn record_render(&mut self, now: Instant) {
        self.last_render_at = Some(now);
        self.render_needed = false;
        self.diagnostics.record_render();
    }

    pub fn record_input_wakeup(&self) {
        self.diagnostics.record_input_wakeup();
    }

    pub fn maybe_write_diagnostics(&self, now: Instant) {
        self.diagnostics.maybe_write(now);
    }

    fn diagnostics_handle(&self) -> DiagnosticsHandle {
        self.diagnostics.handle()
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

            self.shutdown_message =
                Some("The Supervisor did not exit in time. Check the Supervisor logs.".to_string());
            self.shutdown_exit_after = Some(now + SHUTDOWN_TIMEOUT_NOTICE_DELAY);
            self.mark_render_needed();
        }

        false
    }

    pub fn should_exit_after_supervisor_disconnect(&self, now: Instant) -> bool {
        if self.shutdown_in_progress {
            return false;
        }

        self.auto_exit_after_supervisor_disconnect
            .is_some_and(|exit_after| now >= exit_after)
    }

    pub fn scroll_logs_up(&mut self, amount: usize) {
        self.log_follow = false;
        self.log_scroll = self.log_scroll.saturating_add(amount);
        self.clamp_log_scroll();
        self.mark_render_needed();
    }

    pub fn scroll_logs_down(&mut self, amount: usize) {
        self.log_scroll = self.log_scroll.saturating_sub(amount);
        if self.log_scroll == 0 {
            self.log_follow = true;
        }
        self.clamp_log_scroll();
        self.mark_render_needed();
    }

    pub fn scroll_logs_home(&mut self) {
        self.log_follow = false;
        self.log_scroll = self.logs.len().saturating_sub(1);
        self.clamp_log_scroll();
        self.mark_render_needed();
    }

    pub fn scroll_logs_end(&mut self) {
        self.follow_latest_logs();
    }

    pub fn follow_latest_logs(&mut self) {
        self.log_follow = true;
        self.log_scroll = 0;
        self.clamp_log_scroll();
        self.mark_render_needed();
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

    fn update_supervisor_disconnect_auto_exit(&mut self, now: Instant) {
        if !self.exit_when_supervisor_exits {
            return;
        }

        match self.connection {
            ConnectionState::Connected => {
                self.was_connected_once = true;
                self.supervisor_disconnect_seen_at = None;
                self.auto_exit_after_supervisor_disconnect = None;
            }
            ConnectionState::Disconnected if self.was_connected_once => {
                if self.supervisor_disconnect_seen_at.is_none() {
                    self.supervisor_disconnect_seen_at = Some(now);
                    self.auto_exit_after_supervisor_disconnect =
                        Some(now + SUPERVISOR_DISCONNECT_AUTO_EXIT_DELAY);
                    self.mark_render_needed();
                }
            }
            ConnectionState::Disconnected => {}
        }
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
            return Some(format!("{} is already running.", candidate.display_name()));
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
            return Some("A base-station power action is already running.".to_string());
        }

        None
    }

    fn validate_action_start(&self, action: TuiAction) -> std::result::Result<(), String> {
        if self.shutdown_in_progress {
            return Err("Supervisor shutdown is in progress; actions are disabled.".to_string());
        }

        if self.connection != ConnectionState::Connected {
            return Err(format!(
                "Supervisor disconnected; cannot start {}.",
                action.display_name()
            ));
        }

        if let Some(message) = self.action_conflict_message(action) {
            return Err(message);
        }

        if self.action_executable(action) {
            Ok(())
        } else {
            Err(format!(
                "{} is not available from this TUI yet.",
                action.display_name()
            ))
        }
    }

    fn record_action_rejection(&mut self, action: TuiAction, now: Instant) {
        let message = self.validate_action_start(action).err().unwrap_or_else(|| {
            format!(
                "{} is not available from this TUI yet.",
                action.display_name()
            )
        });
        self.record_action_error(action.command_name(), ActionOutcome::Rejected, message, now);
    }

    fn record_backend_off(&mut self, action: TuiAction, now: Instant) {
        self.record_action_error(
            action.command_name(),
            ActionOutcome::BackendOff,
            "Supervisor disconnected.".to_string(),
            now,
        );
    }

    fn spawn_action_worker(&self, action: TuiAction) {
        let sender = self.action_result_tx.clone();
        let diagnostics = self.diagnostics_handle();
        thread::spawn(move || {
            let command = action.command_name().to_string();
            let result = catch_unwind(AssertUnwindSafe(|| {
                let bridge = SupervisorBridge::with_diagnostics(diagnostics);
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
                    message: operator_error_message(&error.to_string()),
                },
                Err(_) => CompletedActionResult {
                    command,
                    completed_at: Instant::now(),
                    outcome: ActionOutcome::Failed,
                    message: "Action could not complete.".to_string(),
                },
            };

            let _ = sender.send(completed);
        });
    }

    fn spawn_shutdown_worker(&self) {
        let sender = self.shutdown_result_tx.clone();
        let diagnostics = self.diagnostics_handle();
        thread::spawn(move || {
            let result = catch_unwind(AssertUnwindSafe(|| {
                let bridge = SupervisorBridge::with_diagnostics(diagnostics);
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
                    message: operator_error_message(&error.to_string()),
                },
                Err(_) => ShutdownRequestResult {
                    completed_at: Instant::now(),
                    accepted: false,
                    message: "Shutdown request could not complete.".to_string(),
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
        self.mark_render_needed();
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
        self.mark_render_needed();
    }

    fn refresh_interval(&self) -> Duration {
        match self.connection {
            ConnectionState::Connected => REFRESH_INTERVAL,
            ConnectionState::Disconnected => DISCONNECTED_REFRESH_INTERVAL,
        }
    }

    fn heartbeat_interval(&self) -> Duration {
        if self.shutdown_in_progress || !self.running_actions.is_empty() {
            ACTIVE_HEARTBEAT_INTERVAL
        } else {
            match self.connection {
                ConnectionState::Connected => CONNECTED_HEARTBEAT_INTERVAL,
                ConnectionState::Disconnected => DISCONNECTED_HEARTBEAT_INTERVAL,
            }
        }
    }
}

fn remaining_until(now: Instant, last: Instant, interval: Duration) -> Duration {
    let elapsed = now.duration_since(last);
    if elapsed >= interval {
        Duration::ZERO
    } else {
        interval - elapsed
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
    let display_name = display_name_for_command(command);
    let message = result
        .message
        .as_deref()
        .or(result.error.as_deref())
        .unwrap_or("Action completed.");

    format!("{display_name}: {message}")
}

pub fn display_name_for_command(command: &str) -> String {
    TuiAction::from_command_name(command)
        .map(TuiAction::display_name)
        .unwrap_or(command)
        .to_string()
}

pub fn operator_error_message(error: &str) -> String {
    let lower = error.to_lowercase();
    if lower.contains("backend unavailable")
        || lower.contains("connection refused")
        || lower.contains("actively refused")
        || lower.contains("connection timed out")
    {
        "Could not contact Supervisor.".to_string()
    } else if lower.contains("could not parse supervisor")
        || lower.contains("response=")
        || lower.contains("json")
    {
        "Could not read the Supervisor response.".to_string()
    } else if lower.contains("closed connection without a response") {
        "Supervisor closed the connection before replying.".to_string()
    } else {
        error.to_string()
    }
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
