// Wave B (4.1 "Paragon"): ONE brain, two hosts.
//
// The shared brain (protocol, knowledge, decision, scheduler, telemetry,
// settings, the portable engine) is compiled ONCE into MaxDpsCompanion.Core and
// that assembly is the single source this test project compiles against. The
// Windows app is imported under the extern alias `win` because exactly ONE type
// intentionally still exists in both assemblies: RotationEngine (Wave C merges
// it; the Windows one binds concrete capture/input, Core's binds IPlatform).
// ShadowedTypeGateTests enforces that set is exactly {RotationEngine}.
//
// Consequence: an unqualified type name in a test file means "from Core". The
// Windows-only HOST types the tests exercise (key delivery, window lookup,
// capture diagnostics, the portable UI models) are re-exported here as per-type
// global aliases. Add a line here when a test needs another Windows-only type -
// never alias a type that Core owns, or the test would silently bind the retired
// Windows copy.
//
// The WinUI *views* (Ui.Shell) are NOT aliased here: the six test files that
// exercise them import the namespace explicitly with
// `using win::MaxDpsCompanion.Ui.Shell;`.

extern alias win;

global using App = win::MaxDpsCompanion.App;
global using BattleNetLauncher = win::MaxDpsCompanion.BattleNetLauncher;
global using BlockLocator = win::MaxDpsCompanion.BlockLocator;
global using CaptureDiagnostics = win::MaxDpsCompanion.CaptureDiagnostics;
global using CaptureOutcome = win::MaxDpsCompanion.CaptureOutcome;
global using ChordPlanner = win::MaxDpsCompanion.ChordPlanner;
global using ChordRoute = win::MaxDpsCompanion.ChordRoute;
global using Ext4Assembler = win::MaxDpsCompanion.Ext4Assembler;
global using HotbarMapStatus = win::MaxDpsCompanion.HotbarMapStatus;
global using HoldCopy = win::MaxDpsCompanion.HoldCopy;
global using ToggleStateCopy = win::MaxDpsCompanion.ToggleStateCopy;
global using IKeyDeliveryBackend = win::MaxDpsCompanion.IKeyDeliveryBackend;
global using KeyDelivery = win::MaxDpsCompanion.KeyDelivery;
global using KeySender = win::MaxDpsCompanion.KeySender;
global using KeyStateProbe = win::MaxDpsCompanion.KeyStateProbe;
global using MovementGuard = win::MaxDpsCompanion.MovementGuard;
global using Native = win::MaxDpsCompanion.Native;
global using ParsedStrokeCache = win::MaxDpsCompanion.ParsedStrokeCache;
global using Program = win::MaxDpsCompanion.Program;
global using ReleaseNotes = win::MaxDpsCompanion.ReleaseNotes;
global using ScreenSampler = win::MaxDpsCompanion.ScreenSampler;
global using SlotGate = win::MaxDpsCompanion.SlotGate;
global using SlotVerdict = win::MaxDpsCompanion.SlotVerdict;
global using SpellIconCache = win::MaxDpsCompanion.SpellIconCache;
global using ToggleMirror = win::MaxDpsCompanion.ToggleMirror;
global using ToggleMirrorState = win::MaxDpsCompanion.ToggleMirrorState;
global using ToggleModel = win::MaxDpsCompanion.ToggleModel;
global using ToggleSwitch = win::MaxDpsCompanion.ToggleSwitch;
global using UiScale = win::MaxDpsCompanion.UiScale;
global using UiTier = win::MaxDpsCompanion.UiTier;
global using UserPress = win::MaxDpsCompanion.UserPress;
global using UserPressActionKind = win::MaxDpsCompanion.UserPressActionKind;
global using UserPressClassifier = win::MaxDpsCompanion.UserPressClassifier;
global using UserPressQueue = win::MaxDpsCompanion.UserPressQueue;
global using WowWindow = win::MaxDpsCompanion.WowWindow;
