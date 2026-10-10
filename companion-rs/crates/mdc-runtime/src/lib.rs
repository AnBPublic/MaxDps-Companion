//! `mdc-runtime` — the engine runtime that sits between the platform backend and
//! a UI. It owns a background worker thread (the engine), while the UI talks to
//! it through a command channel and reads a shared [`Snapshot`].
//!
//! Layout (plan `docs/plans/2026-10-10-gallant-parity.md` §4/§6):
//! - [`Runtime`] — the UI-facing handle: `start` / `stop` / `toggle_pause` /
//!   `calibrate` / `open_game` + [`Runtime::snapshot`].
//! - a private `Worker` owns the [`RotationEngine`], the platform backend, the
//!   decode adapter and the shared log/state; it ticks on `Instant` deadlines
//!   (`33ms` active, `8ms` while idle re-acquiring).
//! - [`sampler`] builds the per-cell colours; [`adapter`] maps the protocol
//!   frame to the engine snapshot.
//!
//! Error-path contract (mirrors the C# reference):
//! - `find_wow` fails at Start -> log "Start WoW first" and stay `Stopped`.
//! - Start while already live -> ignored.
//! - Calibrate while `Running` -> log "Stop the engine first".
//! - empty `[Launch] BNetPath` -> log the auto-detect hint.
//! - window lost mid-run -> hold and log once; decode error -> log and continue;
//!   `NotForeground` -> refuse the input, log once and do not retry.

#![forbid(unsafe_code)]

pub mod adapter;
pub mod sampler;

use std::collections::VecDeque;
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc::{self, Receiver, RecvTimeoutError, Sender};
use std::sync::{Arc, Mutex};
use std::thread::JoinHandle;
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use mdc_engine::{
    Clock, DefaultKnowledge, EngineConfig, Ms, RotationEngine, SchedulePlan, ScheduledAction,
    SLOT_COUNT,
};
use mdc_platform::{
    Key, MouseButton, MouseEvent, Platform, PlatformError, Rect, WindowId, WHEEL_DELTA,
};
use mdc_protocol::{BridgeFrame, CELL_COUNT_EXT3, Color, ColorProfile, DecodeError};
use mdc_settings::Settings;
use mdc_telemetry::{JsonlRecorder, TelemetryEvent};

/// Log ring capacity (`VecDeque`); oldest entry is dropped past this.
pub const LOG_CAPACITY: usize = 200;

/// Configured active tick interval (`33ms`) in the plan; the settings value is
/// the source when it is set, this is the floor.
pub const ACTIVE_POLL_MS: u32 = 33;
/// Idle re-acquire tick interval (`8ms`): used while live but no frame decodes.
pub const IDLE_POLL_MS: u32 = 8;

const APP_VERSION: &str = "0.0.0";
const PROTOCOL_VERSION: u8 = 5;

/// Companion run-state machine (`docs/plans/2026-10-10-gallant-parity.md` §3).
#[derive(Clone, Copy, PartialEq, Eq, Debug, Default)]
pub enum RunState {
    /// No worker activity; Start/Calibrate are available.
    #[default]
    Stopped,
    /// A one-shot capture probe is running.
    Calibrating,
    /// Engine sampling and input are live.
    Running,
    /// Engine live but holding all input.
    Paused,
}

impl RunState {
    /// Stable label for the status pill.
    pub fn label(self) -> &'static str {
        match self {
            RunState::Stopped => "Stopped",
            RunState::Calibrating => "Calibrating",
            RunState::Running => "Running",
            RunState::Paused => "Paused",
        }
    }

    /// True for every state except `Stopped`.
    pub fn is_live(self) -> bool {
        !matches!(self, RunState::Stopped)
    }

    /// True only for `Stopped`.
    pub fn is_stopped(self) -> bool {
        matches!(self, RunState::Stopped)
    }
}

/// One timestamped UI log entry (`HH:mm:ss`, UTC).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct LogLine {
    /// `HH:mm:ss` wall-clock stamp.
    pub ts: String,
    /// Rendered message.
    pub text: String,
}

/// The UI-visible view of the worker: state, sample rate, hero line, current
/// recommendation and the rolling log.
#[derive(Clone, Debug, PartialEq, Eq, Default)]
pub struct Snapshot {
    /// Current run state.
    pub run_state: RunState,
    /// Integer samples/sec (`1000 / poll_ms`).
    pub fps: u32,
    /// Hero state line.
    pub hero: String,
    /// Current recommendation or hold reason.
    pub now: String,
    /// Slot name the scheduler last selected (`None` while stopped/holding).
    pub suggested_slot: Option<String>,
    /// Rolling log, oldest first.
    pub log: Vec<LogLine>,
}

/// Commands the UI sends to the worker thread.
#[derive(Clone, Copy, Debug)]
enum Command {
    Start,
    Stop,
    TogglePause,
    Calibrate,
    OpenGame,
    Shutdown,
}

