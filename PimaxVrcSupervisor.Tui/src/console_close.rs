use std::io;

#[cfg(windows)]
mod platform {
    use std::{
        io::{self, Write},
        net::{SocketAddr, TcpStream},
        sync::atomic::{AtomicBool, Ordering},
    };

    use crate::bridge::{BACKEND_HOST, BACKEND_PORT, CONNECT_TIMEOUT, READ_WRITE_TIMEOUT};

    const CTRL_CLOSE_EVENT: u32 = 2;
    const CTRL_LOGOFF_EVENT: u32 = 5;
    const CTRL_SHUTDOWN_EVENT: u32 = 6;

    static SHUTDOWN_REQUESTED: AtomicBool = AtomicBool::new(false);

    pub struct ConsoleCloseGuard;

    impl Drop for ConsoleCloseGuard {
        fn drop(&mut self) {
            unsafe {
                SetConsoleCtrlHandler(Some(console_ctrl_handler), 0);
            }
        }
    }

    pub fn install() -> io::Result<ConsoleCloseGuard> {
        let result = unsafe { SetConsoleCtrlHandler(Some(console_ctrl_handler), 1) };
        if result == 0 {
            Err(io::Error::last_os_error())
        } else {
            Ok(ConsoleCloseGuard)
        }
    }

    pub fn mark_shutdown_requested() {
        SHUTDOWN_REQUESTED.store(true, Ordering::SeqCst);
    }

    unsafe extern "system" fn console_ctrl_handler(ctrl_type: u32) -> i32 {
        match ctrl_type {
            CTRL_CLOSE_EVENT | CTRL_LOGOFF_EVENT | CTRL_SHUTDOWN_EVENT => {
                if !SHUTDOWN_REQUESTED.swap(true, Ordering::SeqCst) {
                    let _ = send_window_close_shutdown_request();
                }

                1
            }
            _ => 0,
        }
    }

    fn send_window_close_shutdown_request() -> io::Result<()> {
        let endpoint: SocketAddr =
            format!("{BACKEND_HOST}:{BACKEND_PORT}")
                .parse()
                .map_err(|error| {
                    io::Error::new(
                        io::ErrorKind::InvalidInput,
                        format!("invalid backend endpoint: {error}"),
                    )
                })?;
        let mut stream = TcpStream::connect_timeout(&endpoint, CONNECT_TIMEOUT)?;
        stream.set_read_timeout(Some(READ_WRITE_TIMEOUT))?;
        stream.set_write_timeout(Some(READ_WRITE_TIMEOUT))?;
        stream.write_all(
            b"lifecycle-json {\"action\":\"request-graceful-shutdown\",\"source\":\"desktop-tui-window-close\"}\n",
        )?;
        stream.flush()
    }

    unsafe extern "system" {
        fn SetConsoleCtrlHandler(
            handler_routine: Option<unsafe extern "system" fn(u32) -> i32>,
            add: i32,
        ) -> i32;
    }
}

#[cfg(not(windows))]
mod platform {
    use std::io;

    pub struct ConsoleCloseGuard;

    pub fn install() -> io::Result<ConsoleCloseGuard> {
        Ok(ConsoleCloseGuard)
    }

    pub fn mark_shutdown_requested() {}
}

pub use platform::ConsoleCloseGuard;

pub fn install() -> io::Result<ConsoleCloseGuard> {
    platform::install()
}

pub fn mark_shutdown_requested() {
    platform::mark_shutdown_requested();
}
