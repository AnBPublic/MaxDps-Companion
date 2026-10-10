//! `mdc-commands` — the single command registry.
//!
//! One registry is the contract shared by the egui UI, the CLI and (later) the
//! `--control=MCP` surface: every action has a command id, a description and a
//! JSON-in/JSON-out handler. `invoke` never panics: parse and handler failures
//! come back as a JSON error envelope.

#![forbid(unsafe_code)]

use std::collections::BTreeMap;

use mdc_engine::{Slot, FALLBACK_ORDER, SLOT_COUNT};
use mdc_telemetry::{replay, LOCAL_ONLY, SCHEMA_VERSION};
use serde_json::{json, Value};

/// A command handler: JSON value in, JSON result out.
pub type CommandResult = Result<Value, String>;
type Handler = Box<dyn Fn(&Value) -> CommandResult + Send + Sync>;

/// One registered command.
pub struct Command {
    pub id: String,
    pub description: String,
    handler: Handler,
}

impl Command {
    pub fn call(&self, input: &Value) -> CommandResult {
        (self.handler)(input)
    }
}

/// JSON-in/JSON-out command registry keyed by stable command id.
pub struct CommandRegistry {
    commands: BTreeMap<String, Command>,
}

impl Default for CommandRegistry {
    fn default() -> Self {
        Self::new()
    }
}

impl CommandRegistry {
    pub fn new() -> Self {
        Self { commands: BTreeMap::new() }
    }

    /// Registers a command. Re-registering an id replaces it.
    pub fn register<F>(&mut self, id: &str, description: &str, handler: F)
    where
        F: Fn(&Value) -> CommandResult + Send + Sync + 'static,
    {
        self.commands.insert(
            id.to_string(),
            Command {
                id: id.to_string(),
                description: description.to_string(),
                handler: Box::new(handler),
            },
        );
    }

    pub fn contains(&self, id: &str) -> bool {
        self.commands.contains_key(id)
    }

    pub fn ids(&self) -> Vec<String> {
        self.commands.keys().cloned().collect()
    }

    pub fn describe(&self) -> Vec<(String, String)> {
        self.commands
            .values()
            .map(|c| (c.id.clone(), c.description.clone()))
            .collect()
    }

    /// Invokes a command with a JSON string input, returning a JSON string.
    /// Errors (bad JSON, unknown id, handler failure) are returned as JSON too.
    pub fn invoke(&self, id: &str, input_json: &str) -> String {
        let input: Value = match serde_json::from_str(input_json) {
            Ok(v) => v,
            Err(e) => return error_json(id, &format!("invalid json input: {e}")),
        };
        match self.commands.get(id) {
            Some(cmd) => match cmd.call(&input) {
                Ok(value) => json!({ "ok": true, "command": id, "result": value }).to_string(),
                Err(e) => error_json(id, &e),
            },
            None => error_json(id, &format!("unknown command: {id}")),
        }
    }
}

fn error_json(id: &str, message: &str) -> String {
    json!({ "ok": false, "command": id, "error": message }).to_string()
}

fn ok(value: Value) -> CommandResult {
    Ok(value)
}

