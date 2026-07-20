use serde::{Deserialize, Deserializer};
use serde_json::Value;

#[derive(Debug, Deserialize, Default)]
#[serde(default, rename_all = "camelCase")]
pub struct QueryResponse {
    pub timestamp: Option<String>,
    pub request_id: Option<String>,
    pub command: Option<String>,
    pub success: bool,
    pub message: Option<String>,
    pub result_type: Option<String>,
    pub data: Option<Value>,
    pub error: Option<String>,
}

#[derive(Debug, Clone, Deserialize, Default)]
#[serde(default, rename_all = "camelCase")]
pub struct CommandResult {
    pub timestamp: Option<String>,
    pub request_id: Option<String>,
    pub command: Option<String>,
    pub success: bool,
    pub message: Option<String>,
    pub result_type: Option<String>,
    pub data: Option<Value>,
    pub error: Option<String>,
}

#[derive(Debug, Clone, Copy, Eq, PartialEq)]
pub enum TuiAction {
    RestartCoreApps,
    StartOscGoesBrrr,
    BaseStationsOn,
    BaseStationsOff,
    RestartOscRouter,
    ReloadAutostartApps,
    RestartVrSession,
}

pub const START_STEAMVR_COMMAND: &str = "start-steamvr";
pub const RESTART_VR_SESSION_COMMAND: &str = "restart-vr-session";

#[derive(Debug, Clone, Copy, Eq, PartialEq)]
pub enum ExitOption {
    CloseTuiOnly,
    ExitSupervisorPreserveBaseStations,
    ExitSupervisorNormalCleanup,
    Cancel,
}

impl ExitOption {
    pub const ALL: [Self; 4] = [
        Self::CloseTuiOnly,
        Self::ExitSupervisorPreserveBaseStations,
        Self::ExitSupervisorNormalCleanup,
        Self::Cancel,
    ];

    pub fn from_digit(value: char) -> Option<Self> {
        match value {
            '1' => Some(Self::CloseTuiOnly),
            '2' => Some(Self::ExitSupervisorPreserveBaseStations),
            '3' => Some(Self::ExitSupervisorNormalCleanup),
            '4' => Some(Self::Cancel),
            _ => None,
        }
    }

    pub fn digit(self) -> char {
        match self {
            Self::CloseTuiOnly => '1',
            Self::ExitSupervisorPreserveBaseStations => '2',
            Self::ExitSupervisorNormalCleanup => '3',
            Self::Cancel => '4',
        }
    }

    pub fn display_name(self) -> &'static str {
        match self {
            Self::CloseTuiOnly => "Close TUI only",
            Self::ExitSupervisorPreserveBaseStations => "Exit Supervisor - Keep Base Stations On",
            Self::ExitSupervisorNormalCleanup => "Exit Supervisor - Turn Base Stations Off",
            Self::Cancel => "Cancel",
        }
    }

    pub fn detail(self) -> &'static str {
        match self {
            Self::CloseTuiOnly => "Supervisor continues running.",
            Self::ExitSupervisorPreserveBaseStations => {
                "Monitors restored if Supervisor disabled them. Base stations remain powered on."
            }
            Self::ExitSupervisorNormalCleanup => {
                "Monitors restored if Supervisor disabled them. Normal base-station shutdown runs."
            }
            Self::Cancel => "Return to the dashboard.",
        }
    }

    pub fn lifecycle_mode(self) -> Option<&'static str> {
        match self {
            Self::ExitSupervisorPreserveBaseStations => Some("preserve-base-stations"),
            Self::ExitSupervisorNormalCleanup => Some("normal-cleanup"),
            Self::CloseTuiOnly | Self::Cancel => None,
        }
    }
}

impl TuiAction {
    pub const ALL: [Self; 7] = [
        Self::RestartCoreApps,
        Self::StartOscGoesBrrr,
        Self::BaseStationsOn,
        Self::BaseStationsOff,
        Self::RestartOscRouter,
        Self::ReloadAutostartApps,
        Self::RestartVrSession,
    ];

