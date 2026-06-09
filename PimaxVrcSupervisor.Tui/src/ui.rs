use std::time::Instant;

use ratatui::{
    Frame,
    layout::{Alignment, Constraint, Direction, Layout, Rect},
    style::{Color, Modifier, Style},
    text::{Line, Span},
    widgets::{Block, Borders, Clear, List, ListItem, Paragraph, Wrap},
};

use crate::app::{ActionOutcome, App, ConnectionState, REFRESH_INTERVAL};

const MIN_WIDTH: u16 = 72;
const MIN_HEIGHT: u16 = 20;

pub fn render(frame: &mut Frame<'_>, app: &App) {
    let area = frame.area();
    if area.width < MIN_WIDTH || area.height < MIN_HEIGHT {
        render_small_terminal(frame, area);
        return;
    }

    let now = Instant::now();
    let root = Layout::default()
        .direction(Direction::Vertical)
        .constraints([
            Constraint::Length(3),
            Constraint::Min(7),
            Constraint::Length(5),
            Constraint::Length(9),
            Constraint::Length(1),
        ])
        .split(area);

    render_header(frame, root[0], app, now);

    let body = Layout::default()
        .direction(Direction::Horizontal)
        .constraints([Constraint::Percentage(42), Constraint::Percentage(58)])
        .split(root[1]);

    render_status(frame, body[0], app);
    render_commands(frame, body[1], app);
    render_backend(frame, root[2], app, now);
    render_logs(frame, root[3], app);
    render_footer(frame, root[4]);

    if app.help_visible {
        render_help(frame, area);
    }

    if app.confirmation.is_some() {
        render_restart_osc_router_confirmation(frame, area);
    }
}

fn render_small_terminal(frame: &mut Frame<'_>, area: Rect) {
    let text = vec![
        Line::from(Span::styled(
            "Pimax VRC Supervisor TUI",
            Style::default().add_modifier(Modifier::BOLD),
        )),
        Line::from("Terminal is too small for the dashboard."),
        Line::from(format!("Minimum: {MIN_WIDTH}x{MIN_HEIGHT}")),
        Line::from("Resize the terminal, or press Q / Esc to quit."),
        Line::from("OSC router restart requires confirmation."),
    ];

    frame.render_widget(
        Paragraph::new(text)
            .block(Block::default().borders(Borders::ALL).title("Compact View"))
            .alignment(Alignment::Center)
            .wrap(Wrap { trim: true }),
        area,
    );
}

fn render_header(frame: &mut Frame<'_>, area: Rect, app: &App, now: Instant) {
    let (state, color) = match app.connection {
        ConnectionState::Connected => ("Connected", Color::Green),
        ConnectionState::Disconnected => ("Disconnected", Color::Red),
    };

    let line = Line::from(vec![
        Span::styled("Pimax VRC Supervisor TUI", title_style()),
        Span::raw("  "),
        Span::styled(
            state,
            Style::default().fg(color).add_modifier(Modifier::BOLD),
        ),
        Span::raw(format!("  endpoint {}", app.backend_endpoint)),
        Span::raw(format!("  last ok {}", app.last_success_label(now))),
        Span::raw(format!("  auto refresh {}s", REFRESH_INTERVAL.as_secs())),
    ]);

    let help = Line::from("H Help   F5 Refresh   1 Restart OSC   Q Quit   Esc Cancel/Quit");

    frame.render_widget(
        Paragraph::new(vec![line, help])
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
                let mut lines = vec![Line::from(vec![
                    Span::styled(command.name.as_str(), Style::default().fg(Color::Cyan)),
                    Span::raw(format!(
                        "  {} / {} / action:{}",
                        command.category, command.output_kind, command.action_safety_category
                    )),
                    command_marker(command.dangerous, "[danger]", Color::Red),
                    command_marker(command.requires_confirmation, "[confirm]", Color::Yellow),
                    command_marker(command_is_read_only(command), "[read-only]", Color::Green),
                    command_marker(command.action_supported, "[backend-action]", Color::Blue),
                    command_marker(
                        command.action_supported && !command.tui_executable,
                        "[tui-disabled]",
                        Color::Gray,
                    ),
                    command_marker(command_is_blocked(command), "[blocked]", Color::Red),
                ])];

                if !command_is_read_only(command) && !command.blocked_reason.trim().is_empty() {
                    lines.push(Line::from(vec![
                        Span::styled("  reason: ", Style::default().fg(Color::DarkGray)),
                        Span::raw(command.blocked_reason.as_str()),
                    ]));
                }

                ListItem::new(lines)
            })
            .collect()
    };

    frame.render_widget(
        List::new(items).block(
            Block::default()
                .borders(Borders::ALL)
                .title("Command Capabilities (metadata only)"),
        ),
        area,
    );
}