#[derive(Default)]
struct Shared {
    run_state: RunState,
    fps: u32,
    hero: String,
    now: String,
    suggested_slot: Option<String>,
    log: VecDeque<LogLine>,
}

impl Shared {
    fn push_log(&mut self, line: LogLine) {
        if self.log.len() >= LOG_CAPACITY {
            self.log.pop_front();
        }
        self.log.push_back(line);
    }

    fn snapshot(&self) -> Snapshot {
        Snapshot {
            run_state: self.run_state,
            fps: self.fps,
            hero: self.hero.clone(),
            now: self.now.clone(),
            suggested_slot: self.suggested_slot.clone(),
            log: self.log.iter().cloned().collect(),
        }
    }
}

/// Launches or focuses the game client for `open_game`. Injectable so tests can
/// assert the requested path without spawning a real process.
pub trait Launcher: Send {
    /// Launches/focuses the client. `Ok` carries the status message to log.
    fn open(&self, bnet_path: &str) -> Result<String, String>;
}

/// Default launcher: spawn the Battle.net launcher executable.
#[derive(Debug, Default, Clone, Copy)]
pub struct ProcessLauncher;

impl Launcher for ProcessLauncher {
    fn open(&self, bnet_path: &str) -> Result<String, String> {
        match std::process::Command::new(bnet_path).spawn() {
            Ok(_) => Ok(format!("launched {bnet_path}")),
            Err(e) => Err(e.to_string()),
        }
    }
}

/// Monotonic clock for the production worker, backed by [`Instant`].
#[derive(Debug, Clone)]
struct MonotonicClock {
    start: Instant,
}

impl MonotonicClock {
    fn new() -> Self {
        Self { start: Instant::now() }
    }
}

impl Clock for MonotonicClock {
    fn now_ms(&self) -> Ms {
        self.start.elapsed().as_millis() as i64
    }
}

/// Production worker: owns the engine and talks to the shared snapshot.
struct Worker<P: Platform, C: Clock + Clone> {
    platform: P,
    clock: C,
    engine: RotationEngine<C>,
    settings: Settings,
    profile: ColorProfile,
    shared: Arc<Mutex<Shared>>,
    launcher: Box<dyn Launcher>,
    recorder: Option<JsonlRecorder>,

    state: RunState,
    window: WindowId,
    idle: bool,

    last_decode_err: Option<DecodeError>,
    waiting_logged: bool,
    not_foreground_logged: bool,
}

impl<P: Platform, C: Clock + Clone> Worker<P, C> {
    fn new(
        platform: P,
        clock: C,
        settings: Settings,
        shared: Arc<Mutex<Shared>>,
        launcher: Box<dyn Launcher>,
    ) -> Self {
        let config = engine_config(&settings);
        let profile = color_profile(&settings);
        let engine = RotationEngine::new(clock.clone(), config);
        Self {
            platform,
            clock,
            engine,
            profile,
            settings,
            shared,
            launcher,
            recorder: None,
            state: RunState::Stopped,
            window: WindowId::NONE,
            idle: true,
            last_decode_err: None,
            waiting_logged: false,
            not_foreground_logged: false,
        }
    }

    fn state(&self) -> RunState {
        self.state
    }

    fn poll_interval(&self) -> u32 {
        if self.idle && self.state.is_live() {
            IDLE_POLL_MS
        } else {
            let configured = self.settings.poll_interval_ms.max(1) as u32;
            if configured == 0 {
                ACTIVE_POLL_MS
            } else {
                configured
            }
        }
    }

    fn fps(&self) -> u32 {
        1000 / self.poll_interval().max(1)
    }

    fn handle(&mut self, cmd: Command) {
        match cmd {
            Command::Start => self.start(),
            Command::Stop => self.stop(),
            Command::TogglePause => self.toggle_pause(),
            Command::Calibrate => self.calibrate(),
            Command::OpenGame => self.open_game(),
            Command::Shutdown => {}
        }
    }

    fn publish_initial(&self) {
        let fps = self.fps();
        if let Ok(mut g) = self.shared.lock() {
            g.run_state = self.state;
            g.fps = fps;
            if g.hero.is_empty() {
                g.hero = RunState::Stopped.label().to_string();
            }
        }
    }

    fn set_state(&mut self, state: RunState) {
        self.state = state;
        let fps = self.fps();
        if let Ok(mut g) = self.shared.lock() {
            g.run_state = state;
            g.fps = fps;
        }
    }

    fn set_hero_now(&self, hero: String, now: String) {
        if let Ok(mut g) = self.shared.lock() {
            g.hero = hero;
            g.now = now;
            g.suggested_slot = None;
        }
    }

    fn set_suggested_slot(&self, slot: Option<String>) {
        if let Ok(mut g) = self.shared.lock() {
            g.suggested_slot = slot;
        }
    }