    pub fn from_digit(value: char) -> Option<Self> {
        match value {
            '1' => Some(Self::RestartCoreApps),
            '2' => Some(Self::StartOscGoesBrrr),
            '3' => Some(Self::BaseStationsOn),
            '4' => Some(Self::BaseStationsOff),
            '5' => Some(Self::RestartOscRouter),
            '6' => Some(Self::ReloadAutostartApps),
            '7' => Some(Self::RestartVrSession),
            _ => None,
        }
    }

    pub fn digit(self) -> char {
        match self {
            Self::RestartCoreApps => '1',
            Self::StartOscGoesBrrr => '2',
            Self::BaseStationsOn => '3',
            Self::BaseStationsOff => '4',
            Self::RestartOscRouter => '5',
            Self::ReloadAutostartApps => '6',
            Self::RestartVrSession => '7',
        }
    }

    pub fn short_label(self) -> &'static str {
        match self {
            Self::RestartCoreApps => "Core",
            Self::StartOscGoesBrrr => "OGB",
            Self::BaseStationsOn => "BS On",
            Self::BaseStationsOff => "BS Off",
            Self::RestartOscRouter => "OSC",
            Self::ReloadAutostartApps => "Autostart",
            Self::RestartVrSession => "SteamVR",
        }
    }

    pub fn command_name(self) -> &'static str {
        match self {
            Self::RestartCoreApps => "restart-core-apps",
            Self::StartOscGoesBrrr => "start-osc-goes-brrr",
            Self::BaseStationsOn => "base-stations-on",
            Self::BaseStationsOff => "base-stations-off",
            Self::RestartOscRouter => "restart-osc-router",
            Self::ReloadAutostartApps => "reload-autostart-apps",
            Self::RestartVrSession => RESTART_VR_SESSION_COMMAND,
        }
    }

    pub fn display_name(self) -> &'static str {
        match self {
            Self::RestartCoreApps => "Restart Core Apps",
            Self::StartOscGoesBrrr => "Start OSCGoesBrrr",
            Self::BaseStationsOn => "Base Stations On",
            Self::BaseStationsOff => "Base Stations Off",
            Self::RestartOscRouter => "Restart OSC Router",
            Self::ReloadAutostartApps => "Reload Autostart Apps",
            Self::RestartVrSession => "SteamVR",
        }
    }

    pub fn from_command_name(command: &str) -> Option<Self> {
        if command.eq_ignore_ascii_case(START_STEAMVR_COMMAND) {
            return Some(Self::RestartVrSession);
        }

        Self::ALL
            .iter()
            .copied()
            .find(|action| action.command_name().eq_ignore_ascii_case(command))
    }
}

#[derive(Debug, Clone, Copy, Eq, PartialEq)]
pub enum SteamVrControlMode {
    Start,
    Restart,
    Starting,
    Restarting,
    Disconnected,
}

impl SteamVrControlMode {
    pub fn command_name(self) -> &'static str {
        match self {
            Self::Start | Self::Starting => START_STEAMVR_COMMAND,
            Self::Restart | Self::Restarting => RESTART_VR_SESSION_COMMAND,
            Self::Disconnected => RESTART_VR_SESSION_COMMAND,
        }
    }

    pub fn badge(self) -> &'static str {
        match self {
            Self::Start => "START",
            Self::Restart => "RESTART",
            Self::Starting | Self::Restarting => "BUSY",
            Self::Disconnected => "DISCONNECTED",
        }
    }

    pub fn detail(self) -> &'static str {
        match self {
            Self::Start => "Start SteamVR",
            Self::Restart => "Restart SteamVR",
            Self::Starting => "Starting SteamVR",
            Self::Restarting => "Restarting SteamVR",
            Self::Disconnected => "Could not contact Supervisor",
        }
    }

    pub fn confirmation_title(self) -> &'static str {
        match self {
            Self::Start | Self::Starting => "Start SteamVR?",
            Self::Restart | Self::Restarting | Self::Disconnected => "Restart SteamVR?",
        }
    }

    pub fn confirmation_body(self) -> &'static [&'static str] {
        match self {
            Self::Start | Self::Starting => &[
                "SteamVR will be started.",
                "VRChat will not be launched automatically.",
            ],
            Self::Restart | Self::Restarting | Self::Disconnected => &[
                "SteamVR will restart.",
                "If VRChat is running, it will close and launch again automatically.",
                "Base stations will remain on and monitors will remain in VR mode.",
            ],
        }
    }
}

