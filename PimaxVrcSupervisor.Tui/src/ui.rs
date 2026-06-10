use std::{borrow::Cow, time::Instant};

use ratatui::{
    Frame,
    layout::{Alignment, Constraint, Direction, Layout, Rect},
    style::{Color, Style},
    text::{Line, Span},
    widgets::{Block, Borders, Clear, List, ListItem, Paragraph, Wrap},
};

use crate::{
    app::{ActionOutcome, App, ClickAction, ConnectionState, REFRESH_INTERVAL},
    models::{CommandSummary, TuiAction},
    theme,
};

const FULL_MIN_WIDTH: u16 = 120;
const FULL_MIN_HEIGHT: u16 = 32;
const COMPACT_MIN_WIDTH: u16 = 100;
const COMPACT_MIN_HEIGHT: u16 = 26;
const SMALL_MIN_WIDTH: u16 = 80;
const SMALL_MIN_HEIGHT: u16 = 20;

pub fn render(frame: &mut Frame<'_>, app: &mut App) {
    app.clear_click_regions();

    let area = frame.area();
    frame.render_widget(Block::default().style(theme::app_style()), area);

    let now = Instant::now();
    if area.width >= FULL_MIN_WIDTH && area.height >= FULL_MIN_HEIGHT {
        render_full_dashboard(frame, area, app, now);
    } else if area.width >= COMPACT_MIN_WIDTH && area.height >= COMPACT_MIN_HEIGHT {
        render_compact_dashboard(frame, area, app, now);
    } else if area.width >= SMALL_MIN_WIDTH && area.height >= SMALL_MIN_HEIGHT {
        render_small_dashboard(frame, area, app, now);
    } else {
        render_tiny_fallback(frame, area, app);
        return;
    }

    if app.help_visible {
        render_help(frame, area);
    }

    if app.confirmation.is_some() {
        render_action_confirmation(frame, area, app);
    }
}

