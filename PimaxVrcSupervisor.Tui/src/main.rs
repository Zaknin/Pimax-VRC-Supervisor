mod app;
mod bridge;
mod console_close;
mod diagnostics;
mod models;
mod supervisor_process;
mod theme;
mod ui;

use std::{ffi::OsStr, io, time::Instant};

use crate::models::{ExitOption, TuiAction};
use app::{App, ClickAction, LOG_PAGE_SIZE, ModalButtonFocus};
use color_eyre::eyre::Result;
use crossterm::{
    event::{
        self, DisableMouseCapture, EnableMouseCapture, Event, KeyCode, KeyEvent, KeyEventKind,
        MouseButton, MouseEvent, MouseEventKind,
    },
    execute,
    terminal::{
        EnterAlternateScreen, LeaveAlternateScreen, SetSize, disable_raw_mode, enable_raw_mode,
        size,
    },
};
use ratatui::{Terminal, backend::CrosstermBackend};

const MOUSE_WHEEL_LOG_LINES: usize = 3;

#[derive(Debug, Clone, Copy, Eq, PartialEq)]
enum Shortcut {
    Help,
    Refresh,
    FollowLogs,
    Quit,
    OpenAction(TuiAction),
    Confirm,
    Cancel,
}

fn main() -> Result<()> {
    color_eyre::install()?;

    let args = std::env::args_os().collect::<Vec<_>>();
    let exit_when_supervisor_exits = args
        .iter()
        .any(|arg| arg == OsStr::new("--exit-when-supervisor-exits"));
    let supervisor_process = supervisor_process::from_args(&args);
    let diagnostics = diagnostics::TuiDiagnostics::from_args(args);

    let mut supervisor_process_notice = None;
    let supervisor_monitor = match supervisor_process {
        supervisor_process::SupervisorPidArgument::AlreadyExited(message) => {
            eprintln!("{message}");
            return Ok(());
        }
        supervisor_process::SupervisorPidArgument::Monitor(monitor) => Some(monitor),
        supervisor_process::SupervisorPidArgument::Fallback(message) => {
            eprintln!("{message}");
            supervisor_process_notice = Some(message);
            None
        }
        supervisor_process::SupervisorPidArgument::None => None,
    };

    let (console_close_guard, console_close_error) = match console_close::install() {
        Ok(guard) => (Some(guard), None),
        Err(error) => (
            None,
            Some(format!(
                "Window-close shutdown handler disabled; keyboard shutdown still works: {error}"
            )),
        ),
    };

    enable_raw_mode()?;
    let mut stdout = io::stdout();
    request_initial_full_layout_size(&mut stdout);
    execute!(stdout, EnterAlternateScreen)?;
    let mouse_capture_error = match execute!(stdout, EnableMouseCapture) {
        Ok(()) => None,
        Err(error) => Some(error.to_string()),
    };

    let backend = CrosstermBackend::new(stdout);
    let mut terminal = Terminal::new(backend)?;
    let result = run(
        &mut terminal,
        mouse_capture_error,
        console_close_error,
        diagnostics,
        exit_when_supervisor_exits,
        supervisor_monitor,
        supervisor_process_notice,
    );

    restore_terminal(&mut terminal)?;

    drop(console_close_guard);

    result
}

fn request_initial_full_layout_size(stdout: &mut io::Stdout) {
    let Ok((width, height)) = size() else {
        return;
    };

    let requested_width = width.max(ui::FULL_MIN_WIDTH);
    let requested_height = height.max(ui::FULL_MIN_HEIGHT);
    if requested_width == width && requested_height == height {
        return;
    }

    let _ = execute!(stdout, SetSize(requested_width, requested_height));
}

fn restore_terminal(terminal: &mut Terminal<CrosstermBackend<io::Stdout>>) -> io::Result<()> {
    disable_raw_mode()?;
    execute!(
        terminal.backend_mut(),
        DisableMouseCapture,
        LeaveAlternateScreen
    )?;
    terminal.show_cursor()?;
    Ok(())
}