    fn log(&self, text: impl Into<String>) {
        let line = LogLine { ts: now_stamp(), text: text.into() };
        if let Ok(mut g) = self.shared.lock() {
            g.push_log(line);
        }
    }

    // ---- commands ----

    fn start(&mut self) {
        if self.state.is_live() {
            self.log("Start ignored: the engine is already running.");
            return;
        }
        let name = self.settings.process_name.clone();
        match self.platform.find_wow(&name) {
            Ok(window) => self.window = window,
            Err(e) => {
                self.log(format!("Start WoW first - no window for '{name}'."));
                self.log(format!("find game window failed: {e}"));
                self.set_state(RunState::Stopped);
                return;
            }
        }
        self.engine = RotationEngine::new(self.clock.clone(), engine_config(&self.settings));
        self.idle = true;
        self.last_decode_err = None;
        self.waiting_logged = false;
        self.not_foreground_logged = false;
        self.set_state(RunState::Running);
        self.set_hero_now(RunState::Running.label().to_string(), String::new());
        self.log("Engine started.");
        self.begin_telemetry();
    }

    fn stop(&mut self) {
        if self.state.is_stopped() {
            return;
        }
        self.engine.reset();
        self.end_telemetry();
        self.set_state(RunState::Stopped);
        self.set_hero_now(RunState::Stopped.label().to_string(), String::new());
        self.log("Engine stopped.");
    }

    fn toggle_pause(&mut self) {
        match self.state {
            RunState::Running => {
                self.set_state(RunState::Paused);
                self.set_hero_now(RunState::Paused.label().to_string(), String::new());
                self.log("Paused.");
            }
            RunState::Paused => {
                self.set_state(RunState::Running);
                self.set_hero_now(RunState::Running.label().to_string(), String::new());
                self.log("Running.");
            }
            RunState::Stopped => self.start(),
            RunState::Calibrating => {}
        }
    }

    fn calibrate(&mut self) {
        if self.state == RunState::Running {
            self.log("Stop the engine first, then Recalibrate.");
            return;
        }
        if self.state == RunState::Calibrating {
            return;
        }
        self.set_state(RunState::Calibrating);
        self.set_hero_now(RunState::Calibrating.label().to_string(), String::new());
        self.log("Calibrating...");
    }

    fn open_game(&mut self) {
        let override_path = self.settings.bnet_path.trim().to_string();
        let target = if override_path.is_empty() {
            self.log(
                "BNetPath not set - auto-detecting Battle.net (set [Launch] BNetPath in \
                 settings.ini to override).",
            );
            match auto_detect_bnet() {
                Some(path) => path,
                None => {
                    self.log("Battle.net not found - set [Launch] BNetPath in settings.ini.");
                    return;
                }
            }
        } else {
            override_path
        };
        match self.launcher.open(&target) {
            Ok(message) => self.log(message),
            Err(e) => self.log(format!("launch failed: {e}")),
        }
    }

    // ---- one tick ----

    fn step(&mut self) {
        match self.state {
            RunState::Stopped => return,
            RunState::Calibrating => {
                self.do_calibrate();
                return;
            }
            RunState::Running | RunState::Paused => {}
        }

        let name = self.settings.process_name.clone();
        let window = match self.platform.find_wow(&name) {
            Ok(window) => window,
            Err(e) => {
                self.idle = true;
                self.report_waiting(&e.to_string());
                return;
            }
        };
        self.window = window;
        self.waiting_logged = false;

        let region = self.capture_region();
        let frame = match self.platform.capture(window, region) {
            Ok(frame) => frame,
            Err(PlatformError::WindowNotFound(m)) => {
                self.idle = true;
                self.report_waiting(&m);
                return;
            }
            Err(e) => {
                self.idle = true;
                self.log(format!("capture failed: {e}"));
                return;
            }
        };

        let cell_size = self.settings.cell_size.max(1) as u32;
        let cells = sampler::sample_cells(&frame, cell_size, CELL_COUNT_EXT3);
        let decoded = match decode_any(&cells, &self.profile) {
            Ok(frame) => frame,
            Err(e) => {
                self.idle = true;
                self.log_decode_err(e);
                return;
            }
        };

        self.idle = false;
        self.last_decode_err = None;
        let fps = self.fps();
        let label = protocol_state_label(decoded.state);
        let snapshot = adapter::to_snapshot(&decoded);
        let cast = adapter::cast_state(&decoded);
        let out_of_combat = !self.settings.combat_only;

        let (actions, now, head) = {
            let plan = self.engine.tick(Some(&snapshot), None, &DefaultKnowledge, cast, out_of_combat);
            (
                plan.actions.clone(),
                describe_plan(plan),
                plan.head.map(|slot| slot.name().to_string()),
            )
        };
        self.set_hero_now(format!("{label} · {fps} fps"), now);
        self.set_suggested_slot(head);
        for action in &actions {
            self.dispatch(action);
        }
    }

