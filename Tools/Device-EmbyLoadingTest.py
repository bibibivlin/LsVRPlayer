"""Check Emby loading responsiveness on PICO using an owned loopback fixture.

Requires a development APK, foreground focus and verified headset presence.
Only with explicit user authorization, --allow-unworn keeps focus/freshness
checks but records unworn presence separately from worn-headset acceptance.
No real account or video is used. Four device stores, synthetic UserData,
commands, sessions and only this run's reverse mapping are restored/verified.
Command round trips include ADB and the 0.5-second development poll; they are
not physical-controller input latency or a frame-time percentile measurement.
"""
import argparse
import copy
import importlib.util
import json
from pathlib import Path
import statistics
import sys
import threading
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "Tools/.runtime"))


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    value = importlib.util.module_from_spec(spec)
    sys.modules[name] = value
    spec.loader.exec_module(value)
    return value


parts = module("loading_device_parts", ROOT / "Tools/Device-EmbyMoviePartsTest.py")
base = parts.base
fixture = module("loading_fixture", ROOT / "Tools/Serve-EmbyMoviePartsFixture.py")


class LoadingState(fixture.FixtureState):
    def __init__(self, media, count):
        super().__init__(media, 90000, artwork=True)
        self.catalog = [{"Id": f"load-{index:04}", "Name": f"Loading fixture {index:04}",
                         "SortName": f"{index:04}", "Type": "Movie", "ProductionYear": 2026,
                         "Overview": "文" * 8192, "RunTimeTicks": self.duration_ticks,
                         "People": [{"Id": "fixture-actor", "Name": "Fixture Actor", "Type": "Actor"}] * 12,
                         "Genres": ["Loading Genre"], "ImageTags": {"Primary": "fixture-primary"},
                         "UserData": self.empty_user_data()} for index in range(count)]
        self.catalog_by_id = {item["Id"]: item for item in self.catalog}
        self.hold = False
        self.held = threading.Event()
        self.release = threading.Event()
        self.events = []

    def item(self, item_id):
        if item_id.startswith("load-"):
            return self.catalog_by_id[item_id]
        return super().item(item_id)

    def arm(self):
        self.release.clear()
        self.held.clear()
        self.hold = True

    def library_items(self, query):
        items = self.catalog
        search = query.get("searchterm", "").strip().casefold()
        if search:
            items = [item for item in items if search in item["Name"].casefold()]
        start = max(0, int(query.get("startindex", 0)))
        limit = min(200, max(0, int(query.get("limit", 30))))
        return {"Items": items[start:start+limit], "TotalRecordCount": len(items), "StartIndex": start}


class LoadingHandler(fixture.FixtureHandler):
    def reply(self, status, value=None):
        state = self.server.state
        if not isinstance(value, dict) or "StartIndex" not in value:
            return super().reply(status, value)
        event = {"start": value["StartIndex"], "count": len(value["Items"]),
                 "requestedAt": time.monotonic(), "held": False, "completed": False}
        with state.lock:
            state.events.append(event)
            hold = state.hold and value["StartIndex"] == 200
            if hold:
                state.hold = False
                event["held"] = True
        if hold:
            state.held.set()
            if not state.release.wait(25):
                event["holdTimedOut"] = True
        body = json.dumps(value, ensure_ascii=False).encode("utf-8")
        event["bytes"] = len(body)
        base.require(len(body) < 8 * 1024 * 1024, "Fixture exceeded the client response limit")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        event["sendStartedAt"] = time.monotonic()
        try:
            for offset in range(0, len(body), 32 * 1024):
                self.wfile.write(body[offset:offset+32*1024])
                self.wfile.flush()
                time.sleep(.003)
            event["completed"] = True
        finally:
            event["endedAt"] = time.monotonic()