fn render_backend(frame: &mut Frame<'_>, area: Rect, app: &App, now: Instant) {
    let mut lines = vec![Line::from(vec![
        Span::styled("Action safety: ", label_style()),
        Span::raw("only confirmed restart-osc-router uses action-json."),
    ])];

    let mut backend_line = vec![Span::styled("Backend: ", label_style())];
    match app.connection {
        ConnectionState::Connected => {
            backend_line.push(Span::styled("available", Style::default().fg(Color::Green)));
        }
        ConnectionState::Disconnected => {
            backend_line.push(Span::styled(
                format!("Backend unavailable at {}", app.backend_endpoint),
                Style::default().fg(Color::Red),
            ));
        }
    }

    if let Some(error) = &app.last_error {
        let when = app
            .last_error_label(now)
            .unwrap_or_else(|| "unknown time".to_string());
        backend_line.push(Span::raw("  "));
        backend_line.push(Span::styled(
            format!("Last error ({when}): "),
            label_style(),
        ));
        backend_line.push(Span::raw(error.as_str()));
    } else {
        backend_line.push(Span::raw("  No connection or parse errors recorded."));
    }

    lines.push(Line::from(backend_line));

    if app.action_in_progress {
        let command = app
            .last_action_command
            .as_deref()
            .unwrap_or("restart-osc-router");
        let when = app
            .last_action_started_label(now)
            .unwrap_or_else(|| "unknown time".to_string());
        lines.push(Line::from(vec![
            Span::styled("Action: ", label_style()),
            Span::styled(
                format!("{command} in progress, started {when}"),
                Style::default().fg(Color::Yellow),
            ),
        ]));
    } else if let Some(outcome) = app.last_action_outcome {
        let command = app
            .last_action_command
            .as_deref()
            .unwrap_or("restart-osc-router");
        let when = app
            .last_action_completed_label(now)
            .unwrap_or_else(|| "unknown time".to_string());
        let (status, color) = action_outcome_style(outcome);
        let message = app
            .last_action_result
            .as_deref()
            .or(app.last_action_error.as_deref())
            .unwrap_or("-");
        lines.push(Line::from(vec![
            Span::styled("Last action: ", label_style()),
            Span::styled(
                format!("{command} {status} {when} - "),
                Style::default().fg(color),
            ),
            Span::raw(message),
        ]));
    }

    frame.render_widget(
        Paragraph::new(lines)
            .block(
                Block::default()
                    .borders(Borders::ALL)
                    .title("Backend / Errors"),
            )
            .wrap(Wrap { trim: true }),
        area,
    );
}