/// Registry preloaded with the companion's doctor/audit/replay and panel stubs.
pub fn default_registry() -> CommandRegistry {
    let mut reg = CommandRegistry::new();

    reg.register("meta.version", "Report registry + schema versions", |_| {
        ok(json!({
            "command_schema": 1,
            "telemetry_schema": SCHEMA_VERSION,
            "telemetry_local_only": LOCAL_ONLY,
            "slot_count": SLOT_COUNT,
        }))
    });

    reg.register("commands.list", "List every registered command id + description", |_| {
        ok(json!([]))
    });

    reg.register("doctor", "Environment + contract health check", |_| {
        ok(json!({
            "engine_slots": SLOT_COUNT,
            "telemetry_local_only": LOCAL_ONLY,
            "telemetry_schema": SCHEMA_VERSION,
            "cwd": std::env::current_dir().map(|p| p.display().to_string()).unwrap_or_default(),
            "status": "ok",
        }))
    });

    reg.register("audit", "Audit pinned scheduler + decision contracts", |_| {
        let fallback: Vec<&str> = FALLBACK_ORDER.iter().map(|s| s.name()).collect();
        ok(json!({
            "fallback_order": fallback,
            "fallback_pin": "Main,Offensive,Interrupt,Defensive,Consumable,Trinket",
            "slot_count": SLOT_COUNT,
        }))
    });

    reg.register("replay", "Replay a local JSONL telemetry file", |input| {
        let path = input
            .get("path")
            .and_then(Value::as_str)
            .ok_or_else(|| "replay requires {\"path\": \"...\"}".to_string())?;
        let events = replay(path).map_err(|e| e.to_string())?;
        let count = events.len();
        ok(json!({ "path": path, "count": count, "events": events }))
    });

    reg.register("invoke", "Dispatch another command: {\"command\":id,\"input\":{}}", |input| {
        let command = input.get("command").and_then(Value::as_str).unwrap_or("").to_string();
        ok(json!({ "dispatched": command, "note": "CLI performs the real dispatch via registry.invoke" }))
    });

    reg.register("engine.decision", "Evaluate the legacy decision order for a slot set", |input| {
        let slots: Vec<&str> = input
            .get("slots")
            .and_then(Value::as_array)
            .map(|a| a.iter().filter_map(Value::as_str).collect())
            .unwrap_or_default();
        ok(json!({ "requested_slots": slots, "status": "stub" }))
    });

    reg.register(
        "ui.panel.console",
        "Open the Gallant console (runtime transport + log)",
        |_| {
            ok(json!({
                "panel": "console",
                "title": "Console",
                "implemented": true,
                "surface": "runtime transport (start/stop/pause/calibrate/open game) + log",
            }))
        },
    );

    reg.register(
        "ui.panel.settings",
        "Read the live settings.ini into structured settings-panel data",
        |input| {
            let path = input
                .get("path")
                .and_then(Value::as_str)
                .unwrap_or("settings.ini");
            let settings = mdc_settings::Settings::load(path.to_string());
            ok(json!({
                "panel": "settings",
                "title": "Settings",
                "implemented": true,
                "path": settings.path,
                "exists": std::path::Path::new(&settings.path).exists(),
                "process_name": settings.process_name,
                "cell_size": settings.cell_size,
                "poll_interval_ms": settings.poll_interval_ms,
                "min_key_interval_ms": settings.min_key_interval_ms,
                "key_press_ms": settings.key_press_ms,
                "slot_enabled": settings.slot_enabled,
                "combat_only": settings.combat_only,
                "scheduler_enabled": settings.scheduler_enabled,
                "intelligence_enabled": settings.intelligence_enabled,
                "ttk_enabled": settings.ttk_enabled,
                "bnet_path": settings.bnet_path,
                "load_warnings": settings.load_warnings,
            }))
        },
    );

    reg.register(
        "ui.panel.doctor",
        "Environment + contract health check for the Doctor panel",
        |_| {
            let exe_dir = std::env::current_exe()
                .ok()
                .and_then(|exe| exe.parent().map(|dir| dir.display().to_string()))
                .unwrap_or_default();
            ok(json!({
                "panel": "doctor",
                "title": "Doctor",
                "implemented": true,
                "version": "v3.7.7 Gallant",
                "cwd": std::env::current_dir().map(|p| p.display().to_string()).unwrap_or_default(),
                "exe_dir": exe_dir,
                "protocol_versions": [5, 4, 1],
                "slot_count": SLOT_COUNT,
                "telemetry_schema": SCHEMA_VERSION,
                "telemetry_local_only": LOCAL_ONLY,
            }))
        },
    );

    reg.register(
        "ui.panel.class_browser",
        "List the 8 action slots + enable state for the Class Browser panel",
        |_| {
            const SLOTS: [&str; 8] = [
                "Main", "Offensive", "Defensive", "Consumable", "Trinket", "Interrupt",
                "Mobility", "SelfHeal",
            ];
            let slots: Vec<Value> = SLOTS
                .iter()
                .enumerate()
                .map(|(i, name)| json!({ "index": i + 1, "name": name }))
                .collect();
            ok(json!({
                "panel": "class_browser",
                "title": "Class Browser",
                "implemented": true,
                "class_registry": false,
                "detail": "mdc-engine exposes the 8-slot contract only; class knowledge is not ported yet.",
                "slots": slots,
            }))
        },
    );

    reg.register(
        "ui.panel.telemetry",
        "Local-only JSONL recorder + replay contract for the Telemetry panel",
        |_| {
            ok(json!({
                "panel": "telemetry",
                "title": "Telemetry",
                "implemented": true,
                "local_only": LOCAL_ONLY,
                "schema": SCHEMA_VERSION,
                "recorder": "JsonlRecorder",
                "replay": "replay",
            }))
        },
    );

    // Fill in the concrete list now that every id exists.
    let listing: Vec<Value> = reg
        .describe()
        .into_iter()
        .map(|(id, d)| json!({ "id": id, "description": d }))
        .collect();
    reg.register("commands.list", "List every registered command id + description", move |_| {
        ok(Value::Array(listing.clone()))
    });

    reg
}

/// Convenience: slot order used by the audit contract.
pub fn canonical_fallback_order() -> [Slot; 6] {
    FALLBACK_ORDER
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn invoke_returns_json_envelope() {
        let reg = default_registry();
        let out = reg.invoke("meta.version", "{}");
        let v: Value = serde_json::from_str(&out).unwrap();
        assert_eq!(v["ok"], true);
        assert_eq!(v["command"], "meta.version");
    }

    #[test]
    fn unknown_command_is_json_error() {
        let reg = default_registry();
        let v: Value = serde_json::from_str(&reg.invoke("nope", "{}")).unwrap();
        assert_eq!(v["ok"], false);
    }

    #[test]
    fn list_is_populated() {
        let reg = default_registry();
        for id in ["doctor", "audit", "replay", "invoke", "commands.list"] {
            assert!(reg.contains(id), "missing {id}");
        }
        let v: Value = serde_json::from_str(&reg.invoke("commands.list", "{}")).unwrap();
        assert!(v["result"].as_array().map(|a| a.len() >= 5).unwrap_or(false));
    }

    #[test]
    fn panel_commands_return_real_structured_data() {
        let reg = default_registry();
        for id in [
            "ui.panel.console",
            "ui.panel.settings",
            "ui.panel.doctor",
            "ui.panel.class_browser",
            "ui.panel.telemetry",
        ] {
            let v: Value = serde_json::from_str(&reg.invoke(id, "{}")).unwrap();
            assert_eq!(v["ok"], true, "{id} failed");
            assert_eq!(v["result"]["implemented"], true, "{id} not implemented");
            assert!(v["result"].get("status").is_none(), "{id} is still a stub");
            assert!(v["result"]["title"].is_string(), "{id} missing title");
        }
        // The class browser lists the 8 wire slots.
        let v: Value = serde_json::from_str(&reg.invoke("ui.panel.class_browser", "{}")).unwrap();
        assert_eq!(v["result"]["slots"].as_array().map(Vec::len), Some(8));
    }
}
