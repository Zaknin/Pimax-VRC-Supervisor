use ratatui::{
    layout::{Constraint, Direction, Layout, Rect},
    style::{Color, Modifier, Style},
    text::{Line, Span},
    widgets::{Block, Borders, List, ListItem, Paragraph, Wrap},
    Frame,
};

use crate::app::{App, ConnectionState};

pub fn render(frame: &mut Frame<'_>, app: &App) {
    let root = Layout::default()
        .direction(Direction::Vertical)
        .constraints([
            Constraint::Length(3),
            Constraint::Min(8),
            Constraint::Length(10),
            Constraint::Length(1),
        ])
        .split(frame.area());

    render_header(frame, root[0], app);

    let body = Layout::default()
        .direction(Direction::Horizontal)
        .constraints([Constraint::Percentage(42), Constraint::Percentage(58)])
        .split(root[1]);

    render_status(frame, body[0], app);
    render_commands(frame, body[1], app);
    render_logs(frame, root[2], app);
    render_footer(frame, root[3]);
}

fn render_header(frame: &mut Frame<'_>, area: Rect, app: &App) {
    let (state, color) = match app.connection {
        ConnectionState::Connected => ("Connected", Color::Green),
        ConnectionState::Disconnected => ("Disconnected", Color::Red),
    };

    let mut line = vec![
        Span::styled(
            "Pimax VRC Supervisor TUI",
            Style::default().add_modifier(Modifier::BOLD),
        ),
        Span::raw("  "),
        Span::styled(state, Style::default().fg(color)),
    ];

    if let Some(error) = &app.error {
        line.push(Span::raw("  "));
        line.push(Span::styled(error.as_str(), Style::default().fg(Color::Yellow)));
    }

    frame.render_widget(
        Paragraph::new(Line::from(line))
            .block(Block::default().borders(Borders::ALL).title("Dashboard"))
            .wrap(Wrap { trim: true }),
        area,
    );
}

fn render_status(frame: &mut Frame<'_>, area: Rect, app: &App) {
    let status = &app.status;
    let lines = vec![
        labeled_line("App version: ", status.app_version.as_str()),
        labeled_line("Mode: ", status.mode.as_str()),
        labeled_line("SteamVR: ", status.steam_vr.as_str()),
        labeled_line("Lifecycle: ", status.lifecycle.as_str()),
        labeled_line("Core apps: ", status.core_apps.as_str()),
        labeled_line("Base stations: ", status.base_stations.as_str()),
        labeled_line("OSC router: ", status.osc_router.as_str()),
        labeled_line("OSCGoesBrrr: ", status.osc_goes_brrr.as_str()),
    ];

    frame.render_widget(
        Paragraph::new(lines)
            .block(Block::default().borders(Borders::ALL).title("Status"))
            .wrap(Wrap { trim: true }),
        area,
    );
}

fn render_commands(frame: &mut Frame<'_>, area: Rect, app: &App) {
    let items = if app.commands.is_empty() {
        vec![ListItem::new("No command metadata available.")]
    } else {
        app.commands
            .iter()
            .map(|command| {
                let marker = if command.dangerous {
                    "danger"
                } else if command.requires_confirmation {
                    "confirm"
                } else {
                    "read-only/action"
                };

                ListItem::new(Line::from(vec![
                    Span::styled(command.name.as_str(), Style::default().fg(Color::Cyan)),
                    Span::raw(format!(
                        "  {} / {} / {}",
                        command.category, command.output_kind, marker
                    )),
                ]))
            })
            .collect()
    };

    frame.render_widget(
        List::new(items).block(Block::default().borders(Borders::ALL).title("Bridge Commands")),
        area,
    );
}

fn render_logs(frame: &mut Frame<'_>, area: Rect, app: &App) {
    let items = if app.logs.is_empty() {
        vec![ListItem::new("No recent log lines available.")]
    } else {
        app.logs
            .iter()
            .map(|line| {
                let message = if line.message == "-" {
                    line.raw.as_str()
                } else {
                    line.message.as_str()
                };

                match &line.timestamp {
                    Some(timestamp) => ListItem::new(Line::from(vec![
                        Span::styled(timestamp.as_str(), Style::default().fg(Color::DarkGray)),
                        Span::raw("  "),
                        Span::raw(message),
                    ])),
                    None => ListItem::new(message),
                }
            })
            .collect()
    };

    frame.render_widget(
        List::new(items).block(Block::default().borders(Borders::ALL).title("Recent Logs")),
        area,
    );
}

fn render_footer(frame: &mut Frame<'_>, area: Rect) {
    frame.render_widget(
        Paragraph::new("r refresh   q/Esc quit   read-only query-json client"),
        area,
    );
}

fn label_style() -> Style {
    Style::default()
        .fg(Color::Gray)
        .add_modifier(Modifier::BOLD)
}

fn labeled_line<'a>(label: &'a str, value: &'a str) -> Line<'a> {
    Line::from(vec![Span::styled(label, label_style()), Span::raw(value)])
}
