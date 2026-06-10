use std::time::Instant;

use ratatui::{
    Frame,
    layout::{Alignment, Constraint, Direction, Layout, Rect},
    style::{Color, Style},
    text::{Line, Span},
    widgets::{Block, Borders, Clear, List, ListItem, Paragraph, Wrap},
};

use crate::{
    app::{ActionOutcome, App, ConnectionState, REFRESH_INTERVAL},
    models::{CommandSummary, TuiAction},
    theme,
};

const MIN_WIDTH: u16 = 72;
const MIN_HEIGHT: u16 = 24;

pub fn render(frame: &mut Frame<'_>, app: &App) {
    let area = frame.area();
    frame.render_widget(Block::default().style(theme::app_style()), area);

    if area.width < MIN_WIDTH || area.height < MIN_HEIGHT {
        render_small_terminal(frame, area);
        return;
    }

    let now = Instant::now();
    let root = Layout::default()
        .direction(Direction::Vertical)
        .constraints([
            Constraint::Length(3),
            Constraint::Min(11),
            Constraint::Length(7),
            Constraint::Min(7),
            Constraint::Length(1),
        ])
        .split(area);

    render_header(frame, root[0], app, now);

    let body = Layout::default()
        .direction(Direction::Horizontal)
        .constraints([Constraint::Percentage(38), Constraint::Percentage(62)])
        .split(root[1]);

    render_status(frame, body[0], app);
    render_actions(frame, body[1], app, now);

    let middle = Layout::default()
        .direction(Direction::Horizontal)
        .constraints([Constraint::Percentage(55), Constraint::Percentage(45)])
        .split(root[2]);
    render_action_activity(frame, middle[0], app, now);
    render_backend(frame, middle[1], app, now);

    render_logs(frame, root[3], app);
    render_footer(frame, root[4]);

    if app.help_visible {
        render_help(frame, area);
    }

    if app.confirmation.is_some() {
        render_action_confirmation(frame, area, app);
    }
}

fn render_small_terminal(frame: &mut Frame<'_>, area: Rect) {
    let lines = vec![
        Line::from(Span::styled(
            "Pimax VRC Supervisor TUI",
            theme::title_style(),
        )),
        Line::from("Terminal too small for full dashboard."),
        Line::from("Resize window for action cards and logs."),
        Line::from(""),
        Line::from(vec![
            Span::styled("0 Help", theme::success_style()),
            Span::raw("   "),
            Span::styled("Q Quit TUI", theme::warning_style()),
        ]),
    ];

    frame.render_widget(
        Paragraph::new(lines)
            .block(theme::accent_panel_block("Compact View"))
            .alignment(Alignment::Center)
            .wrap(Wrap { trim: true }),
        area,
    );
}

fn render_header(frame: &mut Frame<'_>, area: Rect, app: &App, now: Instant) {
    let (backend_label, backend_style) = match app.connection {
        ConnectionState::Connected => ("OK", theme::success_style()),
        ConnectionState::Disconnected => ("UNAVAILABLE", theme::error_style()),
    };

    let mut line = vec![
        Span::styled("Pimax VRC Supervisor TUI", theme::title_style()),
        Span::raw("   Backend "),
        theme::badge(backend_label, backend_style),
        Span::raw(format!("   {}", app.backend_endpoint)),
        Span::styled(
            format!("   Last OK {}", app.last_success_label(now)),
            theme::secondary_style(),
        ),
        Span::styled(
            format!("   Refresh {}s", REFRESH_INTERVAL.as_secs()),
            theme::secondary_style(),
        ),
    ];

    if !app.running_actions.is_empty() {
        line.push(Span::styled(
            format!("   Running {}", app.running_actions.len()),
            theme::warning_style(),
        ));
    }

    if app.connection == ConnectionState::Disconnected {
        if let Some(error) = &app.last_error {
            line.push(Span::styled(
                format!("   Last error: {}", truncate(error, 52)),
                theme::error_style(),
            ));
        }
    }

    let help = Line::from(shortcut_line(area.width));

    frame.render_widget(
        Paragraph::new(vec![Line::from(line), help])
            .block(theme::accent_panel_block("Dashboard"))
            .wrap(Wrap { trim: true }),
        area,
    );
}

