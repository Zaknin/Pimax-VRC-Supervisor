use std::{
    io::{BufRead, BufReader, Write},
    net::{SocketAddr, TcpStream},
    time::{Duration, Instant},
};

use color_eyre::eyre::{Result, eyre};
use serde_json::{Value, json};

use crate::{
    diagnostics::DiagnosticsHandle,
    models::{CommandResult, QueryResponse, TuiAction},
};

pub const BACKEND_HOST: &str = "127.0.0.1";
pub const BACKEND_PORT: u16 = 37957;
pub const CONNECT_TIMEOUT: Duration = Duration::from_millis(1000);
pub const READ_WRITE_TIMEOUT: Duration = Duration::from_millis(1000);
pub const ACTION_READ_WRITE_TIMEOUT: Duration = Duration::from_secs(30);

pub struct SupervisorBridge {
    endpoint: SocketAddr,
    diagnostics: Option<DiagnosticsHandle>,
}

impl Default for SupervisorBridge {
    fn default() -> Self {
        Self {
            endpoint: backend_endpoint()
                .parse()
                .expect("static endpoint is valid"),
            diagnostics: None,
        }
    }
}

pub fn backend_endpoint() -> String {
    format!("{BACKEND_HOST}:{BACKEND_PORT}")
}

impl SupervisorBridge {
    pub fn with_diagnostics(diagnostics: DiagnosticsHandle) -> Self {
        Self {
            diagnostics: Some(diagnostics),
            ..Self::default()
        }
    }

    pub fn query_status(&self) -> Result<QueryResponse> {
        self.query(json!({ "resource": "status" }))
    }

    pub fn query_commands(&self) -> Result<QueryResponse> {
        self.query(json!({ "resource": "commands" }))
    }

    pub fn query_log(&self, max_lines: usize) -> Result<QueryResponse> {
        self.query(json!({ "resource": "log", "maxLines": max_lines }))
    }

    pub fn execute_tui_action(&self, action: TuiAction) -> Result<CommandResult> {
        let request_json =
            serde_json::to_string(&json!({ "command": action.command_name(), "confirmed": true }))?;
        let response_line = self.send_line(
            &format!("action-json {request_json}"),
            ACTION_READ_WRITE_TIMEOUT,
        )?;
        let response = serde_json::from_str::<CommandResult>(&response_line).map_err(|error| {
            eyre!("could not parse supervisor action response: {error}; response={response_line}")
        })?;

        if response.success {
            Ok(response)
        } else {
            let message = response
                .message
                .clone()
                .or_else(|| response.error.clone())
                .unwrap_or_else(|| "supervisor action failed".to_string());
            Err(eyre!(message))
        }
    }

    pub fn request_graceful_shutdown(&self) -> Result<CommandResult> {
        let request_json = serde_json::to_string(
            &json!({ "action": "request-graceful-shutdown", "source": "Desktop TUI" }),
        )?;
        let response_line = self.send_line(
            &format!("lifecycle-json {request_json}"),
            ACTION_READ_WRITE_TIMEOUT,
        )?;
        let response = serde_json::from_str::<CommandResult>(&response_line).map_err(|error| {
            eyre!(
                "could not parse supervisor lifecycle response: {error}; response={response_line}"
            )
        })?;

        if response.success {
            Ok(response)
        } else {
            let message = response
                .message
                .clone()
                .or_else(|| response.error.clone())
                .unwrap_or_else(|| "supervisor lifecycle request failed".to_string());
            Err(eyre!(message))
        }
    }

    fn query(&self, request: Value) -> Result<QueryResponse> {
        let request_json = serde_json::to_string(&request)?;
        let response_line =
            self.send_line(&format!("query-json {request_json}"), READ_WRITE_TIMEOUT)?;
        let response = serde_json::from_str::<QueryResponse>(&response_line).map_err(|error| {
            eyre!("could not parse supervisor response: {error}; response={response_line}")
        })?;

        if response.success {
            Ok(response)
        } else {
            let message = response
                .message
                .clone()
                .or_else(|| response.error.clone())
                .unwrap_or_else(|| "supervisor query failed".to_string());
            Err(eyre!(message))
        }
    }

    fn send_line(&self, command: &str, read_write_timeout: Duration) -> Result<String> {
        let started = Instant::now();
        let result = self.send_line_inner(command, read_write_timeout);
        if let Some(diagnostics) = &self.diagnostics {
            diagnostics.record_bridge_call(
                started.elapsed(),
                result.is_ok(),
                result
                    .as_ref()
                    .err()
                    .is_some_and(|error| is_timeout_error(&error.to_string())),
            );
        }

        result
    }

    fn send_line_inner(&self, command: &str, read_write_timeout: Duration) -> Result<String> {
        let mut stream = TcpStream::connect_timeout(&self.endpoint, CONNECT_TIMEOUT)
            .map_err(|error| eyre!("backend unavailable at {}: {error}", self.endpoint))?;

        stream.set_read_timeout(Some(read_write_timeout))?;
        stream.set_write_timeout(Some(read_write_timeout))?;

        writeln!(stream, "{command}")?;

        let mut reader = BufReader::new(stream);
        let mut response = String::new();
        let bytes_read = reader.read_line(&mut response)?;

        if bytes_read == 0 {
            return Err(eyre!("backend closed connection without a response"));
        }

        Ok(response.trim_end_matches(['\r', '\n']).to_string())
    }
}

fn is_timeout_error(message: &str) -> bool {
    let message = message.to_ascii_lowercase();
    message.contains("timed out") || message.contains("timeout") || message.contains("would block")
}