class ResponsiveDevice(parts.GuardedDevice):
    def __init__(self, args, run_id):
        super().__init__(args, run_id)
        self.round_trips = []
        self.startup_samples = []
        self.profile_remote = base.EXTERNAL + "loading-profile-" + run_id + ".raw"

    def launch(self):
        if not self.args.profile:
            return super().launch()
        base.require(not self.exists(self.profile_remote), "Owned profiler target already exists")
        activity = self.adb("shell", "cmd", "package", "resolve-activity", "--brief", base.PACKAGE).stdout.decode().strip().splitlines()[-1]
        base.require(activity.startswith(base.PACKAGE + "/") and " " not in activity, "Could not resolve the player activity")
        self.adb("shell", "input", "keyevent", "KEYCODE_WAKEUP")
        flags = "-profiler-enable -profiler-log-file " + self.profile_remote
        self.adb("shell", "am", "start", "-n", activity, "--es", "unity", "'" + flags + "'")
        def wake():
            while not self.awake.wait(1):
                self.adb("shell", "input", "keyevent", "KEYCODE_WAKEUP", check=False)
        self.wake_thread = threading.Thread(target=wake, daemon=True)
        self.wake_thread.start()

    def _guard(self, value):
        if not self.args.allow_unworn:
            return super()._guard(value)
        if not value.get("hasFocus") or time.time() - self.diagnostic_modified > 7:
            raise parts.PresenceFailure("Application focus or fresh diagnostics interrupted the run")
        self.focus_samples += 1

    def begin_guard(self, launch_time):
        # Startup may emit a snapshot before XR focus/presence has settled.
        # This readiness wait is only before the run; an interruption after
        # guarded=True still fails immediately and cannot be resumed in place.
        deadline = time.monotonic() + 45
        while time.monotonic() < deadline:
            try:
                value = self.status()
                self.startup_samples.append({key: value.get(key) for key in
                    ("timestampUtc", "frame", "hasFocus", "presenceKnown", "userPresent")})
                if self.diagnostic_modified >= int(launch_time) and value.get("snapshot", {}).get("generation") == 0:
                    if value.get("hasFocus") and (self.args.allow_unworn or value.get("presenceKnown") and value.get("userPresent")):
                        self._guard(value)
                        self.guarded = True
                        return
            except (base.CheckFailure, json.JSONDecodeError, ValueError):
                pass
            time.sleep(.4)
        raise parts.PresenceFailure("Fresh headset-presence and application-focus diagnostics did not become available")

    def command(self, action, **fields):
        started = time.monotonic()
        value = super().command(action, **fields)
        elapsed = time.monotonic() - started
        if self.guarded:
            base.require(value.get("hasFocus") and (self.args.allow_unworn or value.get("presenceKnown") and value.get("userPresent")),
                         "A command observed interrupted focus or headset presence")
            self.round_trips.append({"action": action, "seconds": elapsed, "frame": value["frame"],
                                     "loading": value["emby"]["loading"]})
            base.require(elapsed < 3, "Main-thread development command exceeded three seconds")
        return value