fn run(
    terminal: &mut Terminal<CrosstermBackend<io::Stdout>>,
    mouse_capture_error: Option<String>,
    console_close_error: Option<String>,
    diagnostics: diagnostics::TuiDiagnostics,
    exit_when_supervisor_exits: bool,
    supervisor_monitor: Option<supervisor_process::SupervisorProcessMonitor>,
    supervisor_process_notice: Option<String>,
) -> Result<()> {
    let bridge_disconnect_auto_exit = exit_when_supervisor_exits && supervisor_monitor.is_none();
    let mut app = App::new(diagnostics, bridge_disconnect_auto_exit);
    app.set_mouse_status(
        mouse_capture_error.is_none(),
        mouse_capture_error.map(|error| format!("Mouse disabled; keyboard-only mode: {error}")),
    );
    app.set_console_close_status(console_close_error.is_none(), console_close_error);
    app.set_supervisor_process_notice(supervisor_process_notice);
    app.refresh(Instant::now());

    loop {
        if supervisor_monitor
            .as_ref()
            .is_some_and(|monitor| monitor.try_recv_exit())
        {
            break;
        }

        app.drain_action_results();
        app.drain_shutdown_result();
        let now = Instant::now();
        if app.should_auto_refresh(now) {
            app.refresh(now);
        }

        if app.should_exit_after_shutdown(now) {
            break;
        }

        if app.should_close_tui() {
            break;
        }

        if app.should_exit_after_supervisor_disconnect(now) {
            break;
        }

        let now = Instant::now();
        if app.should_render(now) {
            terminal.draw(|frame| ui::render(frame, &mut app))?;
            app.record_render(now);
        }
        app.maybe_write_diagnostics(Instant::now());

        if event::poll(app.poll_timeout(Instant::now()))? {
            app.record_input_wakeup();
            match event::read()? {
                Event::Key(key) => {
                    if key.kind != KeyEventKind::Press {
                        continue;
                    }

                    if handle_key(&mut app, key) {
                        break;
                    }
                }
                Event::Mouse(mouse) => {
                    if handle_mouse(&mut app, mouse) {
                        break;
                    }
                }
                Event::Resize(_, _) => {
                    app.mark_render_needed();
                }
                _ => {}
            }
        }

        app.maybe_write_diagnostics(Instant::now());
    }

    Ok(())
}

fn handle_key(app: &mut App, key: KeyEvent) -> bool {
    let now = Instant::now();
    let shortcut = Shortcut::from_key(key);

    if app.action_result_dialog.is_some() {
        match key.code {
            KeyCode::Enter | KeyCode::Char(' ') | KeyCode::Esc => {
                app.acknowledge_action_result();
                return false;
            }
            _ => return false,
        }
    }

    if app.exit_dialog {
        match key.code {
            KeyCode::Enter | KeyCode::Char(' ') => {
                return app.confirm_selected_exit_option(now);
            }
            KeyCode::Esc => {
                app.cancel_exit_dialog();
                return false;
            }
            KeyCode::Up => {
                app.move_exit_selection_up();
                return false;
            }
            KeyCode::Down => {
                app.move_exit_selection_down();
                return false;
            }
            KeyCode::Char(value) => {
                if let Some(option) = ExitOption::from_digit(value) {
                    return app.confirm_exit_option(option, now);
                }
                return false;
            }
            _ => return false,
        }
    }

    if app.confirmation.is_some() {
        match key.code {
            KeyCode::Enter | KeyCode::Char(' ') => {
                app.activate_focused_confirmation(now);
                return false;
            }
            KeyCode::Esc => {
                app.cancel_confirmation(now);
                return false;
            }
            KeyCode::Tab | KeyCode::Left | KeyCode::Right => {
                app.move_confirmation_focus();
                return false;
            }
            _ => return false,
        }
    }

    if app.help_visible {
        match key.code {
            KeyCode::Enter | KeyCode::Char(' ') | KeyCode::Esc => app.close_help(),
            _ => {}
        }
        false
    } else {
        match shortcut {
            Some(Shortcut::Quit) | Some(Shortcut::Cancel) => app.request_exit_dialog(now),
            Some(Shortcut::Refresh) => {
                app.refresh(now);
                false
            }
            Some(Shortcut::FollowLogs) => {
                app.follow_latest_logs();
                false
            }
            Some(Shortcut::Help) => {
                app.toggle_help();
                false
            }
            Some(Shortcut::OpenAction(action)) => {
                app.activate_action(action, now);
                false
            }
            Some(Shortcut::Confirm) => false,
            None => handle_navigation_key(app, key),
        }
    }
}

