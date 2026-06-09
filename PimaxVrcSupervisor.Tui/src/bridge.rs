use std::{
    io::{BufRead, BufReader, Write},
    net::{SocketAddr, TcpStream},
    time::Duration,
};

use color_eyre::eyre::{Result, eyre};
use serde_json::{Value, json};

use crate::models::QueryResponse;

pub const BACKEND_HOST: &str = "127.0.0.1";
pub const BACKEND_PORT: u16 = 37957;
pub const CONNECT_TIMEOUT: Duration = Duration::from_millis(1000);
pub const READ_WRITE_TIMEOUT: Duration = Duration::from_millis(1000);

pub struct SupervisorBridge {
    endpoint: SocketAddr,
}

impl Default for SupervisorBridge {
    fn default() -> Self {
        Self {
            endpoint: backend_endpoint()
                .parse()
                .expect("static endpoint is valid"),
        }
    }
}

pub fn backend_endpoint() -> String {
    format!("{BACKEND_HOST}:{BACKEND_PORT}")
}

impl SupervisorBridge {
    pub fn query_status(&self) -> Result<QueryResponse> {
        self.query(json!({ "resource": "status" }))
    }

    pub fn query_commands(&self) -> Result<QueryResponse> {
        self.query(json!({ "resource": "commands" }))
    }

    pub fn query_log(&self, max_lines: usize) -> Result<QueryResponse> {
        self.query(json!({ "resource": "log", "maxLines": max_lines }))
    }

    fn query(&self, request: Value) -> Result<QueryResponse> {
        let request_json = serde_json::to_string(&request)?;
        let response_line = self.send_line(&format!("query-json {request_json}"))?;
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

    fn send_line(&self, command: &str) -> Result<String> {
        let mut stream = TcpStream::connect_timeout(&self.endpoint, CONNECT_TIMEOUT)
            .map_err(|error| eyre!("backend unavailable at {}: {error}", self.endpoint))?;

        stream.set_read_timeout(Some(READ_WRITE_TIMEOUT))?;
        stream.set_write_timeout(Some(READ_WRITE_TIMEOUT))?;

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