fn render_status(frame: &mut Frame<'_>, area: Rect, app: &App) {
    let status = &app.status;
    let lines = vec![
        labeled_line("Version", status.app_version.as_str()),
        labeled_line("Mode", status.mode.as_str()),
        status_line(
            "Lifecycle",
            status.lifecycle.as_str(),
            status_badge("lifecycle", &status.lifecycle),
        ),
        status_line(
            "SteamVR",
            status.steam_vr.as_str(),
            status_badge("steamvr", &status.steam_vr),
        ),
        status_line(
            "Core Apps",
            status.core_apps.as_str(),
            status_badge("core", &status.core_apps),
        ),
        status_line(
            "OSC Router",
            status.osc_router.as_str(),
            status_badge("osc", &status.osc_router),
        ),
        status_line(
            "OSCGoesBrrr",
            status.osc_goes_brrr.as_str(),
            status_badge("ogb", &status.osc_goes_brrr),
        ),
        status_line(
            "Base Stations",
            status.base_stations.as_str(),
            status_badge("base", &status.base_stations),
        ),
    ];

    frame.render_widget(
        Paragraph::new(lines)
            .block(theme::panel_block("Supervisor"))
            .wrap(Wrap { trim: true }),
        area,
    );
}

fn render_actions(frame: &mut Frame<'_>, area: Rect, app: &App, now: Instant) {
    let block = theme::panel_block("Actions");
    let inner = block.inner(area);
    frame.render_widget(block, area);

    if inner.height < 6 || inner.width < 48 {
        let lines = TuiAction::ALL
            .iter()
            .map(|action| action_card_line(app, *action, now))
            .collect::<Vec<_>>();
        frame.render_widget(Paragraph::new(lines).wrap(Wrap { trim: true }), inner);
        return;
    }

    let rows = Layout::default()
        .direction(Direction::Vertical)
        .constraints([Constraint::Percentage(50), Constraint::Percentage(50)])
        .split(inner);

    for row_index in 0..2 {
        let columns = Layout::default()
            .direction(Direction::Horizontal)
            .constraints([
                Constraint::Percentage(33),
                Constraint::Percentage(34),
                Constraint::Percentage(33),
            ])
            .split(rows[row_index]);

        for column_index in 0..3 {
            let action_index = row_index * 3 + column_index;
            let Some(action) = TuiAction::ALL.get(action_index).copied() else {
                continue;
            };
            render_action_card(frame, columns[column_index], app, action, now);
        }
    }
}

fn render_action_card(
    frame: &mut Frame<'_>,
    area: Rect,
    app: &App,
    action: TuiAction,
    now: Instant,
) {
    let state = action_state(app, action, now);
    let title = format!("{} {}", action.digit(), action.short_label());
    let block = Block::default()
        .borders(Borders::ALL)
        .border_type(ratatui::widgets::BorderType::Rounded)
        .border_style(Style::default().fg(state.color))
        .style(
            Style::default()
                .fg(theme::TEXT_PRIMARY)
                .bg(theme::PANEL_ELEVATED),
        )
        .title(Span::styled(title, theme::title_style()));

    let mut lines = vec![
        Line::from(theme::badge(state.label, state.style)),
        Line::from(Span::styled(action.display_name(), theme::primary_style())),
    ];

    if let Some(detail) = state.detail {
        lines.push(Line::from(Span::styled(detail, theme::secondary_style())));
    }

    frame.render_widget(
        Paragraph::new(lines)
            .block(block)
            .alignment(Alignment::Left)
            .wrap(Wrap { trim: true }),
        area,
    );
}

