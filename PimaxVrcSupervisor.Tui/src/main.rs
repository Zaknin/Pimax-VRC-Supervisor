mod app;
mod bridge;
mod models;
mod ui;

use std::{io, time::Instant};

use app::{App, LOG_PAGE_SIZE};
use color_eyre::eyre::Result;
use crossterm::{
    event::{self, Event, KeyCode, KeyEvent},
    execute,
    terminal::{EnterAlternateScreen, LeaveAlternateScreen, disable_raw_mode, enable_raw_mode},
};
use ratatui::{Terminal, backend::CrosstermBackend};

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
                if handle_key(&mut app, key) {
                    break;
                }
            }
        }
    }

    Ok(())
}

fn handle_key(app: &mut App, key: KeyEvent) -> bool {
    if app.confirmation.is_some() {
        match key.code {
            KeyCode::Char('y') | KeyCode::Char('Y') => {
                app.confirm_restart_osc_router(Instant::now());
                return false;
            }
            KeyCode::Char('n')
            | KeyCode::Char('N')
            | KeyCode::Char('q')
            | KeyCode::Char('Q')
            | KeyCode::Esc => {
                app.cancel_confirmation();
                return false;
            }
            _ => return false,
        }
    }

    match key.code {
        KeyCode::Char('q') | KeyCode::Char('Q') => true,
        KeyCode::Esc => {
            if app.help_visible {
                app.close_help();
                false
            } else {
                true
            }
        }
        KeyCode::Char('r') | KeyCode::Char('R') => {
            app.refresh(Instant::now());
            false
        }
        KeyCode::Char('h') | KeyCode::Char('H') | KeyCode::Char('?') => {
            app.toggle_help();
            false
        }
        KeyCode::Char('o') | KeyCode::Char('O') => {
            app.request_restart_osc_router_confirmation(Instant::now());
            false
        }
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