fn render_full_dashboard(frame: &mut Frame<'_>, area: Rect, app: &mut App, now: Instant) {
    let root = Layout::default()
        .direction(Direction::Vertical)
        .constraints([
            Constraint::Length(3),
            Constraint::Length(12),
            Constraint::Length(6),
            Constraint::Min(8),
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
    render_system(frame, middle[1], app, now);

    render_logs(frame, root[3], app);
    render_footer(frame, root[4], app);
}

fn render_compact_dashboard(frame: &mut Frame<'_>, area: Rect, app: &mut App, now: Instant) {
    let root = Layout::default()
        .direction(Direction::Vertical)
        .constraints([
            Constraint::Length(3),
            Constraint::Length(11),
            Constraint::Length(4),
            Constraint::Min(7),
            Constraint::Length(1),
        ])
        .split(area);

    render_header(frame, root[0], app, now);

    let main = Layout::default()
        .direction(Direction::Horizontal)
        .constraints([Constraint::Percentage(44), Constraint::Percentage(56)])
        .split(root[1]);

    render_compact_status(frame, main[0], app);
    render_compact_actions(frame, main[1], app, now);
    render_compact_action_activity(frame, root[2], app, now);
    render_logs(frame, root[3], app);
    render_footer(frame, root[4], app);
}

fn render_small_dashboard(frame: &mut Frame<'_>, area: Rect, app: &mut App, now: Instant) {
    let root = Layout::default()
        .direction(Direction::Vertical)
        .constraints([
            Constraint::Length(3),
            Constraint::Length(4),
            Constraint::Length(5),
            Constraint::Length(3),
            Constraint::Min(4),
            Constraint::Length(1),
        ])
        .split(area);

    render_small_header(frame, root[0], app);
    render_small_status(frame, root[1], app);
    render_small_actions(frame, root[2], app, now);
    render_small_activity(frame, root[3], app, now);
    render_small_log(frame, root[4], app);
    render_footer(frame, root[5], app);
}

fn render_tiny_fallback(frame: &mut Frame<'_>, area: Rect, app: &mut App) {
    let lines = vec![
        Line::from(Span::styled(
            "Pimax VRC Supervisor TUI",
            theme::title_style(),
        )),
        Line::from("Terminal too small for dashboard."),
        Line::from(format!(
            "Resize to at least {SMALL_MIN_WIDTH}x{SMALL_MIN_HEIGHT} for compact view.",
        )),
        Line::from(format!(
            "Recommended: {FULL_MIN_WIDTH}x{FULL_MIN_HEIGHT}. Current: {}x{}.",
            area.width, area.height
        )),
        Line::from(""),
        Line::from(vec![
            Span::styled("0 Help", theme::success_style()),
            Span::raw("   "),
            Span::styled("Q Quit TUI", theme::warning_style()),
        ]),
    ];

    let regions = Layout::default()
        .direction(Direction::Horizontal)
        .constraints([Constraint::Percentage(50), Constraint::Percentage(50)])
        .split(area);
    app.add_click_region(regions[0], ClickAction::OpenHelp);
    app.add_click_region(regions[1], ClickAction::QuitTui);

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
        ConnectionState::Connected => ("OK", theme::badge_success_style()),
        ConnectionState::Disconnected => ("BACKEND OFF", theme::badge_error_style()),
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
            theme::success_style(),
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

    frame.render_widget(
        Paragraph::new(vec![
            Line::from(line),
            Line::from(shortcut_line(area.width)),
        ])
        .block(theme::accent_panel_block("Dashboard"))
        .wrap(Wrap { trim: true }),
        area,
    );
}

fn render_status(frame: &mut Frame<'_>, area: Rect, app: &App) {
    let status = &app.status;
    let lines = vec![
        simple_status_line("Version", status.app_version.as_str()),
        simple_status_line("Mode", status.mode.as_str()),
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
        core_apps_status_line(app),
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

fn render_compact_status(frame: &mut Frame<'_>, area: Rect, app: &App) {
    let status = &app.status;
    let lines = vec![
        simple_status_line("Mode", status.mode.as_str()),
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
        core_apps_status_line(app),
        status_line(
            "OSC",
            status.osc_router.as_str(),
            status_badge("osc", &status.osc_router),
        ),
        status_line(
            "Base",
            status.base_stations.as_str(),
            status_badge("base", &status.base_stations),
        ),
    ];

    frame.render_widget(
        Paragraph::new(lines)
            .block(theme::panel_block("Status"))
            .wrap(Wrap { trim: true }),
        area,
    );
}

fn render_compact_actions(frame: &mut Frame<'_>, area: Rect, app: &mut App, now: Instant) {
    let block = theme::panel_block("Actions");
    let inner = block.inner(area);
    frame.render_widget(block, area);

    let mut lines = Vec::new();
    for action in TuiAction::ALL {
        if lines.len() >= inner.height as usize {
            break;
        }

        let row = inner.y.saturating_add(lines.len() as u16);
        app.add_click_region(
            Rect::new(inner.x, row, inner.width, 1),
            ClickAction::SelectAction(action),
        );

        lines.push(compact_action_line(
            app,
            action,
            now,
            inner.width.saturating_sub(1),
        ));
    }

    frame.render_widget(Paragraph::new(lines).wrap(Wrap { trim: true }), inner);
}

fn render_compact_action_activity(frame: &mut Frame<'_>, area: Rect, app: &App, now: Instant) {
    let mut lines = Vec::new();

    if app.running_actions.is_empty() {
        lines.push(Line::from(vec![
            Span::styled("Running ", theme::label_style()),
            Span::styled("none", foreground(theme::TEXT_SECONDARY)),
        ]));
    } else {
        let running = app.running_actions.first().expect("running action exists");
        lines.push(Line::from(vec![
            Span::styled("Running ", theme::label_style()),
            Span::styled(
                format!(
                    "{} {}",
                    running.action.short_label(),
                    format_duration(now.duration_since(running.started_at))
                ),
                theme::badge_success_style(),
            ),
        ]));
    }

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
            .unwrap_or("");
        lines.push(Line::from(vec![
            Span::styled("Last ", theme::label_style()),
            Span::styled(status, style),
            Span::styled(format!(" {when} "), foreground(theme::TEXT_SECONDARY)),
            Span::styled(command.to_string(), foreground(theme::TEXT_PRIMARY)),
            Span::raw(" - "),
            Span::raw(truncate(message, 80)),
        ]));
    } else if let Some(error) = app.last_error.as_deref() {
        lines.push(Line::from(vec![
            Span::styled("Backend ", theme::label_style()),
            Span::styled("ERROR", theme::badge_error_style()),
            Span::raw(" "),
            Span::raw(truncate(error, 96)),
        ]));
    } else {
        let backend = match app.connection {
            ConnectionState::Connected => ("OK", theme::badge_success_style()),
            ConnectionState::Disconnected => ("BACKEND OFF", theme::badge_error_style()),
        };
        lines.push(Line::from(vec![
            Span::styled("Backend ", theme::label_style()),
            Span::styled(backend.0, backend.1),
            Span::styled(
                format!(" {}", app.backend_endpoint),
                foreground(theme::TEXT_SECONDARY),
            ),
        ]));
    }

    frame.render_widget(
        Paragraph::new(lines)
            .block(theme::panel_block("Activity"))
            .wrap(Wrap { trim: true }),
        area,
    );
}

fn render_small_header(frame: &mut Frame<'_>, area: Rect, app: &App) {
    let (backend_label, backend_style) = match app.connection {
        ConnectionState::Connected => ("OK", theme::badge_success_style()),
        ConnectionState::Disconnected => ("BACKEND OFF", theme::badge_error_style()),
    };

    let line = Line::from(vec![
        Span::styled("Pimax VRC Supervisor TUI", theme::title_style()),
        Span::raw("   Backend "),
        theme::badge(backend_label, backend_style),
    ]);

    frame.render_widget(
        Paragraph::new(vec![
            line,
            Line::from("0 Help  F5 Refresh  1-6 Actions  Q Quit TUI"),
        ])
        .block(theme::accent_panel_block("Dashboard"))
        .wrap(Wrap { trim: true }),
        area,
    );
}

fn render_small_status(frame: &mut Frame<'_>, area: Rect, app: &App) {
    let status = &app.status;
    let lines = vec![
        small_status_line(
            "Life",
            status.lifecycle.as_str(),
            status_badge("lifecycle", &status.lifecycle),
        ),
        Line::from(vec![
            Span::styled("Core ", theme::label_style()),
            core_apps_badge(app),
            Span::raw("  "),
            Span::styled("OSC ", theme::label_style()),
            status_badge("osc", &status.osc_router),
            Span::raw("  "),
            Span::styled("Base ", theme::label_style()),
            status_badge("base", &status.base_stations),
        ]),
    ];

    frame.render_widget(
        Paragraph::new(lines)
            .block(theme::panel_block("Essential Status"))
            .wrap(Wrap { trim: true }),
        area,
    );
}

fn render_small_actions(frame: &mut Frame<'_>, area: Rect, app: &mut App, now: Instant) {
    let block = theme::panel_block("Actions");
    let inner = block.inner(area);
    frame.render_widget(block, area);

    for row_index in 0..2 {
        let row_y = inner.y.saturating_add(row_index as u16);
        if row_y >= inner.y.saturating_add(inner.height) {
            continue;
        }

        for column_index in 0..3 {
            let action_index = row_index * 3 + column_index;
            let Some(action) = TuiAction::ALL.get(action_index).copied() else {
                continue;
            };

            let column_width = inner.width / 3;
            let x = inner
                .x
                .saturating_add(column_width.saturating_mul(column_index as u16));
            let width = if column_index == 2 {
                inner.x.saturating_add(inner.width).saturating_sub(x)
            } else {
                column_width
            };

            let cell = Rect::new(x, row_y, width, 1);
            app.add_click_region(cell, ClickAction::SelectAction(action));
            frame.render_widget(
                Paragraph::new(small_action_cell_line(
                    app,
                    action,
                    now,
                    width.saturating_sub(1),
                )),
                cell,
            );
        }
    }
}

fn render_small_activity(frame: &mut Frame<'_>, area: Rect, app: &App, now: Instant) {
    let mut lines = Vec::new();
    if let Some(running) = app.running_actions.first() {
        lines.push(Line::from(vec![
            Span::styled("Running: ", theme::label_style()),
            Span::styled(running.command.clone(), foreground(theme::TEXT_PRIMARY)),
            Span::raw(" "),
            Span::styled(
                format!(
                    "{} ago",
                    format_duration(now.duration_since(running.started_at))
                ),
                foreground(theme::TEXT_SECONDARY),
            ),
        ]));
    } else {
        lines.push(Line::from(vec![
            Span::styled("Running: ", theme::label_style()),
            Span::styled("none", foreground(theme::TEXT_SECONDARY)),
        ]));
    }

    lines.push(last_action_line(app, now, 72));

    frame.render_widget(
        Paragraph::new(lines)
            .block(theme::panel_block("Activity"))
            .wrap(Wrap { trim: true }),
        area,
    );
}

fn render_small_log(frame: &mut Frame<'_>, area: Rect, app: &App) {
    let text = app
        .logs
        .last()
        .map(log_message)
        .unwrap_or("Waiting for logs...");

    frame.render_widget(
        Paragraph::new(Line::from(vec![
            Span::styled("Last log: ", theme::label_style()),
            Span::styled(
                truncate(text, area.width.saturating_sub(14) as usize),
                foreground(theme::TEXT_PRIMARY),
            ),
        ]))
        .block(theme::panel_block("Logs"))
        .wrap(Wrap { trim: true }),
        area,
    );
}

fn render_actions(frame: &mut Frame<'_>, area: Rect, app: &mut App, now: Instant) {
    let block = theme::panel_block("Actions");
    let inner = block.inner(area);
    frame.render_widget(block, area);

    if inner.height < 6 || inner.width < 48 {
        let lines = TuiAction::ALL
            .iter()
            .map(|action| action_card_line(app, *action, now, inner.width.saturating_sub(1)))
            .collect::<Vec<_>>();
        for (index, action) in TuiAction::ALL.iter().copied().enumerate() {
            let row = inner.y.saturating_add(index as u16);
            if row < inner.y.saturating_add(inner.height) {
                app.add_click_region(
                    Rect::new(inner.x, row, inner.width, 1),
                    ClickAction::SelectAction(action),
                );
            }
        }
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
    app: &mut App,
    action: TuiAction,
    now: Instant,
) {
    let state = action_state(app, action, now);
    app.add_click_region(area, ClickAction::SelectAction(action));

    let block = Block::default()
        .borders(Borders::ALL)
        .border_type(ratatui::widgets::BorderType::Rounded)
        .border_style(Style::default().fg(state.border_color))
        .style(
            Style::default()
                .fg(theme::TEXT_PRIMARY)
                .bg(theme::PANEL_ELEVATED),
        );

    let inner_width = area.width.saturating_sub(2);
    let mut lines = vec![
        action_card_line(app, action, now, inner_width),
        Line::from(Span::styled(
            action.display_name(),
            foreground(theme::TEXT_PRIMARY),
        )),
    ];

    if let Some(detail) = state.detail {
        lines.push(Line::from(Span::styled(
            detail,
            foreground(theme::TEXT_SECONDARY),
        )));
    } else if state.label == "START" {
        lines.push(Line::from(Span::styled(
            format!("click or press {}", action.digit()),
            foreground(theme::TEXT_DIM),
        )));
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
    let mut lines = vec![Line::from(Span::styled("Running", theme::title_style()))];

    if app.running_actions.is_empty() {
        lines.push(Line::from(Span::styled(
            "No running actions.",
            theme::secondary_style(),
        )));
    } else {
        for running in app.running_actions.iter().take(3) {
            lines.push(Line::from(vec![
                Span::styled(running.command.clone(), theme::primary_style()),
                Span::raw("  "),
                Span::styled(
                    format!(
                        "RUNNING {}",
                        format_duration(now.duration_since(running.started_at))
                    ),
                    theme::success_style(),
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
        lines.push(Line::from(vec![
            Span::styled(command.to_string(), theme::primary_style()),
            Span::raw("  "),
            Span::styled(status, style),
            Span::styled(format!(" {when}"), theme::secondary_style()),
        ]));

        if let Some(message) = app
            .last_action_result
            .as_deref()
            .or(app.last_action_error.as_deref())
        {
            lines.push(Line::from(Span::raw(truncate(message, 92))));
        }
    } else if let Some(result) = app.last_action_result.as_deref() {
        let command = app.last_action_command.as_deref().unwrap_or("action");
        lines.push(Line::from(vec![
            Span::styled(command.to_string(), theme::warning_style()),
            Span::raw("  "),
            Span::raw(truncate(result, 92)),
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

fn render_system(frame: &mut Frame<'_>, area: Rect, app: &App, now: Instant) {
    let mut lines = Vec::new();
    match app.connection {
        ConnectionState::Connected => lines.push(Line::from(vec![
            Span::styled(format!("{:<8}", "Backend"), theme::label_style()),
            theme::badge("OK", theme::badge_success_style()),
        ])),
        ConnectionState::Disconnected => lines.push(Line::from(vec![
            Span::styled(format!("{:<8}", "Backend"), theme::label_style()),
            theme::badge("BACKEND OFF", theme::badge_error_style()),
            Span::raw(format!(" {}", app.backend_endpoint)),
        ])),
    }

    if let Some(error) = &app.last_error {
        let when = app
            .last_error_label(now)
            .unwrap_or_else(|| "unknown time".to_string());
        lines.push(Line::from(vec![
            Span::styled(format!("{:<8}", "Errors"), theme::label_style()),
            theme::badge("ERROR", theme::badge_error_style()),
            Span::raw(format!(" {when}: ")),
            Span::raw(truncate(error, 84)),
        ]));
    } else {
        lines.push(Line::from(vec![
            Span::styled(format!("{:<8}", "Errors"), theme::label_style()),
            Span::styled("none", theme::secondary_style()),
        ]));
    }

    if let Some(notice) = &app.mouse_notice {
        lines.push(Line::from(vec![
            Span::styled(format!("{:<8}", "Mouse"), theme::label_style()),
            Span::styled(truncate(notice, 84), theme::warning_style()),
        ]));
    } else {
        lines.push(Line::from(vec![
            Span::styled(format!("{:<8}", "Mouse"), theme::label_style()),
            Span::styled(
                if app.mouse_enabled {
                    "enabled"
                } else {
                    "keyboard only"
                },
                theme::secondary_style(),
            ),
        ]));
    }

    frame.render_widget(
        Paragraph::new(lines)
            .block(theme::panel_block("System"))
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
        let end = app.logs.len().saturating_sub(app.log_scroll);
        let start = end.saturating_sub(visible_rows);
        app.logs
            .iter()
            .skip(start)
            .take(end.saturating_sub(start))
            .inspect(|_| visible_count += 1)
            .map(|line| {
                let message = if line.message == "-" {
                    line.raw.as_str()
                } else {
                    line.message.as_str()
                };

                match &line.timestamp {
                    Some(timestamp) => ListItem::new(Line::from(vec![
                        Span::styled(format!("{timestamp:<8}"), theme::dim_style()),
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

    let compact_title = area.width < FULL_MIN_WIDTH;
    let title = if app.logs.is_empty() {
        "Recent Logs (live) - Waiting for logs...".to_string()
    } else if compact_title && app.log_follow {
        "Logs live - Wheel/Up pause, End/F follow".to_string()
    } else if compact_title {
        format!(
            "Logs paused offset {} - End/F follow, Wheel",
            app.log_scroll
        )
    } else if app.log_follow {
        format!(
            "Recent Logs ({}/{}, live) - Up/PgUp pause, Wheel scroll",
            visible_count,
            app.logs.len()
        )
    } else {
        format!(
            "Recent Logs ({}/{}, paused, offset {}) - End/F follow, Wheel scroll",
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

fn render_footer(frame: &mut Frame<'_>, area: Rect, app: &mut App) {
    register_footer_clicks(app, area);
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
        help_line("1-6", "Open confirmation from keyboard"),
        help_line("MOUSE", "Click action card to start immediately"),
        help_line("ENTER", "Confirm modal action"),
        help_line("SPACE", "Confirm modal action"),
        help_line("ESC", "Cancel modal"),
        help_line("Q", "Quit TUI on dashboard only"),
        help_line("UP/PGUP", "Scroll logs older, pauses live follow"),
        help_line("DOWN/PGDN", "Scroll logs newer"),
        help_line("WHEEL", "Scroll logs older/newer"),
        help_line("END/F", "Resume latest log follow"),
        Line::from(""),
        Line::from(Span::styled(
            "While Help is open, any key or mouse click closes Help only.",
            theme::warning_style(),
        )),
        Line::from("Mouse actions use the same allowed action list and conflict checks."),
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

fn render_action_confirmation(frame: &mut Frame<'_>, area: Rect, app: &mut App) {
    let Some(action) = app.confirmation else {
        return;
    };

    let popup = centered_rect(62, 42, area);
    let lines = vec![
        Line::from(Span::styled("Confirm Action", theme::title_style())),
        Line::from(""),
        Line::from(Span::styled(action.display_name(), theme::title_style())),
        labeled_line("Command", action.command_name()),
        Line::from(""),
        Line::from(action.expected_effect()),
        Line::from("This will send the request to the supervisor backend."),
        Line::from(""),
        Line::from(vec![
            Span::styled("ENTER / SPACE Confirm", theme::secondary_style()),
            Span::raw("    "),
            Span::styled("ESC Cancel", theme::secondary_style()),
        ]),
    ];

    frame.render_widget(Clear, popup);
    register_modal_clicks(app, popup);
    frame.render_widget(
        Paragraph::new(lines)
            .block(theme::accent_panel_block(action.display_name()))
            .wrap(Wrap { trim: true }),
        popup,
    );
}

fn action_card_line(app: &App, action: TuiAction, now: Instant, width: u16) -> Line<'static> {
    let state = action_state(app, action, now);
    let left = format!("{} {}", action.digit(), action.short_label());
    aligned_line(
        &left,
        action_state_text(&state).as_ref(),
        width as usize,
        state.style,
    )
}

fn compact_action_line(app: &App, action: TuiAction, now: Instant, width: u16) -> Line<'static> {
    let state = action_state(app, action, now);
    const LABEL_WIDTH: usize = 11;
    const BADGE_WIDTH: usize = 13;

    let label = format!("{} {}", action.digit(), compact_action_label(action));
    let display_name = action.display_name();
    let reserved_width = LABEL_WIDTH + BADGE_WIDTH + 1;
    let display_width = (width as usize).saturating_sub(reserved_width);
    let display_name = truncate(display_name, display_width);

    Line::from(vec![
        Span::styled(format!("{label:<LABEL_WIDTH$}"), theme::title_style()),
        action_state_span(&state),
        Span::raw(
            " ".repeat(BADGE_WIDTH.saturating_sub(action_state_text(&state).chars().count()) + 1),
        ),
        Span::styled(display_name, foreground(theme::TEXT_SECONDARY)),
    ])
}

fn small_action_cell_line(app: &App, action: TuiAction, now: Instant, width: u16) -> Line<'static> {
    let state = action_state(app, action, now);
    let left = format!("{} {:<4} ", action.digit(), small_action_label(action));
    let state_text = action_state_text(&state);
    let used_width = left.chars().count() + state_text.chars().count();
    let padding = (width as usize).saturating_sub(used_width).max(1);

    Line::from(vec![
        Span::styled(left, theme::title_style()),
        action_state_span(&state),
        Span::raw(" ".repeat(padding)),
    ])
}

fn small_action_label(action: TuiAction) -> &'static str {
    match action {
        TuiAction::RestartCoreApps => "Core",
        TuiAction::StartOscGoesBrrr => "OGB",
        TuiAction::BaseStationsOn => "On",
        TuiAction::BaseStationsOff => "Off",
        TuiAction::RestartOscRouter => "OSC",
        TuiAction::ReloadAutostartApps => "Auto",
    }
}

fn compact_action_label(action: TuiAction) -> &'static str {
    match action {
        TuiAction::ReloadAutostartApps => "Auto",
        _ => action.short_label(),
    }
}

fn last_action_line(app: &App, now: Instant, max_message: usize) -> Line<'static> {
    if let Some(outcome) = app.last_action_outcome {
        let command = app.last_action_command.as_deref().unwrap_or("action");
        let when = app
            .last_action_completed_label(now)
            .unwrap_or_else(|| "unknown".to_string());
        let (status, style) = action_outcome_style(outcome);
        let message = app
            .last_action_result
            .as_deref()
            .or(app.last_action_error.as_deref())
            .unwrap_or("");

        return Line::from(vec![
            Span::styled("Last: ", theme::label_style()),
            Span::styled(command.to_string(), foreground(theme::TEXT_PRIMARY)),
            Span::raw(" "),
            Span::styled(status, style),
            Span::styled(format!(" {when} "), foreground(theme::TEXT_SECONDARY)),
            Span::raw(truncate(message, max_message)),
        ]);
    }

    Line::from(vec![
        Span::styled("Last: ", theme::label_style()),
        Span::styled("none", foreground(theme::TEXT_SECONDARY)),
    ])
}

fn log_message(line: &crate::models::LogLine) -> &str {
    if line.message == "-" {
        line.raw.as_str()
    } else {
        line.message.as_str()
    }
}

#[derive(Debug)]
struct ActionState {
    label: &'static str,
    detail: Option<String>,
    border_color: Color,
    style: Style,
}

impl ActionState {
    fn label_text(&self) -> Cow<'static, str> {
        Cow::Borrowed(self.label)
    }
}

fn action_state_text(state: &ActionState) -> Cow<'static, str> {
    if state.label == "START" {
        Cow::Owned(format!("[{}]", state.label))
    } else {
        state.label_text()
    }
}

fn action_state_span(state: &ActionState) -> Span<'static> {
    if state.label == "START" {
        theme::action_button_badge(state.label, state.style)
    } else {
        Span::styled(state.label.to_string(), state.style)
    }
}

fn action_state(app: &App, action: TuiAction, now: Instant) -> ActionState {
    if app.connection == ConnectionState::Disconnected {
        return ActionState {
            label: "BACKEND OFF",
            detail: Some("backend unavailable".to_string()),
            border_color: theme::BORDER_MUTED,
            style: theme::badge_error_style(),
        };
    }

    if let Some(running) = app
        .running_actions
        .iter()
        .find(|running| running.command.eq_ignore_ascii_case(action.command_name()))
    {
        return ActionState {
            label: "RUNNING",
            detail: Some(format_duration(now.duration_since(running.started_at))),
            border_color: theme::ACCENT_GREEN,
            style: theme::badge_success_style(),
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
            border_color: theme::WARNING_ORANGE,
            style: theme::badge_warning_style(),
        };
    }

    let metadata = app.action_metadata(action);
    if metadata.map(command_is_blocked).unwrap_or(false) {
        return ActionState {
            label: "BLOCKED",
            detail: metadata
                .map(|command| command.blocked_reason.clone())
                .filter(|reason| !reason.trim().is_empty()),
            border_color: theme::WARNING_ORANGE,
            style: theme::badge_warning_style(),
        };
    }

    if !metadata.map(action_is_executable).unwrap_or(false) {
        return ActionState {
            label: "UNAVAILABLE",
            detail: metadata
                .map(|command| command.blocked_reason.clone())
                .filter(|reason| !reason.trim().is_empty()),
            border_color: theme::BORDER_MUTED,
            style: theme::badge_muted_style(),
        };
    }

    ActionState {
        label: "START",
        detail: None,
        border_color: theme::BORDER_MUTED,
        style: theme::badge_success_style(),
    }
}

fn register_footer_clicks(app: &mut App, area: Rect) {
    if area.width < 3 {
        return;
    }

    app.add_click_region(
        Rect::new(area.x, area.y, area.width.min(8), area.height),
        ClickAction::OpenHelp,
    );

    if area.width > 12 {
        app.add_click_region(
            Rect::new(area.x.saturating_add(9), area.y, 12, area.height),
            ClickAction::Refresh,
        );
    }

    if area.width > 16 {
        app.add_click_region(
            Rect::new(
                area.x.saturating_add(area.width.saturating_sub(12)),
                area.y,
                12,
                area.height,
            ),
            ClickAction::QuitTui,
        );
    }
}

fn register_modal_clicks(app: &mut App, popup: Rect) {
    let button_row = popup.y.saturating_add(popup.height.saturating_sub(4));
    let confirm = Rect::new(
        popup.x.saturating_add(2),
        button_row,
        popup.width.saturating_sub(4) / 2,
        2,
    );
    let cancel = Rect::new(
        popup.x.saturating_add(popup.width / 2),
        button_row,
        popup
            .width
            .saturating_sub(popup.width / 2)
            .saturating_sub(2),
        2,
    );
    app.add_click_region(confirm, ClickAction::ConfirmModal);
    app.add_click_region(cancel, ClickAction::CancelModal);
}

fn shortcut_line(width: u16) -> &'static str {
    if width >= 120 {
        "0 Help  F5 Refresh  Wheel Logs  End/F Follow  1 Core  2 OGB  3 On  4 Off  5 OSC  6 Auto  Q Quit TUI"
    } else if width >= 100 {
        "0 Help  F5 Refresh  1-6 Actions  End/F Logs  Q Quit TUI"
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

fn simple_status_line<'a>(label: &'a str, value: &'a str) -> Line<'a> {
    Line::from(vec![
        Span::styled(format!("{label:<14}"), theme::label_style()),
        Span::raw(format!("{:<9}", "")),
        Span::raw(value),
    ])
}

fn labeled_line<'a>(label: &'a str, value: &'a str) -> Line<'a> {
    Line::from(vec![
        Span::styled(format!("{label}: "), theme::label_style()),
        Span::raw(value),
    ])
}

fn status_line<'a>(label: &'a str, value: &'a str, badge: Span<'static>) -> Line<'a> {
    Line::from(vec![
        Span::styled(format!("{label:<14}"), theme::label_style()),
        badge,
        Span::raw(" "),
        Span::raw(value),
    ])
}

fn small_status_line<'a>(label: &'a str, value: &'a str, badge: Span<'static>) -> Line<'a> {
    Line::from(vec![
        Span::styled(format!("{label:<5}"), theme::label_style()),
        badge,
        Span::raw(" "),
        Span::raw(value),
    ])
}

fn core_apps_status_line(app: &App) -> Line<'_> {
    let lifecycle = app.status.lifecycle.to_lowercase();
    let core_apps = app.status.core_apps.to_lowercase();
    if lifecycle.contains("waiting-vrchat")
        && (core_apps.contains("incomplete")
            || core_apps.contains("not running")
            || core_apps.contains("waiting")
            || core_apps == "-")
    {
        return status_line(
            "Core Apps",
            "waiting for VRChat",
            fixed_badge("WAITING", theme::badge_info_style()),
        );
    }

    status_line(
        "Core Apps",
        app.status.core_apps.as_str(),
        status_badge("core", &app.status.core_apps),
    )
}

fn core_apps_badge(app: &App) -> Span<'static> {
    let lifecycle = app.status.lifecycle.to_lowercase();
    let core_apps = app.status.core_apps.to_lowercase();
    if lifecycle.contains("waiting-vrchat")
        && (core_apps.contains("incomplete")
            || core_apps.contains("not running")
            || core_apps.contains("waiting")
            || core_apps == "-")
    {
        fixed_badge("WAITING", theme::badge_info_style())
    } else {
        status_badge("core", &app.status.core_apps)
    }
}

fn help_line<'a>(key: &'static str, value: &'a str) -> Line<'a> {
    Line::from(vec![
        Span::styled(format!("{key:<7}"), theme::success_style()),
        Span::raw(value),
    ])
}

fn aligned_line(left: &str, right: &str, width: usize, right_style: Style) -> Line<'static> {
    let left_width = left.chars().count();
    let right_width = right.chars().count();
    let padding = width.saturating_sub(left_width + right_width).max(1);
    Line::from(vec![
        Span::styled(left.to_string(), theme::title_style()),
        Span::raw(" ".repeat(padding)),
        Span::styled(right.to_string(), right_style),
    ])
}

fn status_badge(kind: &str, value: &str) -> Span<'static> {
    let lower = value.to_lowercase();
    match kind {
        "steamvr" if lower.contains("running") => fixed_badge("OK", theme::badge_success_style()),
        "steamvr" => fixed_badge("OFF", theme::badge_warning_style()),
        "core" if lower.contains("running") => fixed_badge("OK", theme::badge_success_style()),
        "core" if lower.contains("incomplete") => fixed_badge("WARN", theme::badge_warning_style()),
        "core" => fixed_badge("OFF", theme::badge_warning_style()),
        "osc" if lower.contains("running") => fixed_badge("OK", theme::badge_success_style()),
        "osc" => fixed_badge("STOPPED", theme::badge_warning_style()),
        "ogb" if lower.contains("running") => fixed_badge("OK", theme::badge_success_style()),
        "ogb" if lower.contains("disabled") => fixed_badge("OFF", theme::badge_warning_style()),
        "ogb" => fixed_badge("WARN", theme::badge_warning_style()),
        "base" if lower.contains("disabled") => fixed_badge("OFF", theme::badge_warning_style()),
        "base" if lower.contains("powered=true") => fixed_badge("OK", theme::badge_success_style()),
        "base" if lower.contains("powered=false") => {
            fixed_badge("OFF", theme::badge_warning_style())
        }
        "base" => fixed_badge("UNKNOWN", theme::badge_warning_style()),
        "lifecycle" if lower.contains("running") => {
            fixed_badge("RUNNING", theme::badge_success_style())
        }
        "lifecycle" => fixed_badge("READY", theme::badge_info_style()),
        _ => fixed_badge("INFO", theme::badge_info_style()),
    }
}

fn fixed_badge(label: &str, style: Style) -> Span<'static> {
    Span::styled(label.to_string(), style)
}

fn foreground(color: Color) -> Style {
    Style::default().fg(color)
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
        ActionOutcome::Succeeded => ("OK", theme::badge_success_style()),
        ActionOutcome::Failed => ("ERROR", theme::badge_error_style()),
        ActionOutcome::Cancelled => ("CANCELLED", theme::badge_warning_style()),
        ActionOutcome::Rejected => ("BLOCKED", theme::badge_warning_style()),
        ActionOutcome::BackendOff => ("BACKEND OFF", theme::badge_error_style()),
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