fn render_action_activity(frame: &mut Frame<'_>, area: Rect, app: &App, now: Instant) {
    let mut lines = vec![Line::from(Span::styled(
        "Running actions",
        theme::title_style(),
    ))];

    if app.running_actions.is_empty() {
        lines.push(Line::from(Span::styled(
            "No running actions.",
            theme::secondary_style(),
        )));
    } else {
        for running in app.running_actions.iter().take(3) {
            lines.push(Line::from(vec![
                Span::styled("- ", theme::dim_style()),
                Span::styled(running.command.clone(), theme::primary_style()),
                Span::raw("  "),
                Span::styled(
                    format!(
                        "RUNNING {}",
                        app.running_action_label(running, now)
                            .split_whitespace()
                            .last()
                            .unwrap_or("")
                    ),
                    theme::warning_style(),
                ),
            ]));
        }
        if app.running_actions.len() > 3 {
            lines.push(Line::from(Span::styled(
                format!("{} more running", app.running_actions.len() - 3),
                theme::secondary_style(),
            )));
        }
    }

    lines.push(Line::from(""));
    lines.push(Line::from(Span::styled(
        "Last result",
        theme::title_style(),
    )));
    if let Some(outcome) = app.last_action_outcome {
        let command = app.last_action_command.as_deref().unwrap_or("action");
        let when = app
            .last_action_completed_label(now)
            .unwrap_or_else(|| "unknown time".to_string());
        let (status, style) = action_outcome_style(outcome);
        let message = app
            .last_action_result
            .as_deref()
            .or(app.last_action_error.as_deref())
            .unwrap_or("-");
        lines.push(Line::from(vec![
            Span::styled("- ", theme::dim_style()),
            Span::styled(command.to_string(), theme::primary_style()),
            Span::raw("  "),
            Span::styled(status, style),
            Span::styled(format!(" {when} - "), theme::secondary_style()),
            Span::raw(truncate(message, 90)),
        ]));
    } else if let Some(result) = app.last_action_result.as_deref() {
        let command = app.last_action_command.as_deref().unwrap_or("action");
        lines.push(Line::from(vec![
            Span::styled("- ", theme::dim_style()),
            Span::styled(command.to_string(), theme::warning_style()),
            Span::raw("  "),
            Span::raw(truncate(result, 90)),
        ]));
    } else {
        lines.push(Line::from(Span::styled(
            "No actions completed yet.",
            theme::secondary_style(),
        )));
    }

    frame.render_widget(
        Paragraph::new(lines)
            .block(theme::panel_block("Action Status"))
            .wrap(Wrap { trim: true }),
        area,
    );
}

fn render_backend(frame: &mut Frame<'_>, area: Rect, app: &App, now: Instant) {
    let mut lines = vec![Line::from(vec![
        Span::styled("Action safety: ", theme::label_style()),
        Span::raw("confirmed 1-6 actions use action-json."),
    ])];

    match app.connection {
        ConnectionState::Connected => lines.push(Line::from(vec![
            Span::styled("Backend: ", theme::label_style()),
            theme::badge("OK", theme::success_style()),
        ])),
        ConnectionState::Disconnected => lines.push(Line::from(vec![
            Span::styled("Backend: ", theme::label_style()),
            theme::badge("ERROR", theme::error_style()),
            Span::raw(format!(" {}", app.backend_endpoint)),
        ])),
    }

    if let Some(error) = &app.last_error {
        let when = app
            .last_error_label(now)
            .unwrap_or_else(|| "unknown time".to_string());
        lines.push(Line::from(vec![
            Span::styled(format!("Last error ({when}): "), theme::label_style()),
            Span::raw(truncate(error, 90)),
        ]));
    } else {
        lines.push(Line::from(vec![
            Span::styled("Last error: ", theme::label_style()),
            Span::styled("none", theme::secondary_style()),
        ]));
    }

    frame.render_widget(
        Paragraph::new(lines)
            .block(theme::panel_block("Backend / Errors"))
            .wrap(Wrap { trim: true }),
        area,
    );
}

