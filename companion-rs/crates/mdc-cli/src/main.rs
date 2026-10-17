//! `mdc-cli` — thin subcommand front-end over the shared command registry.
//!
//! Every subcommand dispatches through [`mdc_commands::CommandRegistry`], so
//! the CLI and the GUI cannot drift: UI == CLI == JSON.

#![forbid(unsafe_code)]

use std::process::ExitCode;

use mdc_commands::{default_registry, CommandRegistry};
use serde_json::json;

const USAGE: &str = "\
companion-cli <command> [options]

Commands:
  doctor                 environment + contract health check
  audit                  audit pinned scheduler/decision contracts
  replay --path <file>   replay a local JSONL telemetry file
  invoke <id> [--input <json>]   invoke any registry command
  list                   list registered command ids
  help                   show this help";

fn main() -> ExitCode {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let registry = default_registry();

    let Some(head) = args.first().map(String::as_str) else {
        println!("{USAGE}");
        return ExitCode::SUCCESS;
    };

    match head {
        "help" | "-h" | "--help" => {
            println!("{USAGE}");
            ExitCode::SUCCESS
        }
        "list" => {
            let v = json!({ "commands": registry.describe() });
            println!("{}", serde_json::to_string_pretty(&v).unwrap_or_default());
            ExitCode::SUCCESS
        }
        "doctor" | "audit" => emit(&registry, head, "{}"),
        "replay" => match arg_value(&args, "--path").or_else(|| args.get(1).cloned()) {
            Some(path) => emit(&registry, "replay", &json!({ "path": path }).to_string()),
            None => {
                eprintln!("replay: missing --path <file>");
                ExitCode::from(2)
            }
        },
        "invoke" => match args.get(1) {
            Some(id) => {
                let input = arg_value(&args, "--input").unwrap_or_else(|| "{}".to_string());
                emit(&registry, id, &input)
            }
            None => {
                eprintln!("invoke: missing <command-id>");
                ExitCode::from(2)
            }
        },
        other => {
            eprintln!("unknown command: {other}\n\n{USAGE}");
            ExitCode::from(2)
        }
    }
}

fn arg_value(args: &[String], flag: &str) -> Option<String> {
    let idx = args.iter().position(|a| a == flag)?;
    args.get(idx + 1).cloned()
}

fn emit(registry: &CommandRegistry, id: &str, input_json: &str) -> ExitCode {
    let out = registry.invoke(id, input_json);
    let value: serde_json::Value =
        serde_json::from_str(&out).unwrap_or_else(|_| json!({ "raw": out.clone() }));
    match serde_json::to_string_pretty(&value) {
        Ok(text) => println!("{text}"),
        Err(_) => println!("{out}"),
    }
    if out.contains("\"ok\":false") {
        ExitCode::FAILURE
    } else {
        ExitCode::SUCCESS
    }
}
