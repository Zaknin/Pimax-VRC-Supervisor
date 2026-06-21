use std::{
    env,
    io::{self, BufRead, BufReader, Read, Write},
    net::{SocketAddr, TcpStream},
    path::PathBuf,
    process::{Command, Stdio},
    thread,
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
pub const ACTION_READ_WRITE_TIMEOUT: Duration = Duration::from_secs(120);
pub const PIMAX_SHELL_LAUNCH_CHILD_TIMEOUT: Duration = Duration::from_secs(105);
pub const PIMAX_SHELL_LAUNCH_STDOUT_LIMIT: usize = 1024 * 1024;
pub const PIMAX_SHELL_LAUNCH_STDERR_TAIL_LIMIT: usize = 256 * 1024;

// Windows CREATE_NO_WINDOW. This hides the C# command host while leaving the
// Shell-opened Pimax Play UI visible.
pub const CREATE_NO_WINDOW: u32 = 0x0800_0000;

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
        if action == TuiAction::RelaunchPimaxPlay {
            return execute_local_pimax_shell_launch();
        }

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

fn execute_local_pimax_shell_launch() -> Result<CommandResult> {
    let spec = local_pimax_shell_launch_command()?;
    let output = run_local_command(&spec, PIMAX_SHELL_LAUNCH_CHILD_TIMEOUT)?;
    pimax_shell_launch_result_from_output(output)
}

fn run_local_command(spec: &LocalCommandSpec, timeout: Duration) -> Result<CapturedChildOutput> {
    let mut command = Command::new(&spec.program);
    command
        .args(&spec.args)
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());

    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        command.creation_flags(spec.creation_flags);
    }

    let mut child = command
        .spawn()
        .map_err(|error| eyre!("could not start pimax-shell-launch-json: {error}"))?;
    let stdout = child
        .stdout
        .take()
        .ok_or_else(|| eyre!("pimax-shell-launch-json stdout pipe was not available"))?;
    let stderr = child
        .stderr
        .take()
        .ok_or_else(|| eyre!("pimax-shell-launch-json stderr pipe was not available"))?;

    let stdout_reader = thread::spawn(move || {
        read_stream(
            stdout,
            PIMAX_SHELL_LAUNCH_STDOUT_LIMIT,
            RetentionMode::Prefix,
        )
    });
    let stderr_reader = thread::spawn(move || {
        read_stream(
            stderr,
            PIMAX_SHELL_LAUNCH_STDERR_TAIL_LIMIT,
            RetentionMode::Tail,
        )
    });

    let started = Instant::now();
    let status = loop {
        if let Some(status) = child
            .try_wait()
            .map_err(|error| eyre!("could not poll pimax-shell-launch-json: {error}"))?
        {
            break status;
        }

        if started.elapsed() >= timeout {
            let _ = child.kill();
            let _ = child.wait();
            let _ = join_stream_reader(stdout_reader, "stdout");
            let _ = join_stream_reader(stderr_reader, "stderr");
            return Err(eyre!(
                "pimax-shell-launch-json bridge timed out after {} seconds; the child command was stopped without retrying or touching Pimax processes.",
                timeout.as_secs()
            ));
        }

        thread::sleep(Duration::from_millis(50));
    };

    Ok(CapturedChildOutput {
        stdout: join_stream_reader(stdout_reader, "stdout")?,
        stderr: join_stream_reader(stderr_reader, "stderr")?,
        exit_success: status.success(),
        exit_code: status.code(),
    })
}

fn join_stream_reader(
    handle: thread::JoinHandle<io::Result<CapturedStream>>,
    name: &str,
) -> Result<CapturedStream> {
    match handle.join() {
        Ok(Ok(output)) => Ok(output),
        Ok(Err(error)) => Err(eyre!(
            "could not drain pimax-shell-launch-json {name}: {error}"
        )),
        Err(_) => Err(eyre!("pimax-shell-launch-json {name} reader panicked")),
    }
}

fn pimax_shell_launch_result_from_output(output: CapturedChildOutput) -> Result<CommandResult> {
    if output.stdout.overflowed {
        return Err(eyre!(
            "pimax-shell-launch-json stdout exceeded {} bytes; output was contained and not shown in the terminal.",
            PIMAX_SHELL_LAUNCH_STDOUT_LIMIT
        ));
    }

    if output.stderr.overflowed {
        return Err(eyre!(
            "pimax-shell-launch-json stderr exceeded {} bytes; output was contained and not shown in the terminal.",
            PIMAX_SHELL_LAUNCH_STDERR_TAIL_LIMIT
        ));
    }

    if !output.exit_success {
        return Err(eyre!(
            "pimax-shell-launch-json exited with code {}; output was contained and not shown in the terminal.",
            output
                .exit_code
                .map(|code| code.to_string())
                .unwrap_or_else(|| "unknown".to_string())
        ));
    }

    let stdout = String::from_utf8_lossy(&output.stdout.bytes)
        .trim()
        .to_string();
    if stdout.is_empty() {
        return Err(eyre!("pimax-shell-launch-json did not produce JSON output"));
    }

    let data = parse_single_json_object(&stdout)?;
    let message = data
        .get("humanReadableSummary")
        .and_then(Value::as_str)
        .unwrap_or("Pimax Play relaunch command completed.")
        .to_string();

    Ok(CommandResult {
        command: Some("pimax-shell-launch-json".to_string()),
        success: true,
        message: Some(message),
        result_type: Some("pimaxShellLaunch".to_string()),
        data: Some(data),
        error: None,
        ..CommandResult::default()
    })
}