    fn do_calibrate(&mut self) {
        let name = self.settings.process_name.clone();
        let window = match self.platform.find_wow(&name) {
            Ok(window) => window,
            Err(e) => {
                self.log(format!("Start WoW first - no window for '{name}'."));
                self.log(format!("find game window failed: {e}"));
                self.finish_calibrate();
                return;
            }
        };
        self.window = window;
        self.engine = RotationEngine::new(self.clock.clone(), engine_config(&self.settings));

        let region = self.capture_region();
        match self.platform.capture(window, region) {
            Ok(frame) => {
                let cell_size = self.settings.cell_size.max(1) as u32;
                let cells = sampler::sample_cells(&frame, cell_size, CELL_COUNT_EXT3);
                match decode_any(&cells, &self.profile) {
                    Ok(f) => self.log(format!(
                        "calibration sample ok: v{} {}",
                        f.version,
                        protocol_state_label(f.state)
                    )),
                    Err(_) => self.log("calibration sample: no decodable bridge block (check /mdb on)"),
                }
            }
            Err(e) => self.log(format!("calibration capture failed: {e}")),
        }
        self.finish_calibrate();
        self.log("calibration finished (live colour learning stays OWED until a retail run).");
    }

    fn finish_calibrate(&mut self) {
        self.set_state(RunState::Stopped);
        self.set_hero_now(RunState::Stopped.label().to_string(), String::new());
    }

    fn capture_region(&self) -> Rect {
        let cell_size = self.settings.cell_size.max(1);
        Rect::new(
            self.settings.offset_x,
            self.settings.offset_y,
            cell_size.saturating_mul(CELL_COUNT_EXT3 as i32),
            cell_size,
        )
    }

    fn report_waiting(&mut self, detail: &str) {
        if !self.waiting_logged {
            self.waiting_logged = true;
            let name = self.settings.process_name.clone();
            self.log(format!("holding - waiting for '{name}' ({detail})"));
        }
    }

    fn log_decode_err(&mut self, err: DecodeError) {
        if self.last_decode_err != Some(err) {
            self.last_decode_err = Some(err);
            self.log(format!("decode failed: {err:?} - no bridge block"));
        }
    }

    // ---- input dispatch ----

    fn dispatch(&mut self, action: &ScheduledAction) {
        let virtual_key = action.stroke.virtual_key as u8;
        let window = self.window;

        if let Some(events) = mouse_events(virtual_key) {
            for event in &events {
                match self.platform.post_mouse(window, event) {
                    Ok(()) => {}
                    Err(PlatformError::NotForeground(m)) => {
                        self.report_not_foreground(&m);
                        return;
                    }
                    Err(e) => {
                        self.log(format!("mouse post failed: {e}"));
                        return;
                    }
                }
            }
            self.note_sent(action);
            return;
        }

        if !self.settings.allow_background_keys {
            match self.platform.is_foreground(window) {
                Ok(true) => {}
                Ok(false) => {
                    self.report_not_foreground("keyboard input requires the game in front");
                    return;
                }
                Err(e) => {
                    self.log(format!("foreground check failed: {e}"));
                    return;
                }
            }
        }

        let key = Key {
            virtual_key,
            ctrl: action.stroke.ctrl,
            alt: action.stroke.alt,
            shift: action.stroke.shift,
        };
        let hold = self.settings.key_press_ms.max(0) as u32;
        match self.platform.post_key(window, &key, hold) {
            Ok(()) => self.note_sent(action),
            Err(PlatformError::NotForeground(m)) => self.report_not_foreground(&m),
            Err(e) => self.log(format!("key post failed: {e}")),
        }
    }

    fn note_sent(&mut self, action: &ScheduledAction) {
        self.engine.note_sent(action.slot, action.stroke, action.spell_id);
        self.not_foreground_logged = false;
        self.record_sent(action);
    }

    fn report_not_foreground(&mut self, detail: &str) {
        if !self.not_foreground_logged {
            self.not_foreground_logged = true;
            self.log(format!(
                "input refused - target is not the foreground window ({detail}); no retry"
            ));
        }
    }

    // ---- telemetry (local-only, opt-in) ----

    fn begin_telemetry(&mut self) {
        if !self.settings.telemetry_enabled {
            return;
        }
        let path = telemetry_path(&self.settings);
        match JsonlRecorder::open_append(&path) {
            Ok(mut recorder) => {
                let event = TelemetryEvent::session(
                    self.engine.clock().now_ms(),
                    APP_VERSION,
                    PROTOCOL_VERSION,
                    self.settings.telemetry_capacity.max(0) as u32,
                    true,
                    "",
                );
                if let Err(e) = recorder.append(&event) {
                    self.log(format!("telemetry write failed: {e}"));
                }
                self.recorder = Some(recorder);
            }
            Err(e) => self.log(format!("telemetry open failed: {e}")),
        }
    }

