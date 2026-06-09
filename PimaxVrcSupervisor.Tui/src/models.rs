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

#[derive(Debug, Clone, Default)]
pub struct StatusSummary {
    pub app_version: String,
    pub mode: String,
    pub steam_vr: String,
    pub lifecycle: String,
    pub core_apps: String,
    pub base_stations: String,
    pub osc_router: String,
    pub osc_goes_brrr: String,
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
        lifecycle: string_value(data, "lifecycle"),
        core_apps: string_value(data, "coreApps"),
        base_stations: string_value(data, "baseStations"),
        osc_router: string_value(data, "oscRouter"),
        osc_goes_brrr: string_value(data, "oscGoesBrrr"),
    }
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
