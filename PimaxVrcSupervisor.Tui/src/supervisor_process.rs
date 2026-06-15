use std::{
    ffi::OsStr,
    io,
    sync::mpsc::{Receiver, channel},
    thread,
};

pub enum SupervisorPidArgument {
    None,
    Monitor(SupervisorProcessMonitor),
    AlreadyExited(String),
    Fallback(String),
}

pub struct SupervisorProcessMonitor {
    receiver: Receiver<()>,
}

impl SupervisorProcessMonitor {
    pub fn try_recv_exit(&self) -> bool {
        self.receiver.try_recv().is_ok()
    }
}

pub fn from_args(args: &[std::ffi::OsString]) -> SupervisorPidArgument {
    let Some(value) = find_supervisor_pid(args) else {
        return SupervisorPidArgument::None;
    };
    let Some(value) = value else {
        return SupervisorPidArgument::Fallback(
            "Supervisor PID monitor disabled; --supervisor-pid was supplied without a PID value."
                .to_string(),
        );
    };

    let Ok(pid64) = value.to_string_lossy().parse::<u64>() else {
        return SupervisorPidArgument::Fallback(format!(
            "Supervisor PID monitor disabled; invalid --supervisor-pid value: {}",
            value.to_string_lossy()
        ));
    };

    if pid64 == 0 || pid64 > u32::MAX as u64 {
        return SupervisorPidArgument::Fallback(format!(
            "Supervisor PID monitor disabled; out-of-range --supervisor-pid value: {pid64}"
        ));
    }

    start_monitor(pid64 as u32)
}

fn find_supervisor_pid(args: &[std::ffi::OsString]) -> Option<Option<&OsStr>> {
    let mut index = 0;
    while index < args.len() {
        let arg = args[index].as_os_str();
        if arg == OsStr::new("--supervisor-pid") {
            return Some(args.get(index + 1).map(|value| value.as_os_str()));
        }

        index += 1;
    }

    None
}

#[cfg(windows)]
fn start_monitor(pid: u32) -> SupervisorPidArgument {
    match windows_process::open(pid) {
        Ok(handle) => {
            let (sender, receiver) = channel();
            thread::Builder::new()
                .name("supervisor-process-monitor".to_string())
                .spawn(move || {
                    let _handle = handle;
                    let _ = windows_process::wait(_handle.raw());
                    let _ = sender.send(());
                })
                .map(|_| SupervisorPidArgument::Monitor(SupervisorProcessMonitor { receiver }))
                .unwrap_or_else(|error| {
                    SupervisorPidArgument::Fallback(format!(
                        "Supervisor PID monitor disabled; could not start monitor thread: {error}"
                    ))
                })
        }
        Err(windows_process::OpenProcessError::AlreadyExited(message)) => {
            SupervisorPidArgument::AlreadyExited(message)
        }
        Err(windows_process::OpenProcessError::Fallback(message)) => {
            SupervisorPidArgument::Fallback(message)
        }
    }
}

#[cfg(not(windows))]
fn start_monitor(pid: u32) -> SupervisorPidArgument {
    SupervisorPidArgument::Fallback(format!(
        "Supervisor PID monitor disabled; process handles are Windows-only. PID={pid}"
    ))
}

#[cfg(windows)]
mod windows_process {
    use super::*;
    use std::ffi::c_void;

    const SYNCHRONIZE: u32 = 0x0010_0000;
    const PROCESS_QUERY_LIMITED_INFORMATION: u32 = 0x1000;
    const INFINITE: u32 = 0xffff_ffff;
    const WAIT_FAILED: u32 = 0xffff_ffff;
    const ERROR_INVALID_PARAMETER: i32 = 87;
    const ERROR_ACCESS_DENIED: i32 = 5;

    #[link(name = "kernel32")]
    unsafe extern "system" {
        fn OpenProcess(dwDesiredAccess: u32, bInheritHandle: i32, dwProcessId: u32) -> *mut c_void;
        fn WaitForSingleObject(hHandle: *mut c_void, dwMilliseconds: u32) -> u32;
        fn CloseHandle(hObject: *mut c_void) -> i32;
    }

    #[derive(Debug)]
    pub enum OpenProcessError {
        AlreadyExited(String),
        Fallback(String),
    }

    pub struct ProcessHandle(*mut c_void);

    unsafe impl Send for ProcessHandle {}

    impl ProcessHandle {
        pub fn raw(&self) -> *mut c_void {
            self.0
        }
    }

    impl Drop for ProcessHandle {
        fn drop(&mut self) {
            unsafe {
                let _ = CloseHandle(self.0);
            }
        }
    }

    pub fn open(pid: u32) -> Result<ProcessHandle, OpenProcessError> {
        let handle =
            unsafe { OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, 0, pid) };
        if !handle.is_null() {
            return Ok(ProcessHandle(handle));
        }

        let error = io::Error::last_os_error();
        match error.raw_os_error() {
            Some(ERROR_INVALID_PARAMETER) => Err(OpenProcessError::AlreadyExited(format!(
                "Supervisor process PID {pid} already exited."
            ))),
            Some(ERROR_ACCESS_DENIED) => Err(OpenProcessError::Fallback(format!(
                "Supervisor PID monitor disabled; access denied for PID {pid}."
            ))),
            _ => Err(OpenProcessError::Fallback(format!(
                "Supervisor PID monitor disabled; could not open PID {pid}: {error}"
            ))),
        }
    }

    pub fn wait(handle: *mut c_void) -> io::Result<()> {
        let result = unsafe { WaitForSingleObject(handle, INFINITE) };
        if result == WAIT_FAILED {
            Err(io::Error::last_os_error())
        } else {
            Ok(())
        }
    }
}