fn parse_single_json_object(stdout: &str) -> Result<Value> {
    let mut values = serde_json::Deserializer::from_str(stdout).into_iter::<Value>();
    let Some(first) = values.next() else {
        return Err(eyre!("pimax-shell-launch-json did not produce JSON output"));
    };
    let first =
        first.map_err(|error| eyre!("could not parse pimax-shell-launch-json output: {error}"))?;

    if values.next().is_some() {
        return Err(eyre!(
            "pimax-shell-launch-json produced more than one JSON value; output was contained and not shown in the terminal."
        ));
    }

    Ok(first)
}

#[derive(Debug, Clone, Copy, Eq, PartialEq)]
enum RetentionMode {
    Prefix,
    Tail,
}

#[derive(Debug, Clone, Eq, PartialEq)]
struct CapturedStream {
    bytes: Vec<u8>,
    overflowed: bool,
    total_bytes: usize,
}

#[derive(Debug, Clone, Eq, PartialEq)]
struct CapturedChildOutput {
    stdout: CapturedStream,
    stderr: CapturedStream,
    exit_success: bool,
    exit_code: Option<i32>,
}

fn read_stream<R: Read>(
    mut reader: R,
    limit: usize,
    retention: RetentionMode,
) -> io::Result<CapturedStream> {
    let mut retained = Vec::new();
    let mut total_bytes = 0usize;
    let mut buffer = [0u8; 8192];

    loop {
        let read = reader.read(&mut buffer)?;
        if read == 0 {
            break;
        }

        total_bytes = total_bytes.saturating_add(read);
        match retention {
            RetentionMode::Prefix => {
                if retained.len() < limit {
                    let available = limit - retained.len();
                    retained.extend_from_slice(&buffer[..read.min(available)]);
                }
            }
            RetentionMode::Tail => {
                retained.extend_from_slice(&buffer[..read]);
                if retained.len() > limit {
                    let excess = retained.len() - limit;
                    retained.drain(..excess);
                }
            }
        }
    }

    Ok(CapturedStream {
        bytes: retained,
        overflowed: total_bytes > limit,
        total_bytes,
    })
}

#[derive(Debug, Clone, Eq, PartialEq)]
struct LocalCommandSpec {
    program: String,
    args: Vec<String>,
    stdin_null: bool,
    stdout_piped: bool,
    stderr_piped: bool,
    creation_flags: u32,
}

fn local_pimax_shell_launch_command() -> Result<LocalCommandSpec> {
    let dll = supervisor_dll_path()?;
    Ok(local_pimax_shell_launch_command_for_dll(dll))
}

fn local_pimax_shell_launch_command_for_dll(dll: PathBuf) -> LocalCommandSpec {
    LocalCommandSpec {
        program: "dotnet".to_string(),
        args: vec![
            dll.to_string_lossy().to_string(),
            "pimax-shell-launch-json".to_string(),
        ],
        stdin_null: true,
        stdout_piped: true,
        stderr_piped: true,
        creation_flags: CREATE_NO_WINDOW,
    }
}

