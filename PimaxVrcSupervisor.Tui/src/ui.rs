use std::time::{Duration, Instant};

use ratatui::{
    Frame,
    layout::{Alignment, Constraint, Direction, Layout, Rect},
    style::{Color, Style},
    text::{Line, Span},
    widgets::{Block, Borders, Clear, List, ListItem, Paragraph, Wrap},
};

use crate::{
    app::{
        ActionOutcome, App, ClickAction, ConnectionState, ModalButtonFocus, REFRESH_INTERVAL,
        display_name_for_command, operator_error_message,
    },
    models::{CommandSummary, ExitOption, TuiAction, UpdateStatusSummary},
    theme,
};

pub const FULL_MIN_WIDTH: u16 = 120;
pub const FULL_MIN_HEIGHT: u16 = 32;
const COMPACT_MIN_WIDTH: u16 = 100;
const COMPACT_MIN_HEIGHT: u16 = 26;
const SMALL_MIN_WIDTH: u16 = 80;
const SMALL_MIN_HEIGHT: u16 = 20;
const COMPACT_ACTION_LABEL_WIDTH: usize = 11;
const SMALL_ACTION_CELL_GUTTER: u16 = 2;

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
        render_help(frame, area, app);
    }

    if app.exit_dialog {
        render_exit_options(frame, area, app);
    }

    if app.confirmation.is_some() {
        render_action_confirmation(frame, area, app);
    }

    if app.action_result_dialog.is_some() {
        render_action_result_dialog(frame, area, app);
    }
}