def run(args):
    run_id = uuid.uuid4().hex
    output = ROOT / "Docs/Validation/EmbyLoadingDevice" / ("device-" + run_id)
    output.mkdir(parents=True)
    # Browsing only: this run-owned file is never requested as video.
    placeholder = output / "unused-media-placeholder.mp4"
    placeholder.write_bytes(b"Emby loading fixture: no video playback\n")
    state = LoadingState(placeholder, args.items)
    original_data = copy.deepcopy(state.user_data)
    server = fixture.FixtureServer(("127.0.0.1", 0), state)
    server.RequestHandlerClass = LoadingHandler
    server_thread = threading.Thread(target=server.serve_forever, daemon=True)
    server_thread.start()
    mapping = "tcp:" + str(server.server_address[1])
    api = base.Server("http://127.0.0.1:" + str(server.server_address[1]))
    device = ResponsiveDevice(args, run_id)
    report = {"passed": False, "scope": "PICO hardware, owned synthetic HTTP fixture over adb reverse",
              "runId": run_id, "items": args.items, "checks": [], "samples": [],
              "physicalControllerVerified": False, "realLanServerVerified": False,
              "videoPlaybackVerified": False, "presenceRequirementWaivedByUser": args.allow_unworn}
    stop_monitor = threading.Event()
    monitor_errors = []
    monitor_thread = None
    backed_up = launched = owns_commands = mapping_created = False
    temporary_id = ""
    original_ids = set()
    usb_original = None
    original_reverse = None

    def persist():
        report["focusSamples"] = device.focus_samples
        report["roundTrips"] = device.round_trips
        report["startupSamples"] = device.startup_samples
        with state.lock:
            report["responses"] = copy.deepcopy(state.events)
        (output / "report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

    def guard():
        if monitor_errors:
            raise parts.PresenceFailure(monitor_errors[0])

    def passed(name, **data):
        guard()
        report["checks"].append({"name": name, "passed": True, **data})
        persist()
        print("PASS " + name, flush=True)

    def monitor():
        while not stop_monitor.wait(.4):
            try:
                sample = device.status()
                report["samples"].append({key: sample.get(key) for key in
                    ("timestampUtc", "frame", "unityFps", "hasFocus", "presenceKnown", "userPresent")})
            except Exception as error:
                monitor_errors.append(str(error))
                return

    def held():
        device.wait(lambda: (guard(), device.status(), state.held.is_set())[2], bool,
                    "Second page did not enter the controlled hold", timeout=15)

    def screenshot(name):
        guard()
        device.status()
        data = device.adb("exec-out", "screencap", "-p").stdout
        base.require(len(data) > 40000, "Screenshot was not reviewable")
        (output / (name + ".png")).write_bytes(data)

    def home():
        device.command("emby-home")
        device.page("Home")

    def complete(action, page, **fields):
        device.command("ui-input-reset")
        home()
        device.command("ui-input-stats-reset")
        response_start = len(state.events)
        started = time.monotonic()
        value = device.command(action, **fields)
        active_samples = 0
        input_samples = []
        last_frame = value["frame"]
        while value["emby"]["loading"]:
            guard()
            active_samples += 1
            value = device.inspect()
            base.require(isinstance(value.get("input"), dict), "APK does not provide input-loop diagnostics")
            input_samples.append(value["input"])
            base.require(not value["input"]["diagnostic"], "Real input was replaced by a diagnostic pointer")
            base.require(0 <= value["frame"] - value["input"]["processFrame"] <= 2,
                         "EventSystem stopped processing input while the main loop continued")
            base.require(value["frame"] > last_frame, "Unity frames stopped advancing during loading")
            last_frame = value["frame"]
            base.require(time.monotonic() - started < 75, "Large library loading timed out")
        value = device.page(page)
        expected = [item["Id"] for item in state.catalog]
        base.require(value["emby"]["itemIds"] == expected and value["emby"]["totalCount"] == args.items,
                     "Large wall lost items or stable order")
        base.require(active_samples >= 2, "Loading ended without enough observed main-thread heartbeats")
        responses = state.events[response_start:]
        base.require([entry["start"] for entry in responses] == list(range(0, args.items, 200)),
                     "The full loading run did not fetch every expected page")
        passed(action + " complete wall stays responsive", seconds=time.monotonic()-started,
               loadingHeartbeats=active_samples, pages=len(responses), bytes=sum(entry["bytes"] for entry in responses),
               inputSamples=input_samples)

    try:
        original_reverse = device.adb("reverse", "--list").stdout.splitlines()
        base.require(mapping not in device.adb("reverse", "--list").stdout.decode().split(), "Reverse mapping already exists")
        base.require(not device.exists(device.command_remote) and not device.exists(device.restore_remote) and
                     not device.exists("files/command.json", True) and not device.exists("files/command.tmp", True),
                     "Another command or owned staging path is pending")
        owns_commands = True
        device.backup(output)
        backed_up = True
        original_accounts = next(entry["bytes"] for entry in device.backups if entry["label"] == "accounts")
        original_ids = {account["id"] for account in json.loads(original_accounts or b"{}").get("accounts", [])}
        usb_original = device.adb("shell", "settings", "get", "global", "stay_on_while_plugged_in").stdout.strip()
        device.adb("reverse", mapping, mapping)
        mapping_created = True
        original_settings = next(entry["bytes"] for entry in device.backups if entry["label"] == "settings")
        settings = json.loads(original_settings or b'{"version":3}')
        settings.update(autoHide=False, autoLoadSubtitles=False, volume=0, history=[])
        device.write_external(base.EXTERNAL + "settings.json", json.dumps(settings).encode())
        api.login("fixture", "fixture")
        launched = True
        launch_time = time.time()
        device.launch()
        device.begin_guard(launch_time)
        monitor_thread = threading.Thread(target=monitor, daemon=True)
        monitor_thread.start()
        device.command("emby-login", uri=api.address, username="fixture", password="fixture", flag=False)
        initial = device.page("Home")
        temporary_id = initial["emby"]["accountId"]
        base.require(temporary_id not in original_ids, "Fixture reused an existing device account")
        device.command("menu", flag=True)
        device.command("tab", name="文件")

        state.arm()
        device.command("emby-library", uri="movies", name="Loading fixture", collectionType="movies")
        held()
        screenshot("loading")
        changed = device.command("tab", name="设置")
        base.require(changed["tab"] == "设置" and changed["emby"]["loading"], "Settings tab could not respond during loading")
        changed = device.command("tab", name="文件")
        base.require(changed["tab"] == "文件" and changed["emby"]["loading"], "Files tab could not return during loading")
        device.command("ui-back", preset=2, flag=False, valid=True)
        started = time.monotonic()
        device.command("ui-back", preset=0, flag=True, valid=True)
        returned = device.page("Home")
        back_seconds = time.monotonic()-started
        base.require(back_seconds < 3 and not state.release.is_set(), "Back waited for the library response")
        device.command("ui-back", preset=2, flag=False, valid=True)
        device.command("ui-input-reset")
        state.release.set()
        time.sleep(1)
        base.require(device.inspect()["emby"]["page"] == "Home", "Retired library overwrote the restored page")
        passed("Tabs and shared Y/B back respond while a library request is held", backRoundTripSeconds=back_seconds)

        state.arm()
        device.command("emby-library", uri="movies", name="Loading fixture", collectionType="movies")
        held()
        value = device.command("source", name="local")
        base.require(value["source"] != "Emby" and not state.release.is_set(), "Source change waited for the library response")
        state.release.set()
        time.sleep(1)
        base.require(device.inspect()["source"] != "Emby", "Cancelled library overwrote the new source")
        passed("Source change cancels loading and rejects late results")

        complete("emby-library", "Library", uri="movies", name="Loading fixture", collectionType="movies")
        screenshot("loaded-wall")
        complete("emby-person", "Person", uri="fixture-actor", name="Fixture Actor")
        complete("emby-filter", "Library", name="Genre", uri="Loading Genre")
        device.command("emby-search", subtitle="010")
        searched = device.page("Library")
        expected_search = [item["Id"] for item in state.catalog if "010" in item["Name"]]
        base.require(searched["emby"]["itemIds"] == expected_search, "Search changed the expected item set")
        passed("Search after loading preserves matching items", matches=len(expected_search))
        base.require(not state.reports and not state.playing, "Browsing unexpectedly started playback")
        guard()
        report["passed"] = True
    except Exception as error:
        report["error"] = str(error)
        raise
    finally:
        errors = []
        state.release.set()
        stop_monitor.set()
        if monitor_thread:
            monitor_thread.join(timeout=5)
            if monitor_thread.is_alive():
                errors.append("Diagnostic monitor did not stop")
        report["continuousFocusPassed"] = bool(monitor_thread) and not monitor_errors
        report["continuousPresencePassed"] = bool(report["samples"]) and report["continuousFocusPassed"] and all(
            sample.get("presenceKnown") and sample.get("userPresent") for sample in report["samples"])
        if monitor_errors:
            report["passed"] = False
        device.guarded = False
        if launched:
            try:
                device.command("stop")
                device.settled_reports()
                current = device.inspect().get("emby", {}).get("accountId", "")
                if current and current not in original_ids:
                    temporary_id = current
                if temporary_id:
                    device.command("emby-remove", name=temporary_id)
                    device.settled_reports()
            except Exception:
                errors.append("Final report drain or fixture logout failed")
        device.awake.set()
        if device.wake_thread:
            device.wake_thread.join(timeout=5)
            if device.wake_thread.is_alive():
                errors.append("Owned wake helper did not stop")
        try:
            device.adb("shell", "am", "force-stop", base.PACKAGE)
            if args.profile:
                base.require(device.exists(device.profile_remote), "Player did not produce the requested profile")
                device.adb("pull", device.profile_remote, str(output / "loading.raw"))
                base.require((output / "loading.raw").stat().st_size > 1024, "Player profiler output was empty")
                device.remove(device.profile_remote)
                base.require(not device.exists(device.profile_remote), "Owned profiler staging remained")
                report["profileCaptured"] = True
            for identity, data in original_data.items():
                if state.user_data[identity] != data:
                    api.restore_user_data(identity, data)
            report["serverUserDataRestored"] = state.user_data == original_data
            base.require(report["serverUserDataRestored"], "Synthetic UserData did not restore exactly")
            if api.token:
                api.request("/Sessions/Logout", "POST", {})
            report["sessionsLoggedOut"] = not state.tokens and not state.playing
            base.require(report["sessionsLoggedOut"], "Fixture sessions remained active")
        except Exception:
            errors.append("Fixture UserData or session restoration failed")
        if backed_up:
            try:
                device.restore()
                report["deviceStoresRestored"] = True
            except Exception:
                errors.append("Four device stores did not restore byte-for-byte")
        if owns_commands:
            try:
                device.cleanup_commands()
                base.require(not device.exists(device.command_remote) and not device.exists(device.restore_remote) and
                             not device.exists("files/command.json", True) and not device.exists("files/command.tmp", True),
                             "Owned command staging was not removed")
                report["commandsRemoved"] = True
            except Exception:
                errors.append("Owned commands did not clean up")
        if mapping_created:
            try:
                device.adb("reverse", "--remove", mapping)
                base.require(device.adb("reverse", "--list").stdout.splitlines() == original_reverse,
                             "Original reverse mappings did not restore")
                report["reverseMappingsRestored"] = True
            except Exception:
                errors.append("Reverse mapping restoration failed")
        if usb_original is not None:
            try:
                report["usbSettingUnchanged"] = device.adb("shell", "settings", "get", "global", "stay_on_while_plugged_in").stdout.strip() == usb_original
                base.require(report["usbSettingUnchanged"], "USB setting changed unexpectedly")
            except Exception:
                errors.append("USB setting verification failed")
        server.shutdown()
        server.server_close()
        server_thread.join(timeout=5)
        if server_thread.is_alive():
            errors.append("Owned fixture server did not stop")
        report["fixtureServerStopped"] = not server_thread.is_alive()
        report["cleanupErrors"] = errors
        if errors:
            report["passed"] = False
        fps = [sample["unityFps"] for sample in report["samples"] if isinstance(sample.get("unityFps"), (int, float))]
        if fps:
            report["sampledUnityFps"] = {"min": min(fps), "median": statistics.median(fps), "samples": len(fps)}
        persist()
        print("Loading report: " + str(output / "report.json"), flush=True)
        if errors:
            raise RuntimeError("; ".join(errors))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--adb", default=base.DEFAULT_ADB)
    parser.add_argument("--items", type=int, default=1005)
    parser.add_argument("--allow-unworn", action="store_true", help="Only use when the user explicitly waives headset-presence validation")
    parser.add_argument("--profile", action="store_true", help="Record a run-owned Unity binary profile and pull it into the ignored report folder")
    arguments = parser.parse_args()
    base.require(401 <= arguments.items <= 2005, "Use 401 to 2005 items for a bounded multipage run")
    run(arguments)