    fn end_telemetry(&mut self) {
        if let Some(mut recorder) = self.recorder.take() {
            let event = TelemetryEvent::session(
                self.engine.clock().now_ms(),
                APP_VERSION,
                PROTOCOL_VERSION,
                self.settings.telemetry_capacity.max(0) as u32,
                false,
                "",
            );
            let _ = recorder.append(&event);
            let _ = recorder.flush();
        }
    }

    fn record_sent(&mut self, action: &ScheduledAction) {
        if let Some(recorder) = self.recorder.as_mut() {
            let event = TelemetryEvent::sent(
                self.engine.clock().now_ms(),
                "spell",
                action.slot,
                action.stroke,
                0,
                action.spell_id,
            );
            let _ = recorder.append(&event);
        }
    }
}

/// UI-facing runtime handle. Dropping it stops and joins the worker.
pub struct Runtime {
    tx: Sender<Command>,
    shared: Arc<Mutex<Shared>>,
    shutdown: Arc<AtomicBool>,
    handle: Option<JoinHandle<()>>,
}

impl Runtime {
    /// Spawns the worker with the process launcher.
    pub fn new<P>(platform: P, settings: Settings) -> Self
    where
        P: Platform + Send + 'static,
    {
        Self::with_launcher(platform, settings, Box::new(ProcessLauncher))
    }

    /// Spawns the worker with an injected launcher (tests / future shells).
    pub fn with_launcher<P>(platform: P, settings: Settings, launcher: Box<dyn Launcher>) -> Self
    where
        P: Platform + Send + 'static,
    {
        let shared = Arc::new(Mutex::new(Shared::default()));
        let (tx, rx) = mpsc::channel::<Command>();
        let shutdown = Arc::new(AtomicBool::new(false));
        let worker_shared = Arc::clone(&shared);
        let worker_shutdown = Arc::clone(&shutdown);
        let handle = std::thread::Builder::new()
            .name("mdc-runtime".to_string())
            .spawn(move || {
                worker_loop::<P>(platform, settings, worker_shared, worker_shutdown, launcher, rx);
            })
            .expect("spawn mdc-runtime worker thread");
        Self { tx, shared, shutdown, handle: Some(handle) }
    }

    fn send_command(&self, cmd: Command) {
        let _ = self.tx.send(cmd);
    }

    /// Requests Start (ignored while already live).
    pub fn start(&self) {
        self.send_command(Command::Start);
    }

    /// Requests Stop.
    pub fn stop(&self) {
        self.send_command(Command::Stop);
    }

    /// Toggles Running<->Paused, or starts when Stopped.
    pub fn toggle_pause(&self) {
        self.send_command(Command::TogglePause);
    }

    /// Requests a calibration probe (refused while Running).
    pub fn calibrate(&self) {
        self.send_command(Command::Calibrate);
    }

    /// Launches/focuses the client.
    pub fn open_game(&self) {
        self.send_command(Command::OpenGame);
    }

    /// Reads the current UI snapshot.
    pub fn snapshot(&self) -> Snapshot {
        match self.shared.lock() {
            Ok(guard) => guard.snapshot(),
            Err(poisoned) => poisoned.into_inner().snapshot(),
        }
    }

    /// Stops the worker and waits for it to exit.
    pub fn shutdown(&mut self) {
        self.shutdown.store(true, Ordering::SeqCst);
        let _ = self.tx.send(Command::Shutdown);
        if let Some(handle) = self.handle.take() {
            let _ = handle.join();
        }
    }
}

impl Drop for Runtime {
    fn drop(&mut self) {
        self.shutdown();
    }
}

fn worker_loop<P: Platform>(
    platform: P,
    settings: Settings,
    shared: Arc<Mutex<Shared>>,
    shutdown: Arc<AtomicBool>,
    launcher: Box<dyn Launcher>,
    rx: Receiver<Command>,
) {
    let mut worker =
        Worker::<P, MonotonicClock>::new(platform, MonotonicClock::new(), settings, shared, launcher);
    worker.publish_initial();

    loop {
        if shutdown.load(Ordering::SeqCst) {
            break;
        }
        if worker.state().is_stopped() {
            match rx.recv() {
                Ok(Command::Shutdown) | Err(_) => break,
                Ok(cmd) => worker.handle(cmd),
            }
            continue;
        }

        worker.step();
        let interval = worker.poll_interval();
        let deadline = Instant::now() + Duration::from_millis(u64::from(interval));
        let mut stop = false;
        while !stop {
            let remaining = deadline.saturating_duration_since(Instant::now());
            if remaining.is_zero() {
                break;
            }
            match rx.recv_timeout(remaining) {
                Ok(Command::Shutdown) | Err(RecvTimeoutError::Disconnected) => {
                    worker.end_telemetry();
                    return;
                }
                Ok(cmd) => {
                    worker.handle(cmd);
                    if worker.state().is_stopped() {
                        stop = true;
                    }
                }
                Err(RecvTimeoutError::Timeout) => break,
            }
        }
    }
    worker.end_telemetry();
}

