mod app;
mod bridge;
mod console_close;
mod models;
mod theme;
mod ui;

use std::{io, time::Instant};

use crate::models::TuiAction;
use app::{App, ClickAction, LOG_PAGE_SIZE};
use color_eyre::eyre::Result;
use crossterm::{
    event::{
        self, DisableMouseCapture, EnableMouseCapture, Event, KeyCode, KeyEvent, KeyEventKind,
        MouseButton, MouseEvent, MouseEventKind,
    },
    execute,
    terminal::{EnterAlternateScreen, LeaveAlternateScreen, disable_raw_mode, enable_raw_mode},
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
    execute!(stdout, EnterAlternateScreen)?;
    let mouse_capture_error = match execute!(stdout, EnableMouseCapture) {
        Ok(()) => None,
        Err(error) => Some(error.to_string()),
    };

    let backend = CrosstermBackend::new(stdout);
    let mut terminal = Terminal::new(backend)?;
    let result = run(&mut terminal, mouse_capture_error, console_close_error);

    restore_terminal(&mut terminal)?;

    drop(console_close_guard);

    result
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
) -> Result<()> {
    let mut app = App::new();
    app.set_mouse_status(
        mouse_capture_error.is_none(),
        mouse_capture_error.map(|error| format!("Mouse disabled; keyboard-only mode: {error}")),
    );
    app.set_console_close_status(console_close_error.is_none(), console_close_error);
    app.refresh(Instant::now());

    loop {
        app.drain_action_results();
        app.drain_shutdown_result();
        let now = Instant::now();
        if app.should_auto_refresh(now) {
            app.refresh(now);
        }

        if app.should_exit_after_shutdown(now) {
            break;
        }

        terminal.draw(|frame| ui::render(frame, &mut app))?;

        if event::poll(app.poll_timeout(Instant::now()))? {
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
                _ => {}
            }
        }
    }

    Ok(())
}

fn handle_key(app: &mut App, key: KeyEvent) -> bool {
    let now = Instant::now();
    let shortcut = Shortcut::from_key(key);

    if app.shutdown_confirmation {
        match key.code {
            KeyCode::Enter | KeyCode::Char(' ') => {
                app.confirm_shutdown(now);
                return false;
            }
            KeyCode::Esc => {
                app.cancel_shutdown_confirmation();
                return false;
            }
            _ => return false,
        }
    }

    if app.confirmation.is_some() {
        match key.code {
            KeyCode::Enter | KeyCode::Char(' ') => {
                app.confirm_action(now);
                return false;
            }
            KeyCode::Esc => {
                app.cancel_confirmation(now);
                return false;
            }
            _ => return false,
        }
    }

    if app.help_visible {
        app.close_help();
        return false;
    } else {
        match shortcut {
            Some(Shortcut::Quit) => app.request_shutdown_confirmation(now),
            Some(Shortcut::Cancel) => false,
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
                app.request_action_confirmation(action, now);
                false
            }
            Some(Shortcut::Confirm) => false,
            None => handle_navigation_key(app, key),
        }
    }
}

fn handle_mouse(app: &mut App, mouse: MouseEvent) -> bool {
    if app.help_visible {
        app.close_help();
        return false;
    }

    if app.shutdown_confirmation {
        if !matches!(mouse.kind, MouseEventKind::Down(MouseButton::Left)) {
            return false;
        }

        let now = Instant::now();
        let Some(action) = app.click_action_at(mouse.column, mouse.row) else {
            return false;
        };

        match action {
            ClickAction::ConfirmModal => {
                app.confirm_shutdown(now);
                return false;
            }
            ClickAction::CancelModal => {
                app.cancel_shutdown_confirmation();
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
                app.confirm_action(now);
                return false;
            }
            ClickAction::CancelModal => {
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
        ClickAction::QuitTui => app.request_shutdown_confirmation(now),
        ClickAction::SelectAction(action) => {
            app.request_action_start(action, now);
            false
        }
        ClickAction::ConfirmModal | ClickAction::CancelModal => false,
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
            '1' | '2' | '3' | '4' | '5' | '6' => TuiAction::from_digit(value).map(Self::OpenAction),
            'h' | 'H' => Some(Self::Help),
            'f' | 'F' => Some(Self::FollowLogs),
            'r' | 'R' | 'к' | 'К' => Some(Self::Refresh),
            'q' | 'Q' | 'й' | 'Й' => Some(Self::Quit),
            ' ' => Some(Self::Confirm),
            _ => None,
        }
    }
}