fn handle_mouse(app: &mut App, mouse: MouseEvent) -> bool {
    if app.action_result_dialog.is_some() {
        if !matches!(mouse.kind, MouseEventKind::Down(MouseButton::Left)) {
            return false;
        }

        let Some(action) = app.click_action_at(mouse.column, mouse.row) else {
            return false;
        };

        if matches!(action, ClickAction::CloseModal) {
            app.acknowledge_action_result();
        }

        return false;
    }

    if app.help_visible {
        if matches!(mouse.kind, MouseEventKind::Down(MouseButton::Left))
            && app.click_action_at(mouse.column, mouse.row) == Some(ClickAction::CloseModal)
        {
            app.close_help();
        }
        return false;
    }

    if app.exit_dialog {
        if !matches!(mouse.kind, MouseEventKind::Down(MouseButton::Left)) {
            return false;
        }

        let now = Instant::now();
        let Some(action) = app.click_action_at(mouse.column, mouse.row) else {
            return false;
        };

        match action {
            ClickAction::ConfirmModal => {
                return app.confirm_selected_exit_option(now);
            }
            ClickAction::CancelModal => {
                app.cancel_exit_dialog();
                return false;
            }
            _ => return false,
        }
    }

    if app.confirmation.is_some() {
        if !matches!(mouse.kind, MouseEventKind::Down(MouseButton::Left)) {
            return false;
        }

        let now = Instant::now();
        let Some(action) = app.click_action_at(mouse.column, mouse.row) else {
            return false;
        };

        match action {
            ClickAction::ConfirmModal => {
                app.focus_confirmation_button(ModalButtonFocus::Confirm);
                app.confirm_action(now);
                return false;
            }
            ClickAction::CancelModal => {
                app.focus_confirmation_button(ModalButtonFocus::Cancel);
                app.cancel_confirmation(now);
                return false;
            }
            _ => return false,
        }
    }

    match mouse.kind {
        MouseEventKind::ScrollUp => {
            app.scroll_logs_up(MOUSE_WHEEL_LOG_LINES);
            return false;
        }
        MouseEventKind::ScrollDown => {
            app.scroll_logs_down(MOUSE_WHEEL_LOG_LINES);
            return false;
        }
        _ => {}
    }

    if !matches!(
        mouse.kind,
        MouseEventKind::Down(MouseButton::Left) | MouseEventKind::Up(MouseButton::Left)
    ) {
        return false;
    }

    if !matches!(mouse.kind, MouseEventKind::Down(MouseButton::Left)) {
        return false;
    }

    let now = Instant::now();

    let Some(action) = app.click_action_at(mouse.column, mouse.row) else {
        return false;
    };

    match action {
        ClickAction::OpenHelp => {
            app.toggle_help();
            false
        }
        ClickAction::Refresh => {
            app.refresh(now);
            false
        }
        ClickAction::QuitTui => app.request_exit_dialog(now),
        ClickAction::SelectAction(action) => {
            app.activate_action(action, now);
            false
        }
        ClickAction::ConfirmModal | ClickAction::CancelModal | ClickAction::CloseModal => false,
    }
}

fn handle_navigation_key(app: &mut App, key: KeyEvent) -> bool {
    match key.code {
        KeyCode::Up => {
            app.scroll_logs_up(1);
            false
        }
        KeyCode::Down => {
            app.scroll_logs_down(1);
            false
        }
        KeyCode::PageUp => {
            app.scroll_logs_up(LOG_PAGE_SIZE);
            false
        }
        KeyCode::PageDown => {
            app.scroll_logs_down(LOG_PAGE_SIZE);
            false
        }
        KeyCode::Home => {
            app.scroll_logs_home();
            false
        }
        KeyCode::End => {
            app.scroll_logs_end();
            false
        }
        _ => false,
    }
}

impl Shortcut {
    fn from_key(key: KeyEvent) -> Option<Self> {
        match key.code {
            KeyCode::F(5) => Some(Self::Refresh),
            KeyCode::Enter => Some(Self::Confirm),
            KeyCode::Esc => Some(Self::Cancel),
            KeyCode::Char(value) => Self::from_char(value),
            _ => None,
        }
    }