#[derive(Debug, Clone, Default)]
pub struct OperationalActionSummary {
    pub operation_id: String,
    pub command: String,
    pub status: String,
    pub progress: String,
    pub result: String,
    pub error: String,
}

#[derive(Debug, Clone, Default)]
pub struct StatusSummary {
    pub app_version: String,
    pub mode: String,
    pub steam_vr: String,
    pub steam_vr_running: Option<bool>,
    pub steam_vr_control_mode: String,
    pub lifecycle: String,
    pub core_apps: String,
    pub base_stations: String,
    pub osc_router: String,
    pub osc_goes_brrr: String,
    pub operator_warning: String,
    pub current_action: Option<OperationalActionSummary>,
    pub last_action_result: Option<OperationalActionSummary>,
}

#[derive(Debug, Clone, Eq, PartialEq)]
pub struct UpdateStatusSummary {
    pub current_version: String,
    pub latest_verified_version: Option<String>,
    pub channel: String,
    pub update_available: bool,
    pub dismissed: bool,
    pub dismissed_version: Option<String>,
    pub last_successful_check_at: Option<String>,
    pub last_error_code: Option<String>,
    pub last_error_summary: Option<String>,
    pub verification_configured: bool,
}

impl UpdateStatusSummary {
    pub fn indicator_version(&self) -> Option<&str> {
        (self.verification_configured && self.update_available && !self.dismissed)
            .then(|| {
                self.latest_verified_version
                    .as_deref()
                    .filter(|version| is_valid_stable_semver(version))
            })
            .flatten()
    }
}

#[derive(Debug, Clone, Deserialize)]
#[serde(default, rename_all = "camelCase")]
pub struct CommandSummary {
    #[serde(deserialize_with = "deserialize_string_or_empty")]
    pub name: String,
    #[serde(deserialize_with = "deserialize_string_or_empty")]
    pub category: String,
    #[serde(deserialize_with = "deserialize_string_or_empty")]
    pub output_kind: String,
    #[serde(deserialize_with = "deserialize_bool_or_false")]
    pub dangerous: bool,
    #[serde(deserialize_with = "deserialize_bool_or_false")]
    pub requires_confirmation: bool,
    #[serde(deserialize_with = "deserialize_bool_or_false")]
    pub action_supported: bool,
    #[serde(
        default = "default_action_safety_category",
        deserialize_with = "deserialize_string_or_dash"
    )]
    pub action_safety_category: String,
    #[serde(deserialize_with = "deserialize_bool_or_false")]
    pub tui_executable: bool,
    #[serde(deserialize_with = "deserialize_string_or_empty")]
    pub blocked_reason: String,
}

impl Default for CommandSummary {
    fn default() -> Self {
        Self {
            name: String::new(),
            category: String::new(),
            output_kind: String::new(),
            dangerous: false,
            requires_confirmation: false,
            action_supported: false,
            action_safety_category: default_action_safety_category(),
            tui_executable: false,
            blocked_reason: String::new(),
        }
    }
}

#[derive(Debug, Clone, Default)]
pub struct LogLine {
    pub timestamp: Option<String>,
    pub message: String,
    pub raw: String,
}

pub fn status_from_response(response: &QueryResponse) -> StatusSummary {
    let data = response.data.as_ref().unwrap_or(&Value::Null);

    StatusSummary {
        app_version: string_value(data, "appVersion"),
        mode: string_value(data, "mode"),
        steam_vr: string_value(data, "steamVr"),
        steam_vr_running: optional_bool_value(data, "steamVrRunning"),
        steam_vr_control_mode: string_value(data, "steamVrControlMode"),
        lifecycle: string_value(data, "lifecycle"),
        core_apps: string_value(data, "coreApps"),
        base_stations: string_value(data, "baseStations"),
        osc_router: string_value(data, "oscRouter"),
        osc_goes_brrr: string_value(data, "oscGoesBrrr"),
        operator_warning: string_value(data, "operatorWarning"),
        current_action: operational_action_value(data, "currentAction"),
        last_action_result: operational_action_value(data, "lastActionResult"),
    }
}