fn supervisor_dll_path() -> Result<PathBuf> {
    let exe = env::current_exe()?;
    let directory = exe
        .parent()
        .ok_or_else(|| eyre!("could not resolve Terminal UI directory"))?;
    let dll = directory.join("PimaxVrcSupervisor.dll");
    if dll.exists() {
        return Ok(dll);
    }

    let development_dll = directory
        .parent()
        .and_then(|deps| deps.parent())
        .and_then(|debug_or_release| debug_or_release.parent())
        .and_then(|target| target.parent())
        .map(|tui| {
            tui.parent()
                .unwrap_or(tui)
                .join("PimaxVrcSupervisor")
                .join("bin")
                .join("Release")
                .join("net9.0-windows10.0.19041.0")
                .join("PimaxVrcSupervisor.dll")
        });
    if let Some(path) = development_dll {
        if path.exists() {
            return Ok(path);
        }
    }

    Err(eyre!(
        "could not find PimaxVrcSupervisor.dll next to Terminal UI"
    ))
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Cursor;

    fn valid_output(stderr: &[u8]) -> CapturedChildOutput {
        CapturedChildOutput {
            stdout: CapturedStream {
                bytes: br#"{"schema":"pimax-shell-launch-result-v1","result":"launchedAndRegistered","humanReadableSummary":"Pimax Play launched successfully and the headset is registered.","shellRequestCount":1,"retryCount":0}"#.to_vec(),
                overflowed: false,
                total_bytes: 189,
            },
            stderr: CapturedStream {
                bytes: stderr.to_vec(),
                overflowed: false,
                total_bytes: stderr.len(),
            },
            exit_success: true,
            exit_code: Some(0),
        }
    }

    #[test]
    fn local_pimax_shell_launch_command_uses_json_command() {
        let spec = local_pimax_shell_launch_command_for_dll(PathBuf::from(
            r"C:\Release\PimaxVrcSupervisor.dll",
        ));

        assert_eq!(spec.program, "dotnet");
        assert!(spec.args[0].ends_with("PimaxVrcSupervisor.dll"));
        assert_eq!(spec.args[1], "pimax-shell-launch-json");
        assert!(spec.stdin_null);
        assert!(spec.stdout_piped);
        assert!(spec.stderr_piped);
        assert_eq!(spec.creation_flags & CREATE_NO_WINDOW, CREATE_NO_WINDOW);
        assert!(
            ![
                "cmd.exe",
                "powershell.exe",
                "explorer.exe",
                "PimaxClient.exe"
            ]
            .iter()
            .any(|blocked| spec.program.eq_ignore_ascii_case(blocked))
        );
    }

    #[test]
    fn valid_json_with_noisy_stderr_is_contained() {
        let output =
            valid_output(b"SDKServicePort\r\nHMDData Changed\r\nssl_client_socket_impl\r\n");

        let result = pimax_shell_launch_result_from_output(output).unwrap();

        assert!(result.success);
        assert_eq!(result.command.as_deref(), Some("pimax-shell-launch-json"));
        let rendered = result.message.unwrap();
        assert!(!rendered.contains("SDKServicePort"));
        assert!(!rendered.contains("HMDData Changed"));
        assert!(!rendered.contains("ssl_client_socket_impl"));
    }

    #[test]
    fn malformed_stdout_is_reported_without_stderr_dump() {
        let mut output = valid_output(b"SDKServicePort\r\nruntimeErrorCode\r\n");
        output.stdout.bytes = b"{not json".to_vec();
        output.stdout.total_bytes = output.stdout.bytes.len();

        let error = pimax_shell_launch_result_from_output(output)
            .unwrap_err()
            .to_string();

        assert!(error.contains("could not parse pimax-shell-launch-json output"));
        assert!(!error.contains("SDKServicePort"));
        assert!(!error.contains("runtimeErrorCode"));
    }

    #[test]
    fn multiple_json_values_are_refused() {
        let mut output = valid_output(b"");
        output.stdout.bytes = br#"{"result":"a"}{"result":"b"}"#.to_vec();
        output.stdout.total_bytes = output.stdout.bytes.len();

        let error = pimax_shell_launch_result_from_output(output)
            .unwrap_err()
            .to_string();

        assert!(error.contains("more than one JSON value"));
    }

    #[test]
    fn oversized_stdout_is_refused_safely() {
        let mut output = valid_output(b"");
        output.stdout.overflowed = true;
        output.stdout.total_bytes = PIMAX_SHELL_LAUNCH_STDOUT_LIMIT + 1;

        let error = pimax_shell_launch_result_from_output(output)
            .unwrap_err()
            .to_string();

        assert!(error.contains("stdout exceeded"));
        assert!(error.contains("not shown in the terminal"));
    }

    #[test]
    fn oversized_stderr_is_refused_safely() {
        let mut output = valid_output(b"SDKServicePort");
        output.stderr.overflowed = true;
        output.stderr.total_bytes = PIMAX_SHELL_LAUNCH_STDERR_TAIL_LIMIT + 1;

        let error = pimax_shell_launch_result_from_output(output)
            .unwrap_err()
            .to_string();

        assert!(error.contains("stderr exceeded"));
        assert!(!error.contains("SDKServicePort"));
    }

    #[test]
    fn nonzero_exit_is_contained() {
        let mut output = valid_output(b"ssl_client_socket_impl handshake failed");
        output.exit_success = false;
        output.exit_code = Some(3);

        let error = pimax_shell_launch_result_from_output(output)
            .unwrap_err()
            .to_string();

        assert!(error.contains("exited with code 3"));
        assert!(!error.contains("ssl_client_socket_impl"));
    }

    #[test]
    fn stderr_tail_retention_keeps_draining_bounded_tail() {
        let captured = read_stream(
            Cursor::new(b"SDKServicePort\nHMDData Changed\nruntimeErrorCode\n"),
            16,
            RetentionMode::Tail,
        )
        .unwrap();

        assert!(captured.overflowed);
        assert_eq!(
            captured.total_bytes,
            b"SDKServicePort\nHMDData Changed\nruntimeErrorCode\n".len()
        );
        assert_eq!(
            String::from_utf8_lossy(&captured.bytes),
            "untimeErrorCode\n"
        );
    }

    #[test]
    fn stdout_prefix_retention_keeps_draining_after_limit() {
        let captured = read_stream(Cursor::new(b"abcdefghij"), 4, RetentionMode::Prefix).unwrap();

        assert!(captured.overflowed);
        assert_eq!(captured.total_bytes, 10);
        assert_eq!(captured.bytes, b"abcd");
    }
}