fn render_logs(frame: &mut Frame<'_>, area: Rect, app: &App) {
    let visible_rows = area.height.saturating_sub(2) as usize;
    let mut visible_count = 0usize;
    let items = if app.logs.is_empty() {
        vec![ListItem::new(Line::from(Span::styled(
            "Waiting for logs...",
            theme::secondary_style(),
        )))]
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
                        Span::styled(timestamp.as_str(), theme::dim_style()),
                        Span::raw("  "),
                        Span::styled(message.to_string(), theme::primary_style()),
                    ])),
                    None => ListItem::new(Line::from(Span::styled(
                        message.to_string(),
                        theme::primary_style(),
                    ))),
                }
            })
            .collect()
    };

    let title = if app.logs.is_empty() {
        "Recent Logs".to_string()
    } else {
        format!(
            "Recent Logs ({}/{}, offset {})",
            visible_count,
            app.logs.len(),
            app.log_scroll
        )
    };

    let block = Block::default()
        .borders(Borders::ALL)
        .border_type(ratatui::widgets::BorderType::Rounded)
        .border_style(Style::default().fg(theme::BORDER_MUTED))
        .style(
            Style::default()
                .fg(theme::TEXT_PRIMARY)
                .bg(theme::PANEL_SURFACE),
        )
        .title(Span::styled(title, theme::title_style()));

    frame.render_widget(List::new(items).block(block), area);
}

fn render_footer(frame: &mut Frame<'_>, area: Rect) {
    frame.render_widget(
        Paragraph::new(shortcut_line(area.width)).style(Style::default().fg(theme::TEXT_SECONDARY)),
        area,
    );
}

fn render_help(frame: &mut Frame<'_>, area: Rect) {
    let popup = centered_rect(62, 62, area);
    let lines = vec![
        Line::from(Span::styled("Controls", theme::title_style())),
        Line::from(""),
        help_line("0", "Help"),
        help_line("H", "Help alias on English layout"),
        help_line("F5", "Refresh"),
        Line::from(""),
        help_line("1", "Restart Core Apps"),
        help_line("2", "Start OSCGoesBrrr"),
        help_line("3", "Base Stations On"),
        help_line("4", "Base Stations Off"),
        help_line("5", "Restart OSC Router"),
        help_line("6", "Reload Autostart Apps"),
        Line::from(""),
        help_line("ENTER", "Confirm modal action"),
        help_line("ESC", "Cancel / close modal"),
        help_line("Q", "Quit TUI on dashboard, cancel modal, close Help"),
        help_line("ARROWS", "Scroll logs"),
        help_line("PG/HOME", "Page or jump log scroll"),
        Line::from(""),
        Line::from(Span::styled(
            "While Help is open, any key closes Help only.",
            theme::warning_style(),
        )),
        Line::from("All actions require confirmation and use backend action-json."),
        Line::from("Q never stops the supervisor backend."),
        Line::from("F1, ?, and Russian help aliases are not mapped."),
        Line::from("force-stop-supervisor is not exposed."),
    ];

    frame.render_widget(Clear, popup);
    frame.render_widget(
        Paragraph::new(lines)
            .block(theme::accent_panel_block("Help"))
            .wrap(Wrap { trim: true }),
        popup,
    );
}