pub fn update_status_from_response(response: &QueryResponse) -> Option<UpdateStatusSummary> {
    if !response.success {
        return None;
    }

    let data = response.data.as_ref()?.as_object()?;
    if required_u64(data, "schemaVersion")? != 1
        || required_string(data, "policy", 32)
            .is_none_or(|value| !matches!(value.as_str(), "Disabled" | "Notify"))
        || required_string(data, "channel", 32).as_deref() != Some("Stable")
    {
        return None;
    }

    let current_version = required_string(data, "currentVersion", 128)?;
    if !is_valid_stable_semver(&current_version) {
        return None;
    }

    let latest_verified_version = optional_string(data, "latestVerifiedVersion", 128)?;
    if latest_verified_version
        .as_deref()
        .is_some_and(|value| !is_valid_stable_semver(value))
    {
        return None;
    }

    let dismissed_version = optional_string(data, "dismissedVersion", 128)?;
    if dismissed_version
        .as_deref()
        .is_some_and(|value| !is_valid_stable_semver(value))
    {
        return None;
    }

    let update_available = required_bool(data, "updateAvailable")?;
    if update_available && latest_verified_version.is_none() {
        return None;
    }

    // Validate the full v1 bridge shape before trusting the presentation booleans.
    optional_string(data, "lastAttemptAt", 64)?;
    required_bool(data, "automaticCheckDue")?;
    required_bool(data, "checkInProgress")?;
    required_nullable_object(data, "operation")?;

    Some(UpdateStatusSummary {
        current_version,
        latest_verified_version,
        channel: "Stable".to_string(),
        update_available,
        dismissed: required_bool(data, "dismissed")?,
        dismissed_version,
        last_successful_check_at: optional_string(data, "lastSuccessfulCheckAt", 64)?,
        last_error_code: optional_string(data, "lastErrorCode", 128)?,
        last_error_summary: optional_string(data, "lastErrorSummary", 512)?,
        verification_configured: required_bool(data, "verificationConfigured")?,
    })
}

fn required_u64(data: &serde_json::Map<String, Value>, key: &str) -> Option<u64> {
    data.get(key)?.as_u64()
}

fn required_bool(data: &serde_json::Map<String, Value>, key: &str) -> Option<bool> {
    data.get(key)?.as_bool()
}

fn required_string(
    data: &serde_json::Map<String, Value>,
    key: &str,
    max_len: usize,
) -> Option<String> {
    let value = data.get(key)?.as_str()?;
    bounded_safe_string(value, max_len).map(str::to_string)
}

fn optional_string(
    data: &serde_json::Map<String, Value>,
    key: &str,
    max_len: usize,
) -> Option<Option<String>> {
    match data.get(key)? {
        Value::Null => Some(None),
        Value::String(value) => bounded_safe_string(value, max_len)
            .map(str::to_string)
            .map(Some),
        _ => None,
    }
}

fn required_nullable_object(data: &serde_json::Map<String, Value>, key: &str) -> Option<()> {
    match data.get(key)? {
        Value::Null | Value::Object(_) => Some(()),
        _ => None,
    }
}

fn bounded_safe_string(value: &str, max_len: usize) -> Option<&str> {
    (!value.is_empty() && value.len() <= max_len && !value.chars().any(char::is_control))
        .then_some(value)
}

fn is_valid_stable_semver(value: &str) -> bool {
    let (version, build) = value
        .split_once('+')
        .map_or((value, None), |(version, build)| (version, Some(build)));
    if version.contains('-') || build.is_some_and(|build| !valid_semver_identifiers(build)) {
        return false;
    }

    let parts = version.split('.').collect::<Vec<_>>();
    parts.len() == 3 && parts.iter().all(|part| valid_numeric_identifier(part))
}

fn valid_numeric_identifier(value: &str) -> bool {
    !value.is_empty()
        && value.bytes().all(|byte| byte.is_ascii_digit())
        && (value == "0" || !value.starts_with('0'))
}

