use color_eyre::eyre::Result;

use crate::{
    bridge::SupervisorBridge,
    models::{
        commands_from_response, logs_from_response, status_from_response, CommandSummary, LogLine,
        StatusSummary,
    },
};

#[derive(Debug, Clone, Copy, Eq, PartialEq)]
pub enum ConnectionState {
    Connected,
    Disconnected,
}

pub struct App {
    pub connection: ConnectionState,
    pub status: StatusSummary,
    pub commands: Vec<CommandSummary>,
    pub logs: Vec<LogLine>,
    pub error: Option<String>,
}

impl App {
    pub fn new() -> Self {
        Self {
            connection: ConnectionState::Disconnected,
            status: StatusSummary::default(),
            commands: Vec::new(),
            logs: Vec::new(),
            error: None,
        }
    }

    pub fn refresh(&mut self) {
        let bridge = SupervisorBridge::default();

        match Self::load(&bridge) {
            Ok((status, commands, logs)) => {
                self.connection = ConnectionState::Connected;
                self.status = status;
                self.commands = commands;
                self.logs = logs;
                self.error = None;
            }
            Err(error) => {
                self.connection = ConnectionState::Disconnected;
                self.error = Some(error.to_string());
            }
        }
    }

    fn load(
        bridge: &SupervisorBridge,
    ) -> Result<(StatusSummary, Vec<CommandSummary>, Vec<LogLine>)> {
        let status_response = bridge.query_status()?;
        let commands_response = bridge.query_commands()?;
        let log_response = bridge.query_log(14)?;

        Ok((
            status_from_response(&status_response),
            commands_from_response(&commands_response),
            logs_from_response(&log_response),
        ))
    }
}