fn engine_config(settings: &Settings) -> EngineConfig {
    // settings.slot_enabled is wire order (Main, Offensive, Defensive,
    // Consumable, Trinket, Interrupt, Mobility, SelfHeal); the engine orders by
    // decision rank. Map engine index -> settings index.
    const SETTINGS_INDEX: [usize; SLOT_COUNT] = [5, 2, 0, 1, 3, 4, 6, 7];
    let mut slot_enabled = [true; SLOT_COUNT];
    for (engine_index, settings_index) in SETTINGS_INDEX.iter().enumerate() {
        slot_enabled[engine_index] = settings.slot_enabled[*settings_index];
    }
    EngineConfig {
        scheduler_enabled: settings.scheduler_enabled,
        intelligence_enabled: settings.intelligence_enabled,
        slot_enabled,
        min_key_interval_ms: settings.min_key_interval_ms,
        stale_after_ms: settings.intelligence_stale_after_ms,
        heartbeat_timeout_ms: settings.scheduler_heartbeat_timeout_ms,
        repeat_suppress_ms: settings.scheduler_repeat_suppress_ms,
        combat_only: settings.combat_only,
    }
}

fn color_profile(settings: &Settings) -> ColorProfile {
    let c = &settings.color;
    ColorProfile {
        magic_r: c.magic_r,
        magic_g: c.magic_g,
        magic_b: c.magic_b,
        black: c.black,
        white: c.white,
        tolerance: c.tolerance,
        learned: !c.learned_at.is_empty(),
    }
}

/// Decode with protocol v5/v4/v1 auto-detection: try the full (widest) capture,
/// then the v4 and v1 windows trimmed out of the same capture.
fn decode_any(cells: &[Color], profile: &ColorProfile) -> Result<BridgeFrame, DecodeError> {
    match mdc_protocol::decode_with_profile(cells, Some(profile)) {
        Ok(frame) => Ok(frame),
        Err(full_error) => {
            let v4 = mdc_protocol::trim_to_v4(cells);
            if let Ok(frame) = mdc_protocol::decode_with_profile(&v4, Some(profile)) {
                return Ok(frame);
            }
            let v1 = mdc_protocol::trim_to_v1(cells);
            if let Ok(frame) = mdc_protocol::decode_with_profile(&v1, Some(profile)) {
                return Ok(frame);
            }
            Err(full_error)
        }
    }
}

fn describe_plan(plan: &SchedulePlan) -> String {
    match plan.actions.first() {
        Some(action) => format!("cast {} ({:?})", action.stroke.describe(), action.reason),
        None => format!("hold: {:?}", plan.reason),
    }
}

fn protocol_state_label(state: mdc_protocol::BridgeState) -> &'static str {
    use mdc_protocol::BridgeState;
    match state {
        BridgeState::Idle => "Idle",
        BridgeState::Active => "Active",
        BridgeState::Paused => "Paused",
        BridgeState::NeedTarget => "NeedTarget",
        BridgeState::NeedInteract => "NeedInteract",
    }
}

/// Maps wire virtual-key mouse codes (`KeySender`) to `SendInput` events.
fn mouse_events(virtual_key: u8) -> Option<Vec<MouseEvent>> {
    let button = match virtual_key {
        0x01 => Some(MouseButton::Left),
        0x02 => Some(MouseButton::Right),
        0x04 => Some(MouseButton::Middle),
        0x05 => Some(MouseButton::X1),
        0x06 => Some(MouseButton::X2),
        _ => None,
    };
    if let Some(button) = button {
        return Some(vec![MouseEvent::Down(button), MouseEvent::Up(button)]);
    }
    match virtual_key {
        0x07 => Some(vec![MouseEvent::Wheel { delta: WHEEL_DELTA }]),
        0x0B => Some(vec![MouseEvent::Wheel { delta: -WHEEL_DELTA }]),
        _ => None,
    }
}

/// UTC `HH:mm:ss` from `SystemTime` (no timezone crate in the workspace).
fn now_stamp() -> String {
    let seconds = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0);
    hhmmss(seconds % 86_400)
}

fn hhmmss(seconds_of_day: u64) -> String {
    let h = seconds_of_day / 3600;
    let m = (seconds_of_day % 3600) / 60;
    let s = seconds_of_day % 60;
    format!("{h:02}:{m:02}:{s:02}")
}

fn telemetry_path(settings: &Settings) -> PathBuf {
    let path = std::path::Path::new(&settings.path);
    let dir = match path.parent() {
        Some(parent) if !parent.as_os_str().is_empty() => parent.to_path_buf(),
        _ => PathBuf::from("."),
    };
    dir.join("telemetry-runtime.jsonl")
}