fn valid_semver_identifiers(value: &str) -> bool {
    value.split('.').all(|identifier| {
        !identifier.is_empty()
            && identifier
                .bytes()
                .all(|byte| byte.is_ascii_alphanumeric() || byte == b'-')
    })
}

fn operational_action_value(data: &Value, key: &str) -> Option<OperationalActionSummary> {
    let value = data.get(key)?;
    if value.is_null() {
        return None;
    }

    Some(OperationalActionSummary {
        operation_id: string_value(value, "operationId"),
        command: string_value(value, "command"),
        status: string_value(value, "status"),
        progress: string_value(value, "progress"),
        result: string_value(value, "result"),
        error: string_value(value, "error"),
    })
}

pub fn commands_from_response(response: &QueryResponse) -> Vec<CommandSummary> {
    response
        .data
        .as_ref()
        .and_then(|data| data.get("commands"))
        .and_then(Value::as_array)
        .map(|items| {
            items
                .iter()
                .filter_map(|item| serde_json::from_value::<CommandSummary>(item.clone()).ok())
                .collect()
        })
        .unwrap_or_default()
}

pub fn logs_from_response(response: &QueryResponse) -> Vec<LogLine> {
    response
        .data
        .as_ref()
        .and_then(|data| data.get("lines"))
        .and_then(Value::as_array)
        .map(|items| {
            items
                .iter()
                .map(|item| LogLine {
                    timestamp: item
                        .get("timestamp")
                        .and_then(Value::as_str)
                        .map(str::to_string),
                    message: string_value(item, "message"),
                    raw: string_value(item, "raw"),
                })
                .collect()
        })
        .unwrap_or_default()
}

fn string_value(data: &Value, key: &str) -> String {
    data.get(key)
        .and_then(Value::as_str)
        .unwrap_or("-")
        .to_string()
}

fn optional_bool_value(data: &Value, key: &str) -> Option<bool> {
    data.get(key).and_then(Value::as_bool)
}

fn default_action_safety_category() -> String {
    "-".to_string()
}

fn deserialize_string_or_empty<'de, D>(deserializer: D) -> Result<String, D::Error>
where
    D: Deserializer<'de>,
{
    Ok(Option::<String>::deserialize(deserializer)?.unwrap_or_default())
}

fn deserialize_string_or_dash<'de, D>(deserializer: D) -> Result<String, D::Error>
where
    D: Deserializer<'de>,
{
    Ok(Option::<String>::deserialize(deserializer)?.unwrap_or_else(default_action_safety_category))
}