fn render_action_confirmation(frame: &mut Frame<'_>, area: Rect, app: &App) {
    let Some(action) = app.confirmation else {
        return;
    };

    let popup = centered_rect(62, 46, area);
    let safety_category = app
        .action_metadata(action)
        .map(|command| command.action_safety_category.as_str())
        .unwrap_or("-");
    let lines = vec![
        Line::from(Span::styled("Confirm Action", theme::title_style())),
        Line::from(""),
        labeled_line("Action", action.display_name()),
        labeled_line("Command", action.command_name()),
        Line::from(vec![
            Span::styled("Safety: ", theme::label_style()),
            safety_badge(safety_category),
        ]),
        Line::from(""),
        Line::from(vec![
            Span::styled("Effect: ", theme::label_style()),
            Span::raw(action.expected_effect()),
        ]),
        Line::from(Span::styled(action.warning(), theme::warning_style())),
        Line::from("This action will be sent to the supervisor backend."),
        Line::from(""),
        Line::from(vec![
            Span::styled("ENTER Confirm", theme::success_style()),
            Span::raw("    "),
            Span::styled("ESC Cancel", theme::warning_style()),
        ]),
        Line::from("Y also confirms, N/Q cancel."),
    ];

    frame.render_widget(Clear, popup);
    frame.render_widget(
        Paragraph::new(lines)
            .block(theme::accent_panel_block(action.display_name()))
            .wrap(Wrap { trim: true }),
        popup,
    );
}

fn action_card_line(app: &App, action: TuiAction, now: Instant) -> Line<'static> {
    let state = action_state(app, action, now);
    Line::from(vec![
        Span::styled(
            format!("{} {:<9}", action.digit(), action.short_label()),
            theme::title_style(),
        ),
        theme::badge(state.label, state.style),
        Span::raw(
            state
                .detail
                .map(|detail| format!(" {detail}"))
                .unwrap_or_default(),
        ),
    ])
}

struct ActionState {
    label: &'static str,
    detail: Option<String>,
    color: Color,
    style: Style,
}

fn action_state(app: &App, action: TuiAction, now: Instant) -> ActionState {
    if let Some(running) = app
        .running_actions
        .iter()
        .find(|running| running.command.eq_ignore_ascii_case(action.command_name()))
    {
        return ActionState {
            label: "RUNNING",
            detail: Some(format_duration(now.duration_since(running.started_at))),
            color: theme::WARNING_ORANGE,
            style: theme::warning_style(),
        };
    }

    if matches!(
        action,
        TuiAction::BaseStationsOn | TuiAction::BaseStationsOff
    ) && app.running_actions.iter().any(|running| {
        matches!(
            running.action,
            TuiAction::BaseStationsOn | TuiAction::BaseStationsOff
        )
    }) {
        return ActionState {
            label: "BLOCKED",
            detail: Some("base-station action running".to_string()),
            color: theme::WARNING_ORANGE,
            style: theme::warning_style(),
        };
    }

    let metadata = app.action_metadata(action);
    if metadata.map(command_is_blocked).unwrap_or(false) {
        return ActionState {
            label: "BLOCKED",
            detail: metadata
                .map(|command| command.blocked_reason.clone())
                .filter(|reason| !reason.trim().is_empty()),
            color: theme::ERROR_RED,
            style: theme::error_style(),
        };
    }

    if !metadata.map(action_is_executable).unwrap_or(false) {
        return ActionState {
            label: "UNAVAILABLE",
            detail: metadata
                .map(|command| command.blocked_reason.clone())
                .filter(|reason| !reason.trim().is_empty()),
            color: theme::TEXT_DIM,
            style: theme::secondary_style(),
        };
    }

    ActionState {
        label: "READY",
        detail: None,
        color: theme::ACCENT_GREEN,
        style: theme::success_style(),
    }
}

fn shortcut_line(width: u16) -> &'static str {
    if width >= 100 {
        "0 Help  F5 Refresh  1 Core  2 OGB  3 BS On  4 BS Off  5 OSC  6 Autostart  Q Quit TUI"
    } else {
        "0 Help  F5 Refresh  1-6 Actions  Q Quit TUI"
    }
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

fn labeled_line<'a>(label: &'a str, value: &'a str) -> Line<'a> {
    Line::from(vec![
        Span::styled(format!("{label}: "), theme::label_style()),
        Span::raw(value),
    ])
}

