use std::{
    io::{BufRead, BufReader, Write},
    net::{SocketAddr, TcpStream},
    time::{Duration, Instant},
};

use color_eyre::eyre::{Result, eyre};
use serde_json::{Value, json};

use crate::{
    diagnostics::DiagnosticsHandle,
    models::{CommandResult, ExitOption, QueryResponse},
};

pub const BACKEND_HOST: &str = "127.0.0.1";
pub const BACKEND_PORT: u16 = 37957;
pub const CONNECT_TIMEOUT: Duration = Duration::from_millis(1000);
pub const READ_WRITE_TIMEOUT: Duration = Duration::from_millis(1000);
pub const ACTION_READ_WRITE_TIMEOUT: Duration = Duration::from_secs(120);

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

    pub fn query_update_status(&self) -> Result<QueryResponse> {
        self.query(json!({ "resource": "update-status" }))
    }

    pub fn query_commands(&self) -> Result<QueryResponse> {
        self.query(json!({ "resource": "commands" }))
    }

    pub fn query_log(&self, max_lines: usize) -> Result<QueryResponse> {
        self.query(json!({ "resource": "log", "maxLines": max_lines }))
    }

    #[cfg_attr(test, allow(dead_code))]
    pub fn execute_tui_action(
        &self,
        command: &str,
        request_id: Option<&str>,
        client_instance_id: &str,
    ) -> Result<CommandResult> {
        let request_json = build_action_request_json(command, request_id, client_instance_id)?;
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
        self.send_lifecycle_request(request_json)
    }

    pub fn request_desktop_tui_close(&self) -> Result<CommandResult> {
        let request_json = serde_json::to_string(
            &json!({ "action": "close-desktop-tui", "source": "Desktop TUI" }),
        )?;
        self.send_lifecycle_request(request_json)
    }

    pub fn request_supervisor_exit(&self, option: ExitOption) -> Result<CommandResult> {
        let Some(mode) = option.lifecycle_mode() else {
            return Err(eyre!(
                "exit option does not map to a Supervisor shutdown mode"
            ));
        };
        let request_json = serde_json::to_string(
            &json!({ "action": "request-supervisor-exit", "mode": mode, "source": "Desktop TUI" }),
        )?;
        self.send_lifecycle_request(request_json)
    }

    fn send_lifecycle_request(&self, request_json: String) -> Result<CommandResult> {
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

fn build_action_request_json(
    command: &str,
    request_id: Option<&str>,
    client_instance_id: &str,
) -> Result<String> {
    let mut request = json!({
        "command": command,
        "confirmed": true,
        "source": "Desktop TUI",
        "sourceClientType": "desktop-tui",
        "sourceClientInstanceId": client_instance_id
    });
    if let Some(request_id) = request_id {
        request["requestId"] = json!(request_id);
    }

    Ok(serde_json::to_string(&request)?)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn action_payload_retains_request_and_client_identity() {
        let payload =
            build_action_request_json("restart-vr-session", Some("request-x"), "client-y")
                .expect("payload");
        let value: Value = serde_json::from_str(&payload).expect("json");

        assert_eq!(value["requestId"], "request-x");
        assert_eq!(value["command"], "restart-vr-session");
        assert_eq!(value["confirmed"], true);
        assert_eq!(value["sourceClientType"], "desktop-tui");
        assert_eq!(value["sourceClientInstanceId"], "client-y");
    }

    #[test]
    fn update_status_uses_only_the_cached_supervisor_resource() {
        let request =
            serde_json::to_string(&json!({ "resource": "update-status" })).expect("request json");

        assert_eq!(request, r#"{"resource":"update-status"}"#);
        let dependencies = include_str!("../Cargo.toml");
        assert!(!dependencies.contains("reqwest"));
        assert!(!dependencies.contains("hyper"));
    }

    #[test]
    fn retained_action_payload_does_not_create_restart_request_identity() {
        let payload =
            build_action_request_json("restart-core-apps", None, "client-y").expect("payload");
        let value: Value = serde_json::from_str(&payload).expect("json");

        assert!(value.get("requestId").is_none());
    }
}