fn deserialize_bool_or_false<'de, D>(deserializer: D) -> Result<bool, D::Error>
where
    D: Deserializer<'de>,
{
    Ok(Option::<bool>::deserialize(deserializer)?.unwrap_or(false))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn status_summary_reads_authoritative_steamvr_control_fields() {
        let response = QueryResponse {
            success: true,
            data: Some(serde_json::json!({
                "steamVr": "not running",
                "steamVrRunning": false,
                "steamVrControlMode": "start"
            })),
            ..QueryResponse::default()
        };

        let status = status_from_response(&response);

        assert_eq!(status.steam_vr, "not running");
        assert_eq!(status.steam_vr_running, Some(false));
        assert_eq!(status.steam_vr_control_mode, "start");
    }

    fn update_response(overrides: Value) -> QueryResponse {
        let mut data = serde_json::json!({
            "schemaVersion": 1,
            "policy": "Notify",
            "channel": "Stable",
            "currentVersion": "1.3.1",
            "latestVerifiedVersion": "1.4.0",
            "updateAvailable": true,
            "dismissed": false,
            "dismissedVersion": null,
            "lastAttemptAt": "2026-07-21T12:00:00+00:00",
            "lastSuccessfulCheckAt": "2026-07-21T12:00:00+00:00",
            "lastErrorCode": null,
            "lastErrorSummary": null,
            "verificationConfigured": true,
            "automaticCheckDue": false,
            "checkInProgress": false,
            "operation": null
        });
        for (key, value) in overrides.as_object().expect("overrides") {
            data[key] = value.clone();
        }
        QueryResponse {
            success: true,
            data: Some(data),
            ..QueryResponse::default()
        }
    }

    #[test]
    fn verified_update_indicator_trusts_validated_supervisor_booleans() {
        let available = update_status_from_response(&update_response(serde_json::json!({})))
            .expect("valid update status");
        assert_eq!(available.indicator_version(), Some("1.4.0"));

        for overrides in [
            serde_json::json!({ "latestVerifiedVersion": null, "updateAvailable": false }),
            serde_json::json!({ "latestVerifiedVersion": "1.3.1", "updateAvailable": false }),
            serde_json::json!({ "latestVerifiedVersion": "1.2.0", "updateAvailable": false }),
            serde_json::json!({ "dismissed": true, "dismissedVersion": "1.4.0" }),
            serde_json::json!({ "verificationConfigured": false }),
        ] {
            let status = update_status_from_response(&update_response(overrides))
                .expect("valid quiet status");
            assert_eq!(status.indicator_version(), None);
        }
    }

    #[test]
    fn older_dismissal_does_not_hide_new_supervisor_candidate() {
        let status = update_status_from_response(&update_response(serde_json::json!({
            "latestVerifiedVersion": "1.5.0",
            "dismissed": false,
            "dismissedVersion": "1.4.0"
        })))
        .expect("valid newer status");

        assert_eq!(status.indicator_version(), Some("1.5.0"));
        assert_eq!(status.dismissed_version.as_deref(), Some("1.4.0"));
    }

    #[test]
    fn malformed_and_unsupported_update_status_fail_quietly() {
        for overrides in [
            serde_json::json!({ "schemaVersion": 2 }),
            serde_json::json!({ "channel": "Beta" }),
            serde_json::json!({ "latestVerifiedVersion": "v1.4.0" }),
            serde_json::json!({ "updateAvailable": "true" }),
            serde_json::json!({ "updateAvailable": true, "latestVerifiedVersion": null }),
            serde_json::json!({ "lastErrorSummary": "bad\nvalue" }),
        ] {
            assert!(update_status_from_response(&update_response(overrides)).is_none());
        }
    }

    #[test]
    fn action_list_contains_the_retained_actions_and_vr_restart() {
        assert_eq!(TuiAction::ALL.len(), 7);
        assert_eq!(
            TuiAction::ALL.map(TuiAction::command_name),
            [
                "restart-core-apps",
                "start-osc-goes-brrr",
                "base-stations-on",
                "base-stations-off",
                "restart-osc-router",
                "reload-autostart-apps",
                "restart-vr-session",
            ]
        );
        assert_eq!(TuiAction::RestartVrSession.display_name(), "SteamVR");
        assert_eq!(TuiAction::RestartVrSession.short_label(), "SteamVR");
    }

    #[test]
    fn digit_seven_maps_to_vr_session_restart() {
        assert_eq!(
            TuiAction::from_digit('7'),
            Some(TuiAction::RestartVrSession)
        );
        assert_eq!(
            TuiAction::from_command_name("start-steamvr"),
            Some(TuiAction::RestartVrSession)
        );
    }

    #[test]
    fn steamvr_control_modes_keep_explicit_commands() {
        assert_eq!(SteamVrControlMode::Start.command_name(), "start-steamvr");
        assert_eq!(
            SteamVrControlMode::Restart.command_name(),
            "restart-vr-session"
        );
        assert_eq!(SteamVrControlMode::Start.badge(), "START");
        assert_eq!(SteamVrControlMode::Restart.badge(), "RESTART");
        assert_eq!(SteamVrControlMode::Starting.badge(), "BUSY");
    }

    #[test]
    fn exit_options_are_modal_and_digit_mapped() {
        assert_eq!(ExitOption::ALL.len(), 4);
        assert_eq!(ExitOption::from_digit('1'), Some(ExitOption::CloseTuiOnly));
        assert_eq!(
            ExitOption::from_digit('2'),
            Some(ExitOption::ExitSupervisorPreserveBaseStations)
        );
        assert_eq!(
            ExitOption::from_digit('3'),
            Some(ExitOption::ExitSupervisorNormalCleanup)
        );
        assert_eq!(ExitOption::from_digit('4'), Some(ExitOption::Cancel));
        assert_eq!(ExitOption::from_digit('7'), None);
    }
}