fn render_full_dashboard(frame: &mut Frame<'_>, area: Rect, app: &mut App, now: Instant) {
    let root = Layout::default()
        .direction(Direction::Vertical)
        .constraints([
            Constraint::Length(3),
            Constraint::Length(14),
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
            Constraint::Length(12),
            Constraint::Length(4),
            Constraint::Min(6),
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
            Span::styled("Esc Exit", theme::warning_style()),
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
    let (supervisor_label, supervisor_style) = match app.connection {
        ConnectionState::Connected => ("OK", theme::badge_success_style()),
        ConnectionState::Disconnected => ("DISCONNECTED", theme::badge_error_style()),
    };

    let mut line = vec![
        Span::styled("Pimax VRC Supervisor TUI", theme::title_style()),
        Span::raw("   Supervisor "),
        theme::badge(supervisor_label, supervisor_style),
    ];

    if let Some(version) = app
        .update_status
        .as_ref()
        .and_then(UpdateStatusSummary::indicator_version)
    {
        line.push(Span::styled(
            format!("   Update available: v{version}"),
            theme::success_style(),
        ));
    }

    line.extend([
        Span::styled(
            format!("   Last OK {}", app.last_success_label(now)),
            theme::secondary_style(),
        ),
        Span::styled(
            format!("   Refresh {}s", REFRESH_INTERVAL.as_secs()),
            theme::secondary_style(),
        ),
    ]);

    if !app.running_actions.is_empty() {
        line.push(Span::styled(
            format!("   Running {}", app.running_actions.len()),
            theme::success_style(),
        ));
    }

    if app.connection == ConnectionState::Disconnected
        && let Some(error) = &app.last_error
    {
        line.push(Span::styled(
            format!("   {}", truncate(&operator_error_message(error), 52)),
            theme::error_style(),
        ));
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
    let mut lines = vec![
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
    lines.extend(update_detail_lines(app.update_status.as_ref()));

    frame.render_widget(
        Paragraph::new(lines).block(theme::panel_block("Supervisor")),
        area,
    );
}

fn render_compact_status(frame: &mut Frame<'_>, area: Rect, app: &App) {
    let status = &app.status;
    let mut lines = vec![
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
    lines.extend(update_detail_lines(app.update_status.as_ref()));

    frame.render_widget(
        Paragraph::new(lines).block(theme::panel_block("Status")),
        area,
    );
}

fn render_compact_actions(frame: &mut Frame<'_>, area: Rect, app: &mut App, now: Instant) {
    let block = theme::panel_block("Actions");
    let inner = block.inner(area);
    frame.render_widget(block, area);

    let mut lines = Vec::new();
    let spaced_rows = inner.height as usize >= TuiAction::ALL.len().saturating_mul(2) - 1;
    let row_step = if spaced_rows { 2 } else { 1 };

    for (index, action) in TuiAction::ALL.iter().copied().enumerate() {
        let row_offset = index * row_step;
        if row_offset >= inner.height as usize {
            break;
        }

        let row = inner.y.saturating_add(row_offset as u16);
        let state = action_state(app, action, now);
        register_start_badge_click_region(
            app,
            Rect::new(inner.x, row, inner.width, 1),
            COMPACT_ACTION_LABEL_WIDTH as u16,
            &state,
            action,
        );

        lines.push(compact_action_line(
            app,
            action,
            now,
            inner.width.saturating_sub(1),
        ));

        if spaced_rows && index + 1 < TuiAction::ALL.len() && lines.len() < inner.height as usize {
            lines.push(Line::from(""));
        }
    }

    frame.render_widget(Paragraph::new(lines).wrap(Wrap { trim: true }), inner);
}

fn render_compact_action_activity(frame: &mut Frame<'_>, area: Rect, app: &App, now: Instant) {
    let mut lines = Vec::new();

    if !app.status.operator_warning.is_empty() {
        lines.push(Line::from(vec![
            theme::badge("WARN", theme::badge_warning_style()),
            Span::raw(" "),
            Span::styled(
                truncate(&app.status.operator_warning, 110),
                theme::warning_style(),
            ),
        ]));
    }

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
                running_action_message(running.action, now.duration_since(running.started_at)),
                theme::badge_success_style(),
            ),
            Span::raw(" "),
            Span::styled(
                running_action_detail(running.action, now.duration_since(running.started_at)),
                foreground(theme::TEXT_SECONDARY),
            ),
        ]));
    }

    if let Some(outcome) = app.last_action_outcome {
        let command = app.last_action_command.as_deref().unwrap_or("action");
        let display_name = display_name_for_command(command);
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
            Span::styled(display_name, foreground(theme::TEXT_PRIMARY)),
            Span::raw(" - "),
            Span::raw(truncate(message, 80)),
        ]));
    } else if let Some(error) = app.last_error.as_deref() {
        lines.push(Line::from(vec![
            Span::styled("Supervisor ", theme::label_style()),
            Span::styled("ERROR", theme::badge_error_style()),
            Span::raw(" "),
            Span::raw(truncate(&operator_error_message(error), 96)),
        ]));
    } else {
        let supervisor = match app.connection {
            ConnectionState::Connected => ("OK", theme::badge_success_style()),
            ConnectionState::Disconnected => ("DISCONNECTED", theme::badge_error_style()),
        };
        lines.push(Line::from(vec![
            Span::styled("Supervisor ", theme::label_style()),
            Span::styled(supervisor.0, supervisor.1),
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
    let (supervisor_label, supervisor_style) = match app.connection {
        ConnectionState::Connected => ("OK", theme::badge_success_style()),
        ConnectionState::Disconnected => ("DISCONNECTED", theme::badge_error_style()),
    };

    let mut spans = vec![
        Span::styled("Pimax VRC Supervisor TUI", theme::title_style()),
        Span::raw("   Supervisor "),
        theme::badge(supervisor_label, supervisor_style),
    ];
    if let Some(version) = app
        .update_status
        .as_ref()
        .and_then(UpdateStatusSummary::indicator_version)
    {
        spans.push(Span::styled(
            format!("   Update available: v{version}"),
            theme::success_style(),
        ));
    }
    let line = Line::from(spans);

    frame.render_widget(
        Paragraph::new(vec![
            line,
            Line::from("0 Help  F5 Refresh  1-7 Actions  Esc Exit"),
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

    for row_index in 0..TuiAction::ALL.len().div_ceil(3) {
        let row_y = inner.y.saturating_add(row_index as u16);
        if row_y >= inner.y.saturating_add(inner.height) {
            continue;
        }

        for column_index in 0..3 {
            let action_index = row_index * 3 + column_index;
            let Some(action) = TuiAction::ALL.get(action_index).copied() else {
                continue;
            };

            let total_gutter = SMALL_ACTION_CELL_GUTTER.saturating_mul(2);
            let available_width = inner.width.saturating_sub(total_gutter);
            let column_width = available_width / 3;
            let x = inner.x.saturating_add(
                column_width
                    .saturating_add(SMALL_ACTION_CELL_GUTTER)
                    .saturating_mul(column_index as u16),
            );
            let width = if column_index == 2 {
                inner.x.saturating_add(inner.width).saturating_sub(x)
            } else {
                column_width
            };

            let cell = Rect::new(x, row_y, width, 1);
            let state = action_state(app, action, now);
            register_start_badge_click_region(
                app,
                cell,
                small_action_badge_offset(action),
                &state,
                action,
            );
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
    if let Some(current) = &app.status.current_action {
        lines.push(Line::from(vec![
            Span::styled("Running: ", theme::label_style()),
            Span::styled(
                display_name_for_command(&current.command),
                foreground(theme::TEXT_PRIMARY),
            ),
            Span::raw(" "),
            Span::styled(
                truncate(&current.progress, area.width.saturating_sub(16) as usize),
                foreground(theme::TEXT_SECONDARY),
            ),
        ]));
    } else if let Some(running) = app.running_actions.first() {
        lines.push(Line::from(vec![
            Span::styled("Running: ", theme::label_style()),
            Span::styled(
                running_action_message(running.action, now.duration_since(running.started_at)),
                foreground(theme::TEXT_PRIMARY),
            ),
            Span::raw(" "),
            Span::styled(
                running_action_detail(running.action, now.duration_since(running.started_at)),
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

    let row_count = 3;
    let rows = Layout::default()
        .direction(Direction::Vertical)
        .constraints(vec![Constraint::Ratio(1, row_count as u32); row_count])
        .split(inner);

    for row_index in 0..row_count {
        if row_index == 2 {
            if let Some(action) = TuiAction::ALL.get(6).copied() {
                render_action_card(frame, rows[row_index], app, action, now);
            }

            continue;
        }

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
    let description = action_description(app, action, &state);
    let mut lines = vec![
        action_card_line(app, action, now, inner_width),
        Line::from(Span::styled(description, foreground(theme::TEXT_PRIMARY))),
    ];

    if action != TuiAction::RestartVrSession
        && let Some(detail) = state.detail
    {
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

    if !app.status.operator_warning.is_empty() {
        lines.push(Line::from(vec![
            theme::badge("WARN", theme::badge_warning_style()),
            Span::raw(" "),
            Span::styled(
                truncate(&app.status.operator_warning, 110),
                theme::warning_style(),
            ),
        ]));
        lines.push(Line::from(""));
    }

    if let Some(current) = &app.status.current_action {
        lines.push(Line::from(vec![
            Span::styled(
                display_name_for_command(&current.command),
                theme::primary_style(),
            ),
            Span::raw("  "),
            Span::styled(current.status.to_ascii_uppercase(), theme::success_style()),
        ]));
        lines.push(Line::from(Span::raw(truncate(&current.progress, 92))));
    } else if app.running_actions.is_empty() {
        lines.push(Line::from(Span::styled(
            "No running actions.",
            theme::secondary_style(),
        )));
    } else {
        for running in app.running_actions.iter().take(3) {
            lines.push(Line::from(vec![
                Span::styled(
                    running_action_message(running.action, now.duration_since(running.started_at)),
                    theme::primary_style(),
                ),
                Span::raw("  "),
                Span::styled(
                    running_action_detail(running.action, now.duration_since(running.started_at)),
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
    if let Some(last) = &app.status.last_action_result {
        let message = if !last.result.is_empty() && last.result != "-" {
            &last.result
        } else if !last.error.is_empty() && last.error != "-" {
            &last.error
        } else {
            &last.progress
        };
        lines.push(Line::from(vec![
            Span::styled(
                display_name_for_command(&last.command),
                theme::primary_style(),
            ),
            Span::raw("  "),
            Span::styled(last.status.to_ascii_uppercase(), theme::secondary_style()),
        ]));
        lines.push(Line::from(Span::raw(truncate(message, 92))));
    } else if let Some(outcome) = app.last_action_outcome {
        let command = app.last_action_command.as_deref().unwrap_or("action");
        let display_name = display_name_for_command(command);
        let when = app
            .last_action_completed_label(now)
            .unwrap_or_else(|| "unknown time".to_string());
        let (status, style) = action_outcome_style(outcome);
        lines.push(Line::from(vec![
            Span::styled(display_name, theme::primary_style()),
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
        let display_name = display_name_for_command(command);
        lines.push(Line::from(vec![
            Span::styled(display_name, theme::warning_style()),
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
    let label = |text: &str| Span::styled(format!("{text:<10}"), theme::label_style());
    let separator = Span::raw("  ");
    let detail_gap = "  ";
    match app.connection {
        ConnectionState::Connected => lines.push(Line::from(vec![
            label("Supervisor"),
            separator.clone(),
            theme::badge("OK", theme::badge_success_style()),
        ])),
        ConnectionState::Disconnected => lines.push(Line::from(vec![
            label("Supervisor"),
            separator.clone(),
            theme::badge("DISCONNECTED", theme::badge_error_style()),
        ])),
    }

    if let Some(error) = &app.last_error {
        let when = app
            .last_error_label(now)
            .unwrap_or_else(|| "unknown time".to_string());
        lines.push(Line::from(vec![
            label("Status"),
            separator.clone(),
            theme::badge("ERROR", theme::badge_error_style()),
            Span::raw(format!("{detail_gap}{when}: ")),
            Span::raw(truncate(&operator_error_message(error), 84)),
        ]));
    } else {
        lines.push(Line::from(vec![
            label("Status"),
            separator.clone(),
            Span::styled("none", theme::secondary_style()),
        ]));
    }

    if let Some(notice) = &app.mouse_notice {
        lines.push(Line::from(vec![
            label("Mouse"),
            separator.clone(),
            Span::styled(truncate(notice, 84), theme::warning_style()),
        ]));
    } else {
        lines.push(Line::from(vec![
            label("Mouse"),
            separator.clone(),
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

    if let Some(notice) = &app.console_close_notice {
        lines.push(Line::from(vec![
            label("Window"),
            separator.clone(),
            Span::styled(truncate(notice, 84), theme::warning_style()),
        ]));
    } else {
        lines.push(Line::from(vec![
            label("Window"),
            separator.clone(),
            Span::styled(
                if app.console_close_enabled {
                    "window close exits Terminal UI only"
                } else {
                    "close handling unavailable"
                },
                theme::secondary_style(),
            ),
        ]));
    }

    if let Some(notice) = &app.supervisor_process_notice {
        lines.push(Line::from(vec![
            label("Parent"),
            separator.clone(),
            Span::styled(truncate(notice, 84), theme::warning_style()),
        ]));
    }

    if app.shutdown_in_progress {
        let message = app
            .shutdown_message
            .as_deref()
            .unwrap_or("Shutdown requested. Closing managed apps...");
        lines.push(Line::from(vec![
            label("Shutdown"),
            separator.clone(),
            theme::badge("RUNNING", theme::badge_warning_style()),
            Span::raw(detail_gap),
            Span::raw(truncate(message, 84)),
        ]));
    } else if let Some(error) = &app.shutdown_error {
        lines.push(Line::from(vec![
            label("Shutdown"),
            separator,
            theme::badge("ERROR", theme::badge_error_style()),
            Span::raw(detail_gap),
            Span::raw(truncate(&operator_error_message(error), 84)),
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

fn render_help(frame: &mut Frame<'_>, area: Rect, app: &mut App) {
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
        help_line("7", "SteamVR"),
        Line::from(""),
        help_line("1-6", "Run action immediately"),
        help_line("7", "Open SteamVR confirmation"),
        help_line("MOUSE", "Click action card or visible modal button"),
        help_line("TAB/L/R", "Move modal button focus"),
        help_line("ENTER", "Activate focused modal button"),
        help_line("SPACE", "Activate focused modal button"),
        help_line("ESC", "Cancel modal"),
        help_line("Q", "Shut down Supervisor and exit TUI after confirmation"),
        help_line("UP/PGUP", "Scroll logs older, pauses live follow"),
        help_line("DOWN/PGDN", "Scroll logs newer"),
        help_line("WHEEL", "Scroll logs older/newer"),
        help_line("END/F", "Resume latest log follow"),
        Line::from(""),
        Line::from(Span::styled(
            "Enter, Space, Esc, or the Close button closes Help only.",
            theme::warning_style(),
        )),
        Line::from("Mouse actions use the same allowed action list and conflict checks."),
        Line::from("Esc or Q opens explicit TUI and Supervisor exit options."),
        Line::from("F1, ?, and Russian help aliases are not mapped."),
        Line::from("Forced stop is not available from this TUI."),
    ];

    frame.render_widget(Clear, popup);
    frame.render_widget(
        Paragraph::new(lines)
            .block(theme::accent_panel_block("Help"))
            .wrap(Wrap { trim: true }),
        popup,
    );
    let close = modal_close_button_rect(popup);
    render_modal_button(frame, close, "[ Close ]", true);
    app.add_click_region(close, ClickAction::CloseModal);
}

fn render_exit_options(frame: &mut Frame<'_>, area: Rect, app: &mut App) {
    let popup = centered_rect(74, 58, area);
    let mut lines = vec![
        Line::from(Span::styled("Exit options", theme::title_style())),
        Line::from(""),
    ];

    for option in ExitOption::ALL {
        let selected = option == app.selected_exit_option;
        let marker = if selected { ">" } else { " " };
        let label_style = if selected {
            theme::warning_style()
        } else {
            theme::secondary_style()
        };
        lines.push(Line::from(vec![
            Span::styled(marker, label_style),
            Span::raw(" ["),
            Span::styled(option.digit().to_string(), label_style),
            Span::raw("] "),
            Span::styled(option.display_name(), label_style),
        ]));
        lines.push(Line::from(format!("    {}", option.detail())));
        lines.push(Line::from(""));
    }

    lines.push(Line::from(Span::styled(
        "UP/DOWN Select",
        theme::secondary_style(),
    )));

    frame.render_widget(Clear, popup);
    frame.render_widget(
        Paragraph::new(lines)
            .block(theme::accent_panel_block("Exit Options"))
            .wrap(Wrap { trim: true }),
        popup,
    );
    let (confirm, cancel) = modal_button_rects(popup);
    render_modal_button(frame, confirm, "[ Select ]", true);
    render_modal_button(frame, cancel, "[ Cancel ]", false);
    register_modal_button_clicks(app, confirm, cancel);
}

fn render_action_confirmation(frame: &mut Frame<'_>, area: Rect, app: &mut App) {
    let Some(action) = app.confirmation.clone() else {
        return;
    };

    let popup = centered_fixed_rect(76, 13, area);
    let mut lines = vec![
        Line::from(Span::styled(action.title, theme::title_style())),
        Line::from(""),
    ];
    lines.extend(action.body.into_iter().map(Line::from));

    frame.render_widget(Clear, popup);
    frame.render_widget(
        Paragraph::new(lines)
            .block(theme::accent_panel_block("SteamVR Control"))
            .wrap(Wrap { trim: true }),
        popup,
    );
    let (confirm, cancel) = modal_button_rects(popup);
    render_modal_button(
        frame,
        confirm,
        "[ Confirm ]",
        app.confirmation_focus == ModalButtonFocus::Confirm,
    );
    render_modal_button(
        frame,
        cancel,
        "[ Cancel ]",
        app.confirmation_focus == ModalButtonFocus::Cancel,
    );
    register_modal_button_clicks(app, confirm, cancel);
}

fn render_action_result_dialog(frame: &mut Frame<'_>, area: Rect, app: &mut App) {
    let Some(result) = app.action_result_dialog.as_ref() else {
        return;
    };

    let popup = centered_rect(62, 36, area);
    let display_name = display_name_for_command(&result.command);
    let (status, style) = action_outcome_style(result.outcome);
    let lines = vec![
        Line::from(Span::styled("Action Result", theme::title_style())),
        Line::from(""),
        Line::from(vec![
            Span::styled(display_name, theme::primary_style()),
            Span::raw("  "),
            Span::styled(status, style),
        ]),
        Line::from(""),
        Line::from(result.message.clone()),
    ];

    frame.render_widget(Clear, popup);
    frame.render_widget(
        Paragraph::new(lines)
            .block(theme::accent_panel_block("Result"))
            .wrap(Wrap { trim: true }),
        popup,
    );
    let close = modal_close_button_rect(popup);
    render_modal_button(frame, close, "[ Close ]", true);
    app.add_click_region(close, ClickAction::CloseModal);
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
    let label = format!("{} {}", action.digit(), compact_action_label(action));
    let description = action_description(app, action, &state);
    let left = format!("{label:<COMPACT_ACTION_LABEL_WIDTH$} {description}");
    aligned_line(
        &left,
        action_state_text(&state).as_ref(),
        width as usize,
        state.style,
    )
}

fn small_action_cell_line(app: &App, action: TuiAction, now: Instant, width: u16) -> Line<'static> {
    let state = action_state(app, action, now);
    let left = format!("{} {:<4} ", action.digit(), small_action_label(action));
    aligned_line(
        &left,
        action_state_text(&state).as_ref(),
        width as usize,
        state.style,
    )
}

fn small_action_label(action: TuiAction) -> &'static str {
    match action {
        TuiAction::RestartCoreApps => "Core",
        TuiAction::StartOscGoesBrrr => "OGB",
        TuiAction::BaseStationsOn => "On",
        TuiAction::BaseStationsOff => "Off",
        TuiAction::RestartOscRouter => "OSC",
        TuiAction::ReloadAutostartApps => "Auto",
        TuiAction::RestartVrSession => "VR",
    }
}

fn compact_action_label(action: TuiAction) -> &'static str {
    match action {
        TuiAction::ReloadAutostartApps => "Auto",
        TuiAction::RestartVrSession => "SteamVR",
        _ => action.short_label(),
    }
}

fn action_description(app: &App, action: TuiAction, state: &ActionState) -> String {
    if action == TuiAction::RestartVrSession {
        return state
            .detail
            .clone()
            .unwrap_or_else(|| app.steamvr_control_mode().detail().to_string());
    }

    action.display_name().to_string()
}

fn running_action_message(action: TuiAction, _elapsed: Duration) -> String {
    action.display_name().to_string()
}

fn running_action_detail(action: TuiAction, elapsed: Duration) -> String {
    let _ = action;
    format!("RUNNING {}", format_duration(elapsed))
}

fn small_action_badge_offset(action: TuiAction) -> u16 {
    format!("{} {:<4} ", action.digit(), small_action_label(action))
        .chars()
        .count() as u16
}

fn register_start_badge_click_region(
    app: &mut App,
    area: Rect,
    badge_offset: u16,
    state: &ActionState,
    action: TuiAction,
) {
    if state.label != "START" {
        return;
    }

    let badge_x = area.x.saturating_add(badge_offset);
    let area_right = area.x.saturating_add(area.width);
    if badge_x >= area_right {
        return;
    }

    let badge_width = (action_state_text(state).chars().count() as u16).min(area_right - badge_x);
    app.add_click_region(
        Rect::new(badge_x, area.y, badge_width, area.height),
        ClickAction::SelectAction(action),
    );
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

fn action_state_text(state: &ActionState) -> String {
    format!("[{}]", state.label)
}

fn action_state(app: &App, action: TuiAction, now: Instant) -> ActionState {
    if app.shutdown_in_progress {
        return ActionState {
            label: "BLOCKED",
            detail: Some("shutdown in progress".to_string()),
            border_color: theme::WARNING_ORANGE,
            style: theme::badge_warning_style(),
        };
    }

    if action == TuiAction::RestartVrSession {
        let mode = app.steamvr_control_mode();
        let style = match mode {
            crate::models::SteamVrControlMode::Start
            | crate::models::SteamVrControlMode::Restart => theme::badge_info_style(),
            crate::models::SteamVrControlMode::Starting
            | crate::models::SteamVrControlMode::Restarting => theme::badge_success_style(),
            crate::models::SteamVrControlMode::Disconnected => theme::badge_error_style(),
        };
        let border_color = match mode {
            crate::models::SteamVrControlMode::Disconnected => theme::BORDER_MUTED,
            crate::models::SteamVrControlMode::Starting
            | crate::models::SteamVrControlMode::Restarting => theme::ACCENT_GREEN,
            crate::models::SteamVrControlMode::Start
            | crate::models::SteamVrControlMode::Restart => theme::BORDER_STRONG,
        };
        return ActionState {
            label: mode.badge(),
            detail: Some(mode.detail().to_string()),
            border_color,
            style,
        };
    }

    if app.connection == ConnectionState::Disconnected {
        return ActionState {
            label: "DISCONNECTED",
            detail: Some("Supervisor disconnected".to_string()),
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

fn register_modal_button_clicks(app: &mut App, confirm: Rect, cancel: Rect) {
    app.add_click_region(confirm, ClickAction::ConfirmModal);
    app.add_click_region(cancel, ClickAction::CancelModal);
}

fn modal_button_rects(popup: Rect) -> (Rect, Rect) {
    const CONFIRM_WIDTH: u16 = 11;
    const CANCEL_WIDTH: u16 = 10;
    const GAP: u16 = 4;
    let total_width = CONFIRM_WIDTH + GAP + CANCEL_WIDTH;
    let left = popup
        .x
        .saturating_add(popup.width.saturating_sub(total_width) / 2);
    let row = popup.y.saturating_add(popup.height.saturating_sub(3));
    (
        Rect::new(left, row, CONFIRM_WIDTH, 1),
        Rect::new(
            left.saturating_add(CONFIRM_WIDTH + GAP),
            row,
            CANCEL_WIDTH,
            1,
        ),
    )
}

fn modal_close_button_rect(popup: Rect) -> Rect {
    const WIDTH: u16 = 9;
    Rect::new(
        popup
            .x
            .saturating_add(popup.width.saturating_sub(WIDTH) / 2),
        popup.y.saturating_add(popup.height.saturating_sub(3)),
        WIDTH,
        1,
    )
}

fn render_modal_button(frame: &mut Frame<'_>, area: Rect, label: &'static str, focused: bool) {
    let style = if focused {
        theme::badge_info_style()
    } else {
        theme::badge_muted_style()
    };
    frame.render_widget(
        Paragraph::new(label)
            .alignment(Alignment::Center)
            .style(style),
        area,
    );
}

fn shortcut_line(width: u16) -> &'static str {
    if width >= 120 {
        "0 Help  F5 Refresh  Wheel Logs  End/F Follow  1 Core  2 OGB  3 On  4 Off  5 OSC  6 Auto  7 SteamVR  Esc Exit"
    } else if width >= 100 {
        "0 Help  F5 Refresh  1-7 Actions  End/F Logs  Esc Exit"
    } else {
        "0 Help  F5 Refresh  1-7 Actions  Esc Exit"
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

fn centered_fixed_rect(width: u16, height: u16, area: Rect) -> Rect {
    let width = width.min(area.width.saturating_sub(2)).max(1);
    let height = height.min(area.height.saturating_sub(2)).max(1);
    Rect::new(
        area.x.saturating_add(area.width.saturating_sub(width) / 2),
        area.y
            .saturating_add(area.height.saturating_sub(height) / 2),
        width,
        height,
    )
}

fn simple_status_line<'a>(label: &'a str, value: &'a str) -> Line<'a> {
    Line::from(vec![
        Span::styled(format!("{label:<14}"), theme::label_style()),
        Span::raw(format!("{:<9}", "")),
        Span::raw(value),
    ])
}

fn update_detail_lines(status: Option<&UpdateStatusSummary>) -> Vec<Line<'static>> {
    let Some(status) = status else {
        return Vec::new();
    };

    let latest = status
        .latest_verified_version
        .as_deref()
        .map(|version| format!("v{version}"))
        .unwrap_or_else(|| "none".to_string());
    let dismissed = if status.dismissed {
        "yes".to_string()
    } else if let Some(previous) = status.dismissed_version.as_deref() {
        format!("no (previous v{previous})")
    } else {
        "no".to_string()
    };
    let checked = status
        .last_successful_check_at
        .as_deref()
        .map(format_update_time)
        .unwrap_or_else(|| "never".to_string());
    let mut lines = vec![
        Line::from(vec![
            Span::styled(format!("{:<14}", "Update"), theme::label_style()),
            Span::raw(format!("v{} -> {latest}", status.current_version)),
        ]),
        Line::from(vec![
            Span::styled(format!("{:<14}", "Channel"), theme::label_style()),
            Span::raw(format!("{}  dismissed {dismissed}", status.channel)),
        ]),
        Line::from(vec![
            Span::styled(format!("{:<14}", "Checked"), theme::label_style()),
            Span::raw(checked),
        ]),
    ];

    if !status.verification_configured {
        lines.push(Line::from(Span::styled(
            "Update verification not configured.",
            theme::secondary_style(),
        )));
    } else if status.last_error_code.is_some() || status.last_error_summary.is_some() {
        let code = status.last_error_code.as_deref().unwrap_or("check_failed");
        let summary = status
            .last_error_summary
            .as_deref()
            .unwrap_or("The cached update check did not complete successfully.");
        lines.push(Line::from(vec![
            Span::styled(format!("{:<14}", "Update error"), theme::label_style()),
            Span::styled(
                truncate(&format!("{code}: {summary}"), 92),
                theme::secondary_style(),
            ),
        ]));
    }

    lines
}

fn format_update_time(value: &str) -> String {
    if value.is_ascii() && value.len() >= 16 && value.as_bytes().get(10) == Some(&b'T') {
        format!("{} {} UTC", &value[..10], &value[11..16])
    } else {
        truncate(value, 28)
    }
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
        "steamvr" if lower.trim() == "running" => fixed_badge("OK", theme::badge_success_style()),
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
        ActionOutcome::Rejected => ("BLOCKED", theme::badge_warning_style()),
        ActionOutcome::BackendOff => ("DISCONNECTED", theme::badge_error_style()),
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

#[cfg(test)]
mod tests {
    use super::*;
    use crate::{
        app::RunningAction,
        diagnostics::TuiDiagnostics,
        models::{
            CommandSummary, RESTART_VR_SESSION_COMMAND, START_STEAMVR_COMMAND, UpdateStatusSummary,
        },
    };
    use ratatui::{Terminal, backend::TestBackend, buffer::Buffer};

    fn app_with_steamvr_commands(steam_vr: &str) -> App {
        let mut app = App::new(TuiDiagnostics::disabled(), false, false);
        app.connection = ConnectionState::Connected;
        app.status.steam_vr = steam_vr.to_string();
        app.commands = vec![
            command(START_STEAMVR_COMMAND, true),
            command(RESTART_VR_SESSION_COMMAND, true),
        ];
        app
    }

    fn command(name: &str, requires_confirmation: bool) -> CommandSummary {
        CommandSummary {
            name: name.to_string(),
            category: "Actions".to_string(),
            output_kind: "Text".to_string(),
            dangerous: false,
            requires_confirmation,
            action_supported: true,
            action_safety_category: "Managed".to_string(),
            tui_executable: true,
            blocked_reason: String::new(),
        }
    }

    fn verified_update() -> UpdateStatusSummary {
        UpdateStatusSummary {
            current_version: "1.3.1".to_string(),
            latest_verified_version: Some("1.4.0".to_string()),
            channel: "Stable".to_string(),
            update_available: true,
            dismissed: false,
            dismissed_version: None,
            last_successful_check_at: Some("2026-07-21T12:00:00+00:00".to_string()),
            last_error_code: None,
            last_error_summary: None,
            verification_configured: true,
        }
    }

    fn render_buffer(app: &mut App, width: u16, height: u16) -> Buffer {
        let backend = TestBackend::new(width, height);
        let mut terminal = Terminal::new(backend).expect("test terminal");
        terminal
            .draw(|frame| render(frame, app))
            .expect("render succeeds");
        terminal.backend().buffer().clone()
    }

    fn rendered_text(buffer: &Buffer) -> String {
        buffer
            .content()
            .iter()
            .map(|cell| cell.symbol())
            .collect::<String>()
    }

    fn text_in_rect(buffer: &Buffer, area: Rect) -> String {
        let mut text = String::new();
        for y in area.y..area.y.saturating_add(area.height) {
            for x in area.x..area.x.saturating_add(area.width) {
                if let Some(cell) = buffer.cell((x, y)) {
                    text.push_str(cell.symbol());
                }
            }
        }
        text
    }

    #[test]
    fn retained_action_progress_uses_generic_name_and_elapsed_time() {
        assert_eq!(
            running_action_message(TuiAction::RestartCoreApps, Duration::from_secs(12)),
            "Restart Core Apps"
        );
        assert_eq!(
            running_action_detail(TuiAction::RestartCoreApps, Duration::from_secs(12)),
            "RUNNING 12s"
        );
    }

    #[test]
    fn steamvr_control_state_renders_start_restart_and_busy() {
        let now = Instant::now();
        let stopped = app_with_steamvr_commands("stopped");
        let start = action_state(&stopped, TuiAction::RestartVrSession, now);
        assert_eq!(start.label, "START");
        assert_eq!(start.detail.as_deref(), Some("Start SteamVR"));
        assert_eq!(action_state_text(&start), "[START]");

        let running = app_with_steamvr_commands("running");
        let restart = action_state(&running, TuiAction::RestartVrSession, now);
        assert_eq!(restart.label, "RESTART");
        assert_eq!(restart.detail.as_deref(), Some("Restart SteamVR"));
        assert_eq!(action_state_text(&restart), "[RESTART]");

        let mut busy = app_with_steamvr_commands("running");
        busy.running_actions.push(RunningAction {
            action: TuiAction::RestartVrSession,
            command: RESTART_VR_SESSION_COMMAND.to_string(),
            started_at: now,
            operation_id: None,
        });
        let busy_state = action_state(&busy, TuiAction::RestartVrSession, now);
        assert_eq!(busy_state.label, "BUSY");
        assert_eq!(busy_state.detail.as_deref(), Some("Restarting SteamVR"));
    }

    #[test]
    fn steamvr_status_badge_does_not_treat_not_running_as_ok() {
        assert_eq!(status_badge("steamvr", "running").content.as_ref(), "OK");
        assert_eq!(
            status_badge("steamvr", "not running").content.as_ref(),
            "OFF"
        );
    }

    #[test]
    fn verified_update_indicator_is_compact_at_every_supported_minimum() {
        for (width, height) in [(120, 32), (100, 26), (80, 20)] {
            let mut app = app_with_steamvr_commands("running");
            app.update_status = Some(verified_update());

            let text = rendered_text(&render_buffer(&mut app, width, height));

            assert!(
                text.contains("Update available: v1.4.0"),
                "{width}x{height}: {text}"
            );
        }
    }

    #[test]
    fn update_details_are_passive_and_bounded() {
        let mut app = app_with_steamvr_commands("running");
        let mut status = verified_update();
        status.update_available = false;
        status.latest_verified_version = None;
        status.verification_configured = false;
        status.last_error_code = Some("verification_unavailable".to_string());
        status.last_error_summary = Some("cached failure".to_string());
        app.update_status = Some(status);

        for (width, height) in [(120, 32), (100, 26)] {
            let text = rendered_text(&render_buffer(&mut app, width, height));
            assert!(text.contains("v1.3.1 -> none"));
            assert!(text.contains("Stable  dismissed no"));
            assert!(text.contains("2026-07-21 12:00 UTC"));
            assert!(
                text.contains("Update verification not configured."),
                "{width}x{height}: {text}"
            );
            assert!(!text.contains("Update available:"));
        }
    }

    #[test]
    fn dismissed_update_has_details_without_prominent_indicator() {
        let mut app = app_with_steamvr_commands("running");
        let mut status = verified_update();
        status.dismissed = true;
        status.dismissed_version = Some("1.4.0".to_string());
        app.update_status = Some(status);

        let text = rendered_text(&render_buffer(&mut app, 120, 32));

        assert!(text.contains("dismissed yes"));
        assert!(!text.contains("Update available:"));
    }

    #[test]
    fn cached_update_failure_appears_only_in_details() {
        let mut app = app_with_steamvr_commands("running");
        let mut status = verified_update();
        status.update_available = false;
        status.latest_verified_version = None;
        status.last_error_code = Some("timeout".to_string());
        status.last_error_summary = Some("The cached check timed out.".to_string());
        app.update_status = Some(status);

        let text = rendered_text(&render_buffer(&mut app, 120, 32));

        assert!(text.contains("timeout: The cached"));
        assert!(!text.contains("Update available:"));
    }

    #[test]
    fn start_and_restart_confirmations_render_exact_clickable_buttons_in_all_layouts() {
        for (width, height) in [(120, 32), (100, 26), (80, 20)] {
            for steam_vr in ["stopped", "running"] {
                let mut app = app_with_steamvr_commands(steam_vr);
                app.request_action_confirmation(TuiAction::RestartVrSession, Instant::now());

                let buffer = render_buffer(&mut app, width, height);
                let text = rendered_text(&buffer);
                let confirm = app
                    .click_regions
                    .iter()
                    .find(|region| region.action == ClickAction::ConfirmModal)
                    .expect("confirm region");
                let cancel = app
                    .click_regions
                    .iter()
                    .find(|region| region.action == ClickAction::CancelModal)
                    .expect("cancel region");

                assert!(text.contains("[ Confirm ]"), "{width}x{height}: {text}");
                assert!(text.contains("[ Cancel ]"), "{width}x{height}: {text}");
                assert_eq!(text_in_rect(&buffer, confirm.area), "[ Confirm ]");
                assert_eq!(text_in_rect(&buffer, cancel.area), "[ Cancel ]");
                assert!(confirm.area.right() <= width && confirm.area.bottom() <= height);
                assert!(cancel.area.right() <= width && cancel.area.bottom() <= height);
                assert_eq!(
                    app.click_action_at(confirm.area.x, confirm.area.y),
                    Some(ClickAction::ConfirmModal)
                );
                assert_eq!(
                    app.click_action_at(cancel.area.x, cancel.area.y),
                    Some(ClickAction::CancelModal)
                );
            }
        }
    }

    #[test]
    fn confirmation_focus_style_tracks_exactly_one_button() {
        let mut app = app_with_steamvr_commands("running");
        app.request_action_confirmation(TuiAction::RestartVrSession, Instant::now());

        let cancel_focused = render_buffer(&mut app, 100, 26);
        let confirm = app
            .click_regions
            .iter()
            .find(|region| region.action == ClickAction::ConfirmModal)
            .expect("confirm region")
            .area;
        let cancel = app
            .click_regions
            .iter()
            .find(|region| region.action == ClickAction::CancelModal)
            .expect("cancel region")
            .area;
        assert_ne!(
            cancel_focused
                .cell((confirm.x, confirm.y))
                .expect("confirm cell")
                .bg,
            cancel_focused
                .cell((cancel.x, cancel.y))
                .expect("cancel cell")
                .bg
        );

        app.focus_confirmation_button(ModalButtonFocus::Confirm);
        let confirm_focused = render_buffer(&mut app, 100, 26);
        assert_eq!(
            confirm_focused
                .cell((confirm.x, confirm.y))
                .expect("confirm cell")
                .bg,
            theme::INFO_BLUE_DIM
        );
        assert_eq!(
            confirm_focused
                .cell((cancel.x, cancel.y))
                .expect("cancel cell")
                .bg,
            theme::BORDER_MUTED
        );
    }

    #[test]
    fn resize_recomputes_modal_hitboxes_inside_the_new_frame() {
        let mut app = app_with_steamvr_commands("running");
        app.request_action_confirmation(TuiAction::RestartVrSession, Instant::now());

        let _ = render_buffer(&mut app, 120, 32);
        let wide_regions = app.click_regions.clone();
        let _ = render_buffer(&mut app, 80, 20);

        assert_ne!(wide_regions, app.click_regions);
        assert!(
            app.click_regions
                .iter()
                .all(|region| { region.area.right() <= 80 && region.area.bottom() <= 20 })
        );
    }

    #[test]
    fn help_modal_renders_a_clickable_close_button() {
        let mut app = app_with_steamvr_commands("stopped");
        app.help_visible = true;

        let buffer = render_buffer(&mut app, 80, 20);
        let close = app
            .click_regions
            .iter()
            .find(|region| region.action == ClickAction::CloseModal)
            .expect("close region");

        assert_eq!(text_in_rect(&buffer, close.area), "[ Close ]");
        assert_eq!(
            app.click_action_at(close.area.x, close.area.y),
            Some(ClickAction::CloseModal)
        );
    }
}
