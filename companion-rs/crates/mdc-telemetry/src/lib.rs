//! `mdc-telemetry` — local-only JSONL telemetry.
//!
//! Writes one JSON object per line and replays the same file back. There is no
//! network code and no remote sink: telemetry never leaves the machine
//! (`LOCAL_ONLY == true`). The event schema mirrors the C# telemetry events
//! (session start/stop + sent action) at the field level the replay needs.

#![forbid(unsafe_code)]

use std::fs::{File, OpenOptions};
use std::io::{self, BufRead, BufReader, Write};
use std::path::{Path, PathBuf};

use mdc_engine::{KeyStroke, Ms, Slot};
use serde::{Deserialize, Serialize};

/// Telemetry is written to the local filesystem only; never transmitted.
pub const LOCAL_ONLY: bool = true;

/// Current JSONL schema version stamped onto every event.
pub const SCHEMA_VERSION: u32 = 1;

/// One telemetry record (tagged JSON object).
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "snake_case")]
pub enum TelemetryEvent {
    /// Session boundary: written on engine start (`recording: true`) and stop.
    Session {
        schema: u32,
        elapsed_ms: Ms,
        app_version: String,
        protocol_version: u8,
        capacity: u32,
        recording: bool,
        catalog_version: String,
    },
    /// A successful key send.
    Sent {
        schema: u32,
        elapsed_ms: Ms,
        input: String,
        slot: Slot,
        stroke: KeyStroke,
        interval_ms: i64,
        spell_id: i32,
    },
}

impl TelemetryEvent {
    pub fn session(
        elapsed_ms: Ms,
        app_version: impl Into<String>,
        protocol_version: u8,
        capacity: u32,
        recording: bool,
        catalog_version: impl Into<String>,
    ) -> Self {
        TelemetryEvent::Session {
            schema: SCHEMA_VERSION,
            elapsed_ms,
            app_version: app_version.into(),
            protocol_version,
            capacity,
            recording,
            catalog_version: catalog_version.into(),
        }
    }

    pub fn sent(
        elapsed_ms: Ms,
        input: impl Into<String>,
        slot: Slot,
        stroke: KeyStroke,
        interval_ms: i64,
        spell_id: i32,
    ) -> Self {
        TelemetryEvent::Sent {
            schema: SCHEMA_VERSION,
            elapsed_ms,
            input: input.into(),
            slot,
            stroke,
            interval_ms,
            spell_id,
        }
    }
}

/// Append-only JSONL writer.
#[derive(Debug)]
pub struct JsonlRecorder {
    path: PathBuf,
    file: File,
    appended: u64,
}

impl JsonlRecorder {
    /// Creates (truncates) the file and starts a fresh recording.
    pub fn create<P: AsRef<Path>>(path: P) -> io::Result<Self> {
        let path = path.as_ref().to_path_buf();
        let file = File::create(&path)?;
        Ok(Self { path, file, appended: 0 })
    }

    /// Opens an existing file for appending (or creates it).
    pub fn open_append<P: AsRef<Path>>(path: P) -> io::Result<Self> {
        let path = path.as_ref().to_path_buf();
        let file = OpenOptions::new().create(true).append(true).open(&path)?;
        Ok(Self { path, file, appended: 0 })
    }

    /// Serializes one event as a single JSON line.
    pub fn append(&mut self, event: &TelemetryEvent) -> io::Result<()> {
        let line = serde_json::to_string(event)
            .map_err(|e| io::Error::new(io::ErrorKind::InvalidData, e))?;
        self.file.write_all(line.as_bytes())?;
        self.file.write_all(b"\n")?;
        self.appended += 1;
        Ok(())
    }

    pub fn flush(&mut self) -> io::Result<()> {
        self.file.flush()
    }

    pub fn appended(&self) -> u64 {
        self.appended
    }

    pub fn path(&self) -> &Path {
        &self.path
    }
}

/// Reads a JSONL telemetry file back into events (replay).
pub fn replay<P: AsRef<Path>>(path: P) -> io::Result<Vec<TelemetryEvent>> {
    let file = File::open(path)?;
    let reader = BufReader::new(file);
    let mut events = Vec::new();
    for line in reader.lines() {
        let line = line?;
        let trimmed = line.trim();
        if trimmed.is_empty() {
            continue;
        }
        let event = serde_json::from_str(trimmed)
            .map_err(|e| io::Error::new(io::ErrorKind::InvalidData, e))?;
        events.push(event);
    }
    Ok(events)
}

// Compile-time enforcement of the local-only contract: a runtime `assert!` on
// a `const bool` is a no-op that clippy rejects
// (`clippy::assertions_on_constants`), so the invariant is evaluated at compile
// time instead (same guarantee, impossible to ship with `LOCAL_ONLY == false`).
const _: () = assert!(LOCAL_ONLY);

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn round_trips_jsonl() {
        let path = std::env::temp_dir().join("mdc-telemetry-roundtrip.jsonl");
        let _ = std::fs::remove_file(&path);
        {
            let mut rec = JsonlRecorder::create(&path).expect("create");
            rec.append(&TelemetryEvent::session(0, "0.1.0", 5, 128, true, "2026-10-10"))
                .expect("append session");
            rec.append(&TelemetryEvent::sent(
                10,
                "spell",
                Slot::Main,
                KeyStroke::new(0x52),
                0,
                100,
            ))
            .expect("append sent");
            rec.flush().expect("flush");
            assert_eq!(rec.appended(), 2);
        }
        let events = replay(&path).expect("replay");
        assert_eq!(events.len(), 2);
        assert!(matches!(events[0], TelemetryEvent::Session { .. }));
        assert!(matches!(events[1], TelemetryEvent::Sent { .. }));
        let _ = std::fs::remove_file(&path);
    }
}