#[cfg(target_os = "windows")]
fn auto_detect_bnet() -> Option<String> {
    const CANDIDATES: [&str; 2] = [
        r"C:\Program Files (x86)\Battle.net\Battle.net Launcher.exe",
        r"C:\Program Files\Battle.net\Battle.net Launcher.exe",
    ];
    for candidate in CANDIDATES {
        if std::path::Path::new(candidate).exists() {
            return Some(candidate.to_string());
        }
    }
    None
}

#[cfg(not(target_os = "windows"))]
fn auto_detect_bnet() -> Option<String> {
    None
}

#[cfg(test)]
mod tests {
    use super::*;
    use mdc_engine::ManualClock;
    use mdc_platform::{Frame, PlatformResult};

    #[derive(Default)]
    struct RecordingLauncher {
        calls: std::cell::RefCell<Vec<String>>,
    }

    impl Launcher for RecordingLauncher {
        fn open(&self, bnet_path: &str) -> Result<String, String> {
            self.calls.borrow_mut().push(bnet_path.to_string());
            Ok(format!("launched {bnet_path}"))
        }
    }

    struct FakePlatform {
        window: PlatformResult<WindowId>,
        frame: Frame,
        capture_err: Option<PlatformError>,
        foreground: bool,
        keys: std::cell::RefCell<Vec<(WindowId, Key, u32)>>,
    }

    impl FakePlatform {
        fn ok(frame: Frame) -> Self {
            Self {
                window: Ok(WindowId(1)),
                frame,
                capture_err: None,
                foreground: true,
                keys: std::cell::RefCell::new(Vec::new()),
            }
        }
    }

    impl Platform for FakePlatform {
        fn find_wow(&self, _process_name: &str) -> PlatformResult<WindowId> {
            self.window.clone()
        }
        fn capture(&self, _window: WindowId, _region: Rect) -> PlatformResult<Frame> {
            match &self.capture_err {
                Some(e) => Err(e.clone()),
                None => Ok(self.frame.clone()),
            }
        }
        fn post_key(&self, window: WindowId, key: &Key, hold_ms: u32) -> PlatformResult<()> {
            self.keys.borrow_mut().push((window, *key, hold_ms));
            Ok(())
        }
        fn post_mouse(&self, _window: WindowId, _event: &MouseEvent) -> PlatformResult<()> {
            Ok(())
        }
        fn is_foreground(&self, _window: WindowId) -> PlatformResult<bool> {
            Ok(self.foreground)
        }
    }

    fn set_cell(pixels: &mut [u8], width: u32, cell_size: u32, cell: usize, color: [u8; 3]) {
        let x0 = cell as u32 * cell_size;
        for y in 0..cell_size {
            for x in x0..x0 + cell_size {
                let i = ((y * width + x) * 4) as usize;
                pixels[i] = color[2];
                pixels[i + 1] = color[1];
                pixels[i + 2] = color[0];
                pixels[i + 3] = 255;
            }
        }
    }

    /// A hand-built v1 (8-cell) strip that decodes: magic cell, one Main slot
    /// bound to VK 0x52, version nibble 1 and a matching checksum.
    fn v1_frame() -> Frame {
        let cell_size: u32 = 8;
        let width = cell_size * CELL_COUNT_EXT3 as u32;
        let height = cell_size;
        let mut pixels = vec![0u8; (width * height * 4) as usize];
        set_cell(&mut pixels, width, cell_size, 0, [255, 0, 255]);
        set_cell(&mut pixels, width, cell_size, 1, [85, 34, 136]);
        set_cell(&mut pixels, width, cell_size, 6, [17, 0, 0]);
        set_cell(&mut pixels, width, cell_size, 7, [17, 0, 0]);
        Frame { width, height, pixels }
    }

    fn test_settings() -> Settings {
        Settings {
            cell_size: 8,
            combat_only: false, // permit the v1 frame to schedule
            ..Settings::default()
        }
    }

    fn worker(
        platform: FakePlatform,
        settings: Settings,
    ) -> (Worker<FakePlatform, ManualClock>, Arc<Mutex<Shared>>) {
        let shared = Arc::new(Mutex::new(Shared::default()));
        let worker = Worker::new(
            platform,
            ManualClock::new(0),
            settings,
            Arc::clone(&shared),
            Box::new(RecordingLauncher::default()),
        );
        (worker, shared)
    }

    fn log_texts(shared: &Arc<Mutex<Shared>>) -> Vec<String> {
        match shared.lock() {
            Ok(guard) => guard.log.iter().map(|l| l.text.clone()).collect(),
            Err(poisoned) => poisoned.into_inner().log.iter().map(|l| l.text.clone()).collect(),
        }
    }

