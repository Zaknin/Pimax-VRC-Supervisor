mod app;
mod bridge;
mod models;
mod ui;

use std::{io, time::Instant};

use app::{App, LOG_PAGE_SIZE};
use color_eyre::eyre::Result;
use crossterm::{
    event::{self, Event, KeyCode, KeyEvent, KeyEventKind},
    execute,
    terminal::{EnterAlternateScreen, LeaveAlternateScreen, disable_raw_mode, enable_raw_mode},
};
use ratatui::{Terminal, backend::CrosstermBackend};

#[derive(Debug, Clone, Copy, Eq, PartialEq)]
enum Shortcut {
    Help,
    Refresh,
    Quit,
    OpenRestartOscRouter,
    Confirm,
    Cancel,
}

fn main() -> Result<()> {
    color_eyre::install()?;

    enable_raw_mode()?;
    let mut stdout = io::stdout();
    execute!(stdout, EnterAlternateScreen)?;

    let backend = CrosstermBackend::new(stdout);
    let mut terminal = Terminal::new(backend)?;
    let result = run(&mut terminal);

    disable_raw_mode()?;
    execute!(terminal.backend_mut(), LeaveAlternateScreen)?;
    terminal.show_cursor()?;

    result
}

fn run(terminal: &mut Terminal<CrosstermBackend<io::Stdout>>) -> Result<()> {
    let mut app = App::new();
    app.refresh(Instant::now());

    loop {
        let now = Instant::now();
        if app.should_auto_refresh(now) {
            app.refresh(now);
        }

        terminal.draw(|frame| ui::render(frame, &app))?;

        if event::poll(app.poll_timeout(Instant::now()))? {
            if let Event::Key(key) = event::read()? {
                if key.kind != KeyEventKind::Press {
                    continue;
                }

                if handle_key(&mut app, key) {
                    break;
                }
            }
        }
    }

    Ok(())
}

fn handle_key(app: &mut App, key: KeyEvent) -> bool {
    let now = Instant::now();
    let shortcut = Shortcut::from_key(key);

    if app.confirmation.is_some() {
        match shortcut {
            Some(Shortcut::Confirm) => {
                app.confirm_restart_osc_router(now);
                return false;
            }
            Some(Shortcut::Cancel | Shortcut::Quit) => {
                app.cancel_confirmation(now);
                return false;
            }
            _ => return false,
        }
    }

    if app.help_visible {
        match shortcut {
            Some(Shortcut::Help) => {
                app.toggle_help();
                false
            }
            Some(Shortcut::Cancel | Shortcut::Quit) => {
                app.close_help();
                false
            }
            _ => false,
        }
    } else {
        match shortcut {
            Some(Shortcut::Quit) => true,
            Some(Shortcut::Cancel) => true,
            Some(Shortcut::Refresh) => {
                app.refresh(now);
                false
            }
            Some(Shortcut::Help) => {
                app.toggle_help();
                false
            }
            Some(Shortcut::OpenRestartOscRouter) => {
                app.request_restart_osc_router_confirmation(now);
                false
            }
            Some(Shortcut::Confirm) => false,
            None => handle_navigation_key(app, key),
        }
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
            '1' => Some(Self::OpenRestartOscRouter),
            'h' | 'H' => Some(Self::Help),
            'r' | 'R' | 'к' | 'К' => Some(Self::Refresh),
            'q' | 'Q' | 'й' | 'Й' => Some(Self::Quit),
            'o' | 'O' | 'щ' | 'Щ' => Some(Self::OpenRestartOscRouter),
            'y' | 'Y' | 'н' | 'Н' => Some(Self::Confirm),
            'n' | 'N' | 'т' | 'Т' => Some(Self::Cancel),
            _ => None,
        }
    }
}
