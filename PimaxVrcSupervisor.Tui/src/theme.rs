use ratatui::{
    style::{Color, Modifier, Style},
    text::Span,
    widgets::{Block, BorderType, Borders},
};

pub const BACKGROUND: Color = Color::Rgb(0x18, 0x18, 0x18);
pub const APP_SURFACE: Color = Color::Rgb(0x20, 0x20, 0x28);
pub const PANEL_SURFACE: Color = Color::Rgb(0x24, 0x24, 0x2C);
pub const PANEL_ELEVATED: Color = Color::Rgb(0x2B, 0x2B, 0x33);
pub const BORDER_MUTED: Color = Color::Rgb(0x5C, 0x5C, 0x66);
pub const BORDER_STRONG: Color = Color::Rgb(0xA8, 0xA8, 0xB0);
pub const TEXT_PRIMARY: Color = Color::Rgb(0xFF, 0xFF, 0xFF);
pub const TEXT_SECONDARY: Color = Color::Rgb(0xA0, 0xA0, 0xAA);
pub const TEXT_DIM: Color = Color::Rgb(0x70, 0x70, 0x78);
pub const ACCENT_GREEN: Color = Color::Rgb(0x32, 0xD6, 0x00);
pub const ACCENT_GREEN_DIM: Color = Color::Rgb(0x1D, 0x5C, 0x1D);
pub const WARNING_ORANGE: Color = Color::Rgb(0xFF, 0xA0, 0x00);
pub const WARNING_ORANGE_DIM: Color = Color::Rgb(0x5A, 0x35, 0x00);
pub const ERROR_RED: Color = Color::Rgb(0xFF, 0x3B, 0x3B);
pub const ERROR_RED_DIM: Color = Color::Rgb(0x5A, 0x18, 0x18);
pub const INFO_BLUE_DIM: Color = Color::Rgb(0x14, 0x3A, 0x5A);

pub fn panel_block(title: &'static str) -> Block<'static> {
    Block::default()
        .borders(Borders::ALL)
        .border_type(BorderType::Rounded)
        .border_style(Style::default().fg(BORDER_MUTED))
        .style(Style::default().fg(TEXT_PRIMARY).bg(PANEL_SURFACE))
        .title(Span::styled(title, title_style()))
}

pub fn accent_panel_block(title: &'static str) -> Block<'static> {
    panel_block(title)
        .border_style(Style::default().fg(BORDER_STRONG))
        .style(Style::default().fg(TEXT_PRIMARY).bg(APP_SURFACE))
}

pub fn app_style() -> Style {
    Style::default().fg(TEXT_PRIMARY).bg(BACKGROUND)
}

pub fn title_style() -> Style {
    Style::default()
        .fg(TEXT_PRIMARY)
        .add_modifier(Modifier::BOLD)
}

pub fn primary_style() -> Style {
    Style::default().fg(TEXT_PRIMARY)
}

pub fn secondary_style() -> Style {
    Style::default().fg(TEXT_SECONDARY)
}

pub fn dim_style() -> Style {
    Style::default().fg(TEXT_DIM)
}

pub fn label_style() -> Style {
    Style::default()
        .fg(TEXT_SECONDARY)
        .add_modifier(Modifier::BOLD)
}

pub fn success_style() -> Style {
    Style::default()
        .fg(ACCENT_GREEN)
        .add_modifier(Modifier::BOLD)
}

pub fn warning_style() -> Style {
    Style::default()
        .fg(WARNING_ORANGE)
        .add_modifier(Modifier::BOLD)
}

pub fn error_style() -> Style {
    Style::default().fg(ERROR_RED).add_modifier(Modifier::BOLD)
}

pub fn badge_success_style() -> Style {
    Style::default()
        .fg(TEXT_PRIMARY)
        .bg(ACCENT_GREEN_DIM)
        .add_modifier(Modifier::BOLD)
}

pub fn badge_info_style() -> Style {
    Style::default()
        .fg(TEXT_PRIMARY)
        .bg(INFO_BLUE_DIM)
        .add_modifier(Modifier::BOLD)
}

pub fn badge_warning_style() -> Style {
    Style::default()
        .fg(TEXT_PRIMARY)
        .bg(WARNING_ORANGE_DIM)
        .add_modifier(Modifier::BOLD)
}

pub fn badge_error_style() -> Style {
    Style::default()
        .fg(TEXT_PRIMARY)
        .bg(ERROR_RED_DIM)
        .add_modifier(Modifier::BOLD)
}

pub fn badge_muted_style() -> Style {
    Style::default()
        .fg(TEXT_PRIMARY)
        .bg(BORDER_MUTED)
        .add_modifier(Modifier::BOLD)
}

pub fn badge(label: impl Into<String>, style: Style) -> Span<'static> {
    Span::styled(
        format!(" {} ", label.into()),
        style.add_modifier(Modifier::BOLD),
    )
}