fn render_logs(frame: &mut Frame<'_>, area: Rect, app: &App) {
    let visible_rows = area.height.saturating_sub(2) as usize;
    let mut visible_count = 0usize;
    let items = if app.logs.is_empty() {
        vec![ListItem::new("No recent log lines available.")]
    } else {
        app.logs
            .iter()
            .skip(app.log_scroll)
            .take(visible_rows)
            .inspect(|_| visible_count += 1)
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

    let title = format!(
        "Recent Logs ({}/{}, offset {})",
        visible_count,
        app.logs.len(),
        app.log_scroll
    );

    frame.render_widget(
        List::new(items).block(Block::default().borders(Borders::ALL).title(title)),
        area,
    );
}

fn render_footer(frame: &mut Frame<'_>, area: Rect) {
    frame.render_widget(
        Paragraph::new("H Help   F5 Refresh   1 Restart OSC   Q Quit   aliases: R/O"),
        area,
    );
}

fn render_help(frame: &mut Frame<'_>, area: Rect) {
    let popup = centered_rect(62, 54, area);
    let lines = vec![
        Line::from(Span::styled("Keybindings", title_style())),
        Line::from("H       help"),
        Line::from("F5      refresh now"),
        Line::from("1       open Restart OSC Router confirmation"),
        Line::from("Q       close help first, otherwise quit"),
        Line::from("Esc     close help, cancel confirmation, otherwise quit"),
        Line::from("Up/Down scroll logs one line"),
        Line::from("PgUp/PgDn scroll logs one page"),
        Line::from("Home/End jump log scroll"),
        Line::from(""),
        Line::from("Letters are shown uppercase; lowercase input is also accepted."),
        Line::from("R refresh and O restart OSC are convenience aliases."),
        Line::from("Russian layout aliases are limited to selected non-help keys."),
        Line::from(""),
        Line::from("Only restart-osc-router is currently executable."),
        Line::from("It requires confirmation and uses backend action-json."),
        Line::from("Confirmation owns input; help and dashboard keys are ignored there."),
        Line::from("No legacy action commands or other actions are exposed."),
    ];

    frame.render_widget(Clear, popup);
    frame.render_widget(
        Paragraph::new(lines)
            .block(Block::default().borders(Borders::ALL).title("Help"))
            .wrap(Wrap { trim: true }),
        popup,
    );
}

fn render_restart_osc_router_confirmation(frame: &mut Frame<'_>, area: Rect) {
    let popup = centered_rect(64, 48, area);
    let lines = vec![
        Line::from(Span::styled("Confirm Action", title_style())),
        Line::from(""),
        labeled_line("Command: ", "restart-osc-router"),
        labeled_line("Category: ", "LowRisk"),
        Line::from(""),
        Line::from("Expected effect: restarts or manually starts OSC routing."),
        Line::from("This action will be sent to the supervisor backend."),
        Line::from(""),
        Line::from(Span::styled(
            "ENTER Confirm   ESC Cancel",
            Style::default()
                .fg(Color::Yellow)
                .add_modifier(Modifier::BOLD),
        )),
        Line::from("Y also confirms, N/Q cancel."),
    ];

    frame.render_widget(Clear, popup);
    frame.render_widget(
        Paragraph::new(lines)
            .block(
                Block::default()
                    .borders(Borders::ALL)
                    .title("Restart OSC Router"),
            )
            .wrap(Wrap { trim: true }),
        popup,
    );
}

fn centered_rect(percent_x: u16, percent_y: u16, area: Rect) -> Rect {
    let vertical = Layout::default()
        .direction(Direction::Vertical)
        .constraints([
            Constraint::Percentage((100 - percent_y) / 2),
            Constraint::Percentage(percent_y),
            Constraint::Percentage((100 - percent_y) / 2),
        ])
        .split(area);

    Layout::default()
        .direction(Direction::Horizontal)
        .constraints([
            Constraint::Percentage((100 - percent_x) / 2),
            Constraint::Percentage(percent_x),
            Constraint::Percentage((100 - percent_x) / 2),
        ])
        .split(vertical[1])[1]
}

fn label_style() -> Style {
    Style::default()
        .fg(Color::Gray)
        .add_modifier(Modifier::BOLD)
}

fn title_style() -> Style {
    Style::default().add_modifier(Modifier::BOLD)
}

fn labeled_line<'a>(label: &'a str, value: &'a str) -> Line<'a> {
    Line::from(vec![Span::styled(label, label_style()), Span::raw(value)])
}

fn command_marker(show: bool, text: &'static str, color: Color) -> Span<'static> {
    if show {
        Span::styled(format!(" {text}"), Style::default().fg(color))
    } else {
        Span::raw("")
    }
}

fn command_is_blocked(command: &crate::models::CommandSummary) -> bool {
    command
        .action_safety_category
        .eq_ignore_ascii_case("Blocked")
}

fn command_is_read_only(command: &crate::models::CommandSummary) -> bool {
    command
        .action_safety_category
        .eq_ignore_ascii_case("ReadOnly")
}

fn action_outcome_style(outcome: ActionOutcome) -> (&'static str, Color) {
    match outcome {
        ActionOutcome::Succeeded => ("succeeded", Color::Green),
        ActionOutcome::Failed => ("failed", Color::Red),
        ActionOutcome::Cancelled => ("cancelled", Color::Yellow),
        ActionOutcome::Rejected => ("rejected", Color::Yellow),
    }
}
