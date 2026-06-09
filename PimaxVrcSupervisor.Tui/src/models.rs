use serde::Deserialize;
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

#[derive(Debug, Clone, Default)]
pub struct CommandSummary {
    pub name: String,
    pub category: String,
    pub output_kind: String,
    pub dangerous: bool,
    pub requires_confirmation: bool,
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
                .map(|item| CommandSummary {
                    name: string_value(item, "name"),
                    category: string_value(item, "category"),
                    output_kind: string_value(item, "outputKind"),
                    dangerous: item
                        .get("dangerous")
                        .and_then(Value::as_bool)
                        .unwrap_or(false),
                    requires_confirmation: item
                        .get("requiresConfirmation")
                        .and_then(Value::as_bool)
                        .unwrap_or(false),
                })
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