    fn from_char(value: char) -> Option<Self> {
        match value {
            '0' => Some(Self::Help),
            '1' | '2' | '3' | '4' | '5' | '6' | '7' => {
                TuiAction::from_digit(value).map(Self::OpenAction)
            }
            'h' | 'H' => Some(Self::Help),
            'f' | 'F' => Some(Self::FollowLogs),
            'r' | 'R' | 'к' | 'К' => Some(Self::Refresh),
            'q' | 'Q' | 'й' | 'Й' => Some(Self::Quit),
            ' ' => Some(Self::Confirm),
            _ => None,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::{
        diagnostics::TuiDiagnostics,
        models::{CommandSummary, RESTART_VR_SESSION_COMMAND, START_STEAMVR_COMMAND},
    };
    use crossterm::event::KeyModifiers;
    use ratatui::layout::Rect;

    fn connected_app() -> App {
        let mut app = App::new(TuiDiagnostics::disabled(), false);
        app.connection = app::ConnectionState::Connected;
        app.status.steam_vr = "running".to_string();
        app.commands = vec![
            command(START_STEAMVR_COMMAND),
            command(RESTART_VR_SESSION_COMMAND),
        ];
        app
    }

    fn command(name: &str) -> CommandSummary {
        CommandSummary {
            name: name.to_string(),
            category: "Actions".to_string(),
            output_kind: "Text".to_string(),
            dangerous: false,
            requires_confirmation: true,
            action_supported: true,
            action_safety_category: "Managed".to_string(),
            tui_executable: true,
            blocked_reason: String::new(),
        }
    }

    fn key(code: KeyCode) -> KeyEvent {
        KeyEvent::new(code, KeyModifiers::NONE)
    }

    fn click(column: u16, row: u16) -> MouseEvent {
        MouseEvent {
            kind: MouseEventKind::Down(MouseButton::Left),
            column,
            row,
            modifiers: KeyModifiers::NONE,
        }
    }

    #[test]
    fn confirmation_defaults_to_cancel_and_enter_cancels() {
        let mut app = connected_app();
        app.activate_action(TuiAction::RestartVrSession, Instant::now());

        assert_eq!(app.confirmation_focus, ModalButtonFocus::Cancel);
        assert!(!handle_key(&mut app, key(KeyCode::Enter)));
        assert!(app.confirmation.is_none());
        assert!(app.running_actions.is_empty());
    }

    #[test]
    fn tab_and_arrows_move_focus_and_space_sends_exactly_one_command() {
        let mut app = connected_app();
        app.activate_action(TuiAction::RestartVrSession, Instant::now());

        handle_key(&mut app, key(KeyCode::Tab));
        assert_eq!(app.confirmation_focus, ModalButtonFocus::Confirm);
        handle_key(&mut app, key(KeyCode::Right));
        assert_eq!(app.confirmation_focus, ModalButtonFocus::Cancel);
        handle_key(&mut app, key(KeyCode::Left));
        assert_eq!(app.confirmation_focus, ModalButtonFocus::Confirm);
        handle_key(&mut app, key(KeyCode::Char(' ')));
        handle_key(&mut app, key(KeyCode::Char(' ')));

        assert!(app.confirmation.is_none());
        assert_eq!(app.running_actions.len(), 1);
        assert_eq!(app.running_actions[0].command, RESTART_VR_SESSION_COMMAND);
    }

    #[test]
    fn escape_always_cancels_confirmation() {
        let mut app = connected_app();
        app.activate_action(TuiAction::RestartVrSession, Instant::now());
        app.focus_confirmation_button(ModalButtonFocus::Confirm);

        handle_key(&mut app, key(KeyCode::Esc));

        assert!(app.confirmation.is_none());
        assert!(app.running_actions.is_empty());
    }

    #[test]
    fn mouse_confirm_cancel_and_outside_clicks_use_exact_regions() {
        let mut cancel_app = connected_app();
        cancel_app.activate_action(TuiAction::RestartVrSession, Instant::now());
        cancel_app.add_click_region(Rect::new(10, 10, 11, 1), ClickAction::ConfirmModal);
        cancel_app.add_click_region(Rect::new(25, 10, 10, 1), ClickAction::CancelModal);

        handle_mouse(&mut cancel_app, click(1, 1));
        assert!(cancel_app.confirmation.is_some());
        handle_mouse(&mut cancel_app, click(25, 10));
        assert!(cancel_app.confirmation.is_none());
        assert!(cancel_app.running_actions.is_empty());

        let mut confirm_app = connected_app();
        confirm_app.activate_action(TuiAction::RestartVrSession, Instant::now());
        confirm_app.add_click_region(Rect::new(10, 10, 11, 1), ClickAction::ConfirmModal);
        handle_mouse(&mut confirm_app, click(10, 10));
        handle_mouse(&mut confirm_app, click(10, 10));

        assert!(confirm_app.confirmation.is_none());
        assert_eq!(confirm_app.running_actions.len(), 1);
    }

    #[test]
    fn help_closes_only_from_supported_keys_or_visible_close_button() {
        let mut app = connected_app();
        app.help_visible = true;
        app.add_click_region(Rect::new(20, 10, 9, 1), ClickAction::CloseModal);

        handle_key(&mut app, key(KeyCode::Char('x')));
        handle_mouse(&mut app, click(1, 1));
        assert!(app.help_visible);

        handle_mouse(&mut app, click(20, 10));
        assert!(!app.help_visible);
    }
}