    #[test]
    fn hhmmss_formats_wall_clock() {
        assert_eq!(hhmmss(0), "00:00:00");
        assert_eq!(hhmmss(13 * 3600 + 5 * 60 + 9), "13:05:09");
    }

    #[test]
    fn start_without_window_stays_stopped_and_logs_hint() {
        let platform = FakePlatform {
            window: Err(PlatformError::WindowNotFound("Wow".to_string())),
            frame: Frame::default(),
            capture_err: None,
            foreground: true,
            keys: std::cell::RefCell::new(Vec::new()),
        };
        let (mut worker, shared) = worker(platform, test_settings());
        worker.start();
        assert_eq!(worker.state(), RunState::Stopped);
        assert!(log_texts(&shared).iter().any(|t| t.contains("Start WoW first")));
    }

    #[test]
    fn start_while_running_is_ignored() {
        let (mut worker, shared) = worker(FakePlatform::ok(v1_frame()), test_settings());
        worker.start();
        worker.start();
        assert_eq!(worker.state(), RunState::Running);
        assert!(log_texts(&shared).iter().any(|t| t.contains("Start ignored")));
    }

    #[test]
    fn decodes_v1_frame_and_posts_the_main_key() {
        let (mut worker, _shared) = worker(FakePlatform::ok(v1_frame()), test_settings());
        worker.start();
        worker.step();
        let keys = worker.platform.keys.borrow();
        assert_eq!(keys.len(), 1, "expected exactly one key send");
        assert_eq!(keys[0].1.virtual_key, 0x52);
        assert_eq!(keys[0].2, 25); // KeyPressMs default
    }

    #[test]
    fn toggle_pause_cycles_running_and_paused() {
        let (mut worker, _shared) = worker(FakePlatform::ok(v1_frame()), test_settings());
        worker.start();
        worker.toggle_pause();
        assert_eq!(worker.state(), RunState::Paused);
        worker.toggle_pause();
        assert_eq!(worker.state(), RunState::Running);
    }

    #[test]
    fn calibrate_while_running_is_refused() {
        let (mut worker, shared) = worker(FakePlatform::ok(v1_frame()), test_settings());
        worker.start();
        worker.calibrate();
        assert_eq!(worker.state(), RunState::Running);
        assert!(log_texts(&shared).iter().any(|t| t.contains("Stop the engine first")));
    }

    #[test]
    fn calibrate_when_stopped_samples_and_returns_to_stopped() {
        let (mut worker, shared) = worker(FakePlatform::ok(v1_frame()), test_settings());
        worker.calibrate();
        assert_eq!(worker.state(), RunState::Calibrating);
        worker.step();
        assert_eq!(worker.state(), RunState::Stopped);
        assert!(log_texts(&shared).iter().any(|t| t.contains("calibration sample ok")));
    }

    #[test]
    fn capture_failure_holds_without_sending_keys() {
        let platform = FakePlatform {
            capture_err: Some(PlatformError::CaptureFailed("stale DIB".to_string())),
            ..FakePlatform::ok(v1_frame())
        };
        let (mut worker, _shared) = worker(platform, test_settings());
        worker.start();
        worker.step();
        assert!(worker.platform.keys.borrow().is_empty());
        assert_eq!(worker.state(), RunState::Running);
    }

    #[test]
    fn background_keys_disabled_and_not_foreground_refuses_input() {
        let mut settings = test_settings();
        settings.allow_background_keys = false;
        let platform = FakePlatform { foreground: false, ..FakePlatform::ok(v1_frame()) };
        let (mut worker, shared) = worker(platform, settings);
        worker.start();
        worker.step();
        assert!(worker.platform.keys.borrow().is_empty());
        assert!(log_texts(&shared).iter().any(|t| t.contains("not the foreground window")));
    }

    #[test]
    fn open_game_empty_path_logs_autodetect_hint() {
        let (mut worker, shared) = worker(FakePlatform::ok(v1_frame()), test_settings());
        worker.open_game();
        assert!(log_texts(&shared).iter().any(|t| t.contains("auto-detecting Battle.net")));
    }

    #[test]
    fn open_game_override_uses_the_launcher() {
        let mut settings = test_settings();
        settings.bnet_path = "C:/fake/Battle.net Launcher.exe".to_string();
        let (mut worker, shared) = worker(FakePlatform::ok(v1_frame()), settings);
        worker.open_game();
        // RecordingLauncher lives behind the trait object; its Ok message is logged.
        assert!(log_texts(&shared)
            .iter()
            .any(|t| t.contains("C:/fake/Battle.net Launcher.exe")));
    }

    #[test]
    fn runtime_smoke_snapshot_and_shutdown() {
        let mut runtime = Runtime::new(FakePlatform::ok(v1_frame()), Settings::default());
        assert_eq!(runtime.snapshot().run_state, RunState::Stopped);
        runtime.start();
        runtime.stop();
        runtime.shutdown();
    }
}
