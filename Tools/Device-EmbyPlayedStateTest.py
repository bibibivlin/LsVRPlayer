"""Check watched actions through real UI pointers on an owned loopback Emby fixture.

Requires a development APK and an existing short test MP4. No real server/account
is used. Device stores and every fixture UserData field are restored and verified.
Physical controller bindings and worn-headset comfort remain separate acceptance.
"""
import argparse
import copy
import importlib.util
import json
from pathlib import Path
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


parts = module("played_device_parts", ROOT / "Tools/Device-EmbyMoviePartsTest.py")
base = parts.base
fixture = module("played_device_fixture", ROOT / "Tools/Serve-EmbyMoviePartsFixture.py")


def run(args):
    run_id = uuid.uuid4().hex
    validation = ROOT / "Docs/Validation/EmbyPlayedState" / ("device-" + run_id)
    validation.mkdir(parents=True)
    captures = validation / "captures"
    captures.mkdir()
    report = {"passed": False, "checks": [], "scope": "owned synthetic fixture over adb reverse", "runId": run_id}
    movie_ids = ("main", "cd2", "cd3")
    seed_ids = ("ordinary", *movie_ids, "pv", "trailer")
    state = fixture.FixtureState(args.media, args.duration_ms, artwork=True)
    for index, identity in enumerate(seed_ids):
        state.user_data[identity].update(PlaybackPositionTicks=10000000, PlayedPercentage=10,
                                         LastPlayedDate=f"2026-10-16T0{index}:00:00Z")
    original_data = copy.deepcopy(state.user_data)
    server = fixture.FixtureServer(("127.0.0.1", 0), state)
    server_thread = threading.Thread(target=server.serve_forever, daemon=True)
    server_thread.start()
    port = server.server_address[1]
    mapping = "tcp:" + str(port)
    api = base.Server("http://127.0.0.1:" + str(port))
    device = parts.GuardedDevice(args, run_id)
    monitor_stop = threading.Event()
    monitor_thread = None
    monitor_error = []
    temporary_id = ""
    original_ids = set()
    mapping_created = launched = backed_up = owns_commands = False
    usb_original = None

    def persist():
        report["focusSamples"] = device.focus_samples
        (validation / "report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

    def check():
        if monitor_error:
            raise parts.PresenceFailure(monitor_error[0])

    def monitor():
        while not monitor_stop.wait(.8):
            try:
                device.status()
            except Exception as error:
                monitor_error.append(str(error))
                return

    def passed(name):
        check()
        report["checks"].append({"name": name, "passed": True})
        persist()
        print("PASS " + name, flush=True)

    def click(name):
        check()
        for pressed in (False, True, False):
            device.command("ui-pointer", name=name, flag=pressed, value=.5, preset=0)
        return device.inspect()

    def capture(name, keep_pointer=False):
        check()
        if not keep_pointer:
            device.command("ui-pointer", name="", flag=False, preset=0)
        time.sleep(.6)
        device.status()
        target = captures / (name + ".png")
        target.write_bytes(device.adb("exec-out", "screencap", "-p").stdout)
        base.require(target.stat().st_size > 40000, "Device capture was not reviewable")

    def marked(played):
        with state.lock:
            return all(state.user_data[identity]["Played"] == played and
                       state.user_data[identity]["PlaybackPositionTicks"] == 0 for identity in movie_ids)

    def wait_marked(played):
        device.wait(lambda: (device.status(), marked(played))[1], bool, "Movie-wide watched state did not settle", timeout=30)
        device.settled_reports()
        time.sleep(.5)

    try:
        base.require(mapping not in device.adb("reverse", "--list").stdout.decode().split(), "Reverse mapping already exists")
        base.require(not device.exists(device.command_remote) and not device.exists(device.restore_remote), "Owned staging paths already exist")
        base.require(not device.exists("files/command.json", True) and not device.exists("files/command.tmp", True), "Another development command is pending")
        owns_commands = True
        device.backup(validation)
        backed_up = True
        for entry in device.backups:
            if entry["label"] == "accounts" and entry["bytes"]:
                original_ids = {value["id"] for value in json.loads(entry["bytes"]).get("accounts", [])}
        usb_original = device.adb("shell", "settings", "get", "global", "stay_on_while_plugged_in").stdout.strip()
        device.adb("reverse", mapping, mapping)
        mapping_created = True
        original_settings = next(value["bytes"] for value in device.backups if value["label"] == "settings")
        settings = json.loads(original_settings) if original_settings else {"version": 3}
        settings.update(version=3, autoHide=False, autoLoadSubtitles=False, formatDetection=0,
                        loop=False, sequential=True, volume=0, speed=1, history=[])
        device.write_external(base.EXTERNAL + "settings.json", json.dumps(settings).encode())
        api.login("fixture", "fixture")
        launch_time = time.time()
        device.launch()
        launched = True
        device.begin_guard(launch_time)
        monitor_thread = threading.Thread(target=monitor, daemon=True)
        monitor_thread.start()
        device.command("emby-login", uri=api.address, username="fixture", password="fixture", flag=False)
        home = device.page("Home")
        temporary_id = home["emby"]["accountId"]
        base.require(temporary_id not in original_ids, "Fixture account was already registered")
        initial_count = home["emby"]["resumeCount"]
        device.command("menu", flag=True)
        device.command("tab", name="文件")
        capture("resume-before")
        device.command("ui-pointer", name="Emby resume cd2", flag=False, value=.18, preset=0)
        capture("resume-hover", keep_pointer=True)
        capture("resume-after-hover")
        click("Emby played cd2")
        wait_marked(True)
        device.wait(device.inspect, lambda value: value["emby"]["resumeCount"] == initial_count - 3,
                    "All movie CDs were not removed from the current continue-watching page")
        device.command("emby-home")
        device.page("Home", resumeCount=initial_count - 3)
        base.require(all(state.user_data[identity] == original_data[identity] for identity in ("ordinary", "pv", "trailer")),
                     "Movie action changed another film or an extra")
        passed("Continue-watching CD action marks the entire movie and survives a server refresh")

        device.command("emby-item", uri="main")
        device.page("Details")
        capture("watched-details")
        click("Emby action 标记为未播放")
        wait_marked(False)
        device.page("Details")
        passed("Details can restore the entire movie to unplayed without restoring stale CD progress")

        click("Emby action 播放")
        device.wait(device.status, lambda value: value["snapshot"]["state"] == 3 and value["snapshot"]["generation"] > 0,
                    "Equal-sized play action did not start original playback", timeout=45)
        time.sleep(1)
        device.command("pause")
        device.settled_reports()
        device.command("emby-item", uri="main")
        device.page("Details")
        capture("resume-details")
        click("Emby action 标记为已播放")
        wait_marked(True)
        # More than a check-in interval while the old session remains open and paused.
        deadline = time.monotonic() + 12
        while time.monotonic() < deadline:
            check()
            device.status()
            base.require(marked(True), "Old session recreated watched movie resume progress")
            time.sleep(.5)
        device.command("emby-home")
        device.page("Home", resumeCount=initial_count - 3)
        capture("resume-after")
        passed("Details marks all CDs and old playback check-ins cannot recreate continue-watching progress")
        check()
        report["passed"] = True
    except Exception as error:
        report["error"] = str(error)
        raise
    finally:
        errors = []
        monitor_stop.set()
        if monitor_thread:
            monitor_thread.join(timeout=5)
        report["continuousPresencePassed"] = bool(monitor_thread) and not monitor_error
        if monitor_error:
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
                errors.append("Progress drain or fixture-account logout failed")
        device.awake.set()
        if device.wake_thread:
            device.wake_thread.join(timeout=5)
        try:
            device.adb("shell", "am", "force-stop", base.PACKAGE)
        except Exception:
            errors.append("App force-stop failed")
        try:
            for identity, data in original_data.items():
                if state.user_data[identity] != data:
                    api.restore_user_data(identity, data)
            report["serverUserDataRestored"] = all(state.user_data[identity] == data for identity, data in original_data.items())
            base.require(report["serverUserDataRestored"], "A fixture UserData field was not restored exactly")
            if api.token:
                api.request("/Sessions/Logout", "POST", {})
            report["sessionsLoggedOut"] = not state.tokens and not state.playing
            base.require(report["sessionsLoggedOut"], "Fixture sessions remained active")
        except Exception:
            errors.append("Fixture user-data or session restoration failed")
        if backed_up:
            try:
                device.restore()
                report["deviceStoresRestored"] = True
            except Exception:
                errors.append("Device stores did not restore byte-for-byte")
        if owns_commands:
            try:
                device.cleanup_commands()
                base.require(not device.exists(device.command_remote) and not device.exists(device.restore_remote) and
                             not device.exists("files/command.json", True) and not device.exists("files/command.tmp", True), "Owned commands remain")
                report["commandsRemoved"] = True
            except Exception:
                errors.append("Owned command cleanup failed")
        if mapping_created:
            try:
                device.adb("reverse", "--remove", mapping)
                base.require(mapping not in device.adb("reverse", "--list").stdout.decode().split(), "Owned reverse mapping remains")
                report["reverseMappingRemoved"] = True
            except Exception:
                errors.append("Owned reverse mapping cleanup failed")
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
        report["cleanupErrors"] = errors
        if errors:
            report["passed"] = False
        persist()
        print("Device watched report: " + str(validation / "report.json"), flush=True)
        if errors:
            raise RuntimeError("; ".join(errors))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--adb", default=base.DEFAULT_ADB)
    parser.add_argument("--media", required=True)
    parser.add_argument("--duration-ms", type=int, required=True)
    run(parser.parse_args())