fn status_line<'a>(label: &'a str, value: &'a str, badge: Span<'static>) -> Line<'a> {
    Line::from(vec![
        Span::styled(format!("{label:<13}"), theme::label_style()),
        badge,
        Span::raw(" "),
        Span::raw(value),
    ])
}

fn help_line<'a>(key: &'static str, value: &'a str) -> Line<'a> {
    Line::from(vec![
        Span::styled(format!("{key:<7}"), theme::success_style()),
        Span::raw(value),
    ])
}

fn status_badge(kind: &str, value: &str) -> Span<'static> {
    let lower = value.to_lowercase();
    match kind {
        "steamvr" if lower.contains("running") => theme::badge("OK", theme::success_style()),
        "steamvr" => theme::badge("OFF", theme::warning_style()),
        "core" if lower.contains("running") => theme::badge("OK", theme::success_style()),
        "core" if lower.contains("incomplete") => theme::badge("WARN", theme::warning_style()),
        "core" => theme::badge("OFF", theme::warning_style()),
        "osc" if lower.contains("running") => theme::badge("OK", theme::success_style()),
        "osc" => theme::badge("STOPPED", theme::warning_style()),
        "ogb" if lower.contains("running") => theme::badge("OK", theme::success_style()),
        "ogb" if lower.contains("disabled") => theme::badge("DISABLED", theme::warning_style()),
        "ogb" => theme::badge("WARN", theme::warning_style()),
        "base" if lower.contains("disabled") => theme::badge("OFF", theme::warning_style()),
        "base" if lower.contains("powered=true") => theme::badge("OK", theme::success_style()),
        "base" if lower.contains("powered=false") => theme::badge("OFF", theme::warning_style()),
        "base" => theme::badge("UNKNOWN", theme::warning_style()),
        "lifecycle" if lower.contains("running") => theme::badge("RUNNING", theme::success_style()),
        "lifecycle" => theme::badge("READY", theme::info_style()),
        _ => theme::badge("INFO", theme::info_style()),
    }
}

fn safety_badge(value: &str) -> Span<'static> {
    if value.eq_ignore_ascii_case("LowRisk") {
        theme::badge("LowRisk", theme::info_style())
    } else if value.eq_ignore_ascii_case("Disruptive") {
        theme::badge("Disruptive", theme::warning_style())
    } else if value.eq_ignore_ascii_case("Blocked") {
        theme::badge("Blocked", theme::error_style())
    } else {
        theme::badge(value.to_string(), theme::secondary_style())
    }
}

fn action_is_executable(command: &CommandSummary) -> bool {
    command.action_supported
        && command.tui_executable
        && !command_is_blocked(command)
        && !command
            .action_safety_category
            .eq_ignore_ascii_case("Dangerous")
}

fn command_is_blocked(command: &CommandSummary) -> bool {
    command
        .action_safety_category
        .eq_ignore_ascii_case("Blocked")
}

fn action_outcome_style(outcome: ActionOutcome) -> (&'static str, Style) {
    match outcome {
        ActionOutcome::Succeeded => ("OK", theme::success_style()),
        ActionOutcome::Failed => ("ERROR", theme::error_style()),
        ActionOutcome::Cancelled => ("CANCELLED", theme::warning_style()),
        ActionOutcome::Rejected => ("BLOCKED", theme::warning_style()),
    }
}

fn truncate(value: &str, max: usize) -> String {
    let mut chars = value.chars();
    let truncated = chars.by_ref().take(max).collect::<String>();
    if chars.next().is_some() {
        format!("{truncated}...")
    } else {
        truncated
    }
}

fn format_duration(duration: std::time::Duration) -> String {
    let seconds = duration.as_secs();
    if seconds < 60 {
        format!("{seconds}s")
    } else {
        format!("{}m{}s", seconds / 60, seconds % 60)
    }
}
