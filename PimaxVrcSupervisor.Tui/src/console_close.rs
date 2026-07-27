use std::io;

#[cfg(windows)]
mod platform {
    use std::io;

    const CTRL_CLOSE_EVENT: u32 = 2;
    const CTRL_LOGOFF_EVENT: u32 = 5;
    const CTRL_SHUTDOWN_EVENT: u32 = 6;

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

    pub fn mark_shutdown_requested() {}

    unsafe extern "system" fn console_ctrl_handler(ctrl_type: u32) -> i32 {
        match ctrl_type {
            CTRL_CLOSE_EVENT | CTRL_LOGOFF_EVENT | CTRL_SHUTDOWN_EVENT => {
                // Closing the Terminal UI is a client-only action. Returning zero lets the
                // normal console handler close this process without requesting Supervisor shutdown.
                0
            }
            _ => 0,
        }
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
