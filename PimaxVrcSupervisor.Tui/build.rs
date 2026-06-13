fn main() {
    if std::env::var_os("CARGO_CFG_WINDOWS").is_none() {
        return;
    }

    let mut resource = winresource::WindowsResource::new();
    resource.set_icon("../PimaxVrcSupervisor/Assets/app.ico");

    if let Err(error) = resource.compile() {
        panic!("failed to embed Windows icon resource: {error}");
    }
}
