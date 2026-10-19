"""Check panel Grip gestures and Emby artwork sizes on a PICO development APK.

Uses an owned loopback fixture and shared uGUI/XR diagnostic input paths.
Physical Grip bindings and worn-headset comfort require separate acceptance.
Restores six device files and every changed fixture UserData field exactly.
"""
import argparse
import copy
from datetime import datetime
import importlib.util
import json
from pathlib import Path
import re
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


parts = module("panel_artwork_parts", ROOT / "Tools/Device-EmbyMoviePartsTest.py")
base = parts.base
fixture = module("panel_artwork_fixture", ROOT / "Tools/Serve-EmbyMoviePartsFixture.py")


def run(args):
    base.require(re.fullmatch(r"com\.linxyi\.LsVRPlayer\.[A-Za-z0-9]+", args.package),
                 "Use a separate development validation package")
    base.require(Path(args.media).is_file() and args.duration_ms > 0, "Provide an existing generated MP4 and its actual duration")
    base.PACKAGE = parts.PACKAGE = args.package
    base.EXTERNAL = parts.EXTERNAL = "/sdcard/Android/data/" + args.package + "/files/"
    run_id = datetime.now(base.SHANGHAI).strftime("%Y%m%d-%H%M%S-") + uuid.uuid4().hex[:8]
    validation = ROOT / "Docs/Validation/PanelArtwork" / run_id
    validation.mkdir(parents=True)
    captures = validation / "captures"
    captures.mkdir()
    device = parts.GuardedDevice(args, run_id)
    state = fixture.FixtureState(args.media, args.duration_ms, artwork=True)
    original_data = copy.deepcopy(state.user_data)
    server = fixture.FixtureServer(("127.0.0.1", 0), state)
    server_thread = threading.Thread(target=server.serve_forever, daemon=True)
    server_thread.start()
    mapping = "tcp:" + str(server.server_address[1])
    api = base.Server("http://127.0.0.1:" + str(server.server_address[1]))
    report = {"passed": False, "checks": [], "runId": run_id, "section": args.section,
              "scope": "PICO development input paths with owned synthetic Emby over adb reverse",
              "limitations": ["Physical Grip binding, readability and binocular comfort are separate acceptance."]}
    monitor_stop = threading.Event()
    monitor_error = []
    monitor_thread = None
    backed_up = mapping_created = launched = owns_commands = False
    temporary_id = ""
    original_ids = set()
    usb_original = None
    consent_source = consent_bytes = None

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

    def idle(seconds):
        deadline = time.monotonic() + seconds
        while time.monotonic() < deadline:
            check()
            time.sleep(min(.15, max(0, deadline-time.monotonic())))

    def passed(name, **metrics):
        check()
        report["checks"].append({"name": name, "passed": True, **metrics})
        persist()
        print("PASS " + name, flush=True)

    def click(name):
        for pressed in (False, True, False):
            device.command("ui-pointer", preset=0, name=name, value=.5, flag=pressed)
        return device.inspect()

    def stick(hand, vertical=0, horizontal=0):
        device.command("ui-stick", preset=hand, offset=vertical, value=horizontal)

    def grip(hand, pressed):
        device.command("ui-grip", preset=hand, flag=pressed)

    def neutral():
        for hand in (0, 1):
            stick(hand)
            grip(hand, False)

    def capture(name):
        device.command("ui-pointer", preset=0, name="", flag=False)
        device.command("menu", flag=True)
        idle(.5)
        device.status()
        target = captures / (name + ".png")
        target.write_bytes(device.adb("exec-out", "screencap", "-p").stdout)
        base.require(target.stat().st_size > 40000, "Device screenshot was not reviewable")

    def wall():
        return device.inspect()["wall"]

    def choose_size(size):
        click("Emby view selector")
        base.require(wall()["menuOpen"], "View menu did not open through the pointer")
        click("Emby artwork size " + size)
        value = device.wait(wall, lambda value: value["size"] == size and not value["menuOpen"] and value["poolCount"] > 0,
                            "Artwork size did not rebuild the real wall")
        saved = json.loads(device.read(base.EXTERNAL + "settings.json")).get("embyLibraryViews", [])
        base.require(any(entry.get("accountId") == temporary_id and entry.get("libraryId") == "movies" and
                         entry.get("size") == {"Large": 0, "Medium": 1, "Small": 2}[size] for entry in saved),
                     "Device settings did not persist the selected library artwork size")
        return value

    try:
        base.require(mapping not in device.adb("reverse", "--list").stdout.decode().split(), "Reverse mapping already exists")
        base.require(not device.exists(device.command_remote) and not device.exists(device.restore_remote), "Owned staging paths already exist")
        base.require(not device.exists("files/command.json", True) and not device.exists("files/command.tmp", True), "Another development command is pending")
        owns_commands = True
        device.backup(validation)
        backed_up = True
        for label, path, private in (("command-result", "files/command-result.json", True),
                                     ("privacy-consent", base.EXTERNAL + "privacy-consent.json", False)):
            data = device.read(path, private) if device.exists(path, private) else None
            local = validation / (label + ".backup.json")
            if data is not None:
                local.write_bytes(data)
            device.backups.append({"label": label, "path": path, "private": private, "bytes": data, "local": local})
        accounts_bytes = next(entry["bytes"] for entry in device.backups if entry["label"] == "accounts")
        if accounts_bytes:
            original_ids = {entry["id"] for entry in json.loads(accounts_bytes).get("accounts", [])}
        if args.consent_from_package:
            base.require(args.consent_from_package != args.package, "Consent source must be another existing installation")
            consent_source = "/sdcard/Android/data/" + args.consent_from_package + "/files/privacy-consent.json"
            base.require(device.exists(consent_source), "No existing product consent is available")
            consent_bytes = device.read(consent_source)
            base.require(json.loads(consent_bytes).get("version") == "2026-10-17.1", "Existing consent does not match the current product policy")
            device.write_external(base.EXTERNAL + "privacy-consent.json", consent_bytes)
        usb_original = device.adb("shell", "settings", "get", "global", "stay_on_while_plugged_in").stdout.strip()
        original_settings = next(entry["bytes"] for entry in device.backups if entry["label"] == "settings")
        settings = json.loads(original_settings) if original_settings else {"version": 3}
        settings.update(version=3, language=1, autoHide=False, autoLoadSubtitles=False, formatDetection=0,
                        loop=False, sequential=False, volume=0, speed=1, history=[], mainPanelDistance=(6 if args.section == "visibility" else 2), embyLibraryViews=[])
        device.write_external(base.EXTERNAL + "settings.json", json.dumps(settings).encode())
        device.adb("reverse", mapping, mapping)
        mapping_created = True
        api.login("fixture", "fixture")
        launch_time = time.time()
        device.launch()
        launched = True
        device.begin_guard(launch_time)
        monitor_thread = threading.Thread(target=monitor, daemon=True)
        monitor_thread.start()
        device.command("menu", flag=True)
        device.command("tab", name="文件")
        device.command("ui-input-reset")
        neutral()
        if args.section != "visibility":
            initial = device.inspect()
            base.require(abs(initial["mainPanelDistance"]-2) < .01, "Main panel did not start at its existing default")
            base.require(device.status()["snapshot"]["generation"] == 0, "No-film gesture fixture unexpectedly opened media")
            for hand in (0, 1):
                before = device.inspect()
                grip(hand, True)
                stick(hand, -1)
                idle(2.7)
                stick(hand)
                near = device.inspect()
                base.require(abs(near["mainPanelDistance"]-1.2) < .01, "Grip and stick did not reach the near limit")
                delta = sum((near["panelPosition"][axis]-before["panelPosition"][axis])*before["panelForward"][axis] for axis in ("x", "y", "z"))
                base.require(abs(delta-(near["mainPanelDistance"]-before["mainPanelDistance"])) < .01, "Canvas did not move by the recorded distance")
                base.require(near["panelScale"] == before["panelScale"], "Panel distance changed its physical scale")
                stick(hand, 1)
                idle(2.6)
                stick(hand)
                far = device.inspect()
                base.require(abs(far["mainPanelDistance"]-6) < .01, "Grip and stick did not reach the far limit")
                grip(hand, False)
                stick(hand, -1)
                idle(.3)
                stick(hand)
                base.require(abs(device.inspect()["mainPanelDistance"]-6) < .01, "Unmodified vertical stick moved the panel")
                passed("Grip gesture, actual Canvas motion, bounds and normal-stick isolation for hand " + str(hand))

            neutral()
            grip(0, True)
            stick(0, -1)
            idle(1.4)
            stick(0)
            grip(0, False)
            saved_distance = device.inspect()["mainPanelDistance"]
            if saved_distance < 1.5:
                grip(0, True)
                stick(0, .35)
                idle(.6)
                stick(0)
                grip(0, False)
                saved_distance = device.inspect()["mainPanelDistance"]
            capture("main-panel-distance")
            persisted = json.loads(device.read(base.EXTERNAL + "settings.json"))
            base.require(abs(persisted["mainPanelDistance"]-saved_distance) < .01, "Neutral did not persist panel distance")
            device.command("ui-input-reset")
            device.guarded = False
            device.adb("shell", "am", "force-stop", args.package)
            launch_time = time.time()
            device.adb("shell", "monkey", "-p", args.package, "-c", "android.intent.category.LAUNCHER", "1")
            device.begin_guard(launch_time)
            device.command("menu", flag=True)
            base.require(abs(device.inspect()["mainPanelDistance"]-saved_distance) < .01, "Restart lost the independent panel distance")
            passed("Panel distance persists across a real Android process restart", distance=saved_distance)

        else:
            base.require(abs(device.inspect()["mainPanelDistance"]-6) < .01, "Far-distance startup seed was not loaded")
            capture("main-panel-far-no-film")
            passed("Far-distance main panel renders at startup beyond the blank video screen")

        device.command("emby-login", uri=api.address, username="fixture", password="fixture", flag=False)
        home = device.page("Home")
        temporary_id = home["emby"]["accountId"]
        base.require(temporary_id not in original_ids, "Fixture account was already registered")
        device.command("emby-library", uri="movies", name="Fixture films", collectionType="movies")
        device.page("Library", itemCount=205)
        neutral()
        if args.section == "all":
            measurements = {}
            labels = {"Poster": "海报", "Banner": "横幅图", "Disc": "光盘封面", "Logo": "徽标", "Thumb": "缩略图", "List": "列表", "Table": "表格"}
            for view, label in labels.items():
                if wall()["view"] != view:
                    click("Emby view selector")
                    click(label)
                    device.wait(wall, lambda value: value["view"] == view and value["poolCount"] > 0, "View did not change through its button")
                sizes = {}
                for size in ("Large", "Medium", "Small"):
                    value = choose_size(size)
                    base.require(value["itemCount"] == 205 and 0 < value["poolCount"] < 100, "Wall lost items or created an unbounded card pool")
                    sizes[size] = {key: value[key] for key in ("columns", "artworkWidth", "artworkHeight", "poolCount")}
                    if view in ("Poster", "Thumb", "Disc"):
                        capture(view + "-" + size)
                base.require(sizes["Large"]["artworkWidth"] > sizes["Medium"]["artworkWidth"] > sizes["Small"]["artworkWidth"], "Artwork sizes were not strictly ordered")
                ratios = [value["artworkWidth"]/value["artworkHeight"] for value in sizes.values()]
                base.require(max(ratios)-min(ratios) < .001, "Artwork sizing changed image proportions")
                measurements[view] = sizes
                passed("Three actual UI sizes retain artwork ratio and bounded pooling: " + view, sizes=sizes)

            click("Emby view selector")
            click("海报")
            choose_size("Small")
            neutral()
            stick(0, -1)
            device.wait(wall, lambda value: value["offset"] > 300, "Unmodified stick did not scroll the library")
            stick(0)
            small_offset = wall()["offset"]
            choose_size("Large")
            base.require(wall()["offset"] < .1, "Large artwork reused the smaller layout's scroll position")
            choose_size("Small")
            base.require(abs(wall()["offset"]-small_offset) < 1, "Size selection lost its independent scroll position")
            click("Emby view selector")
            offset_before = wall()["offset"]
            stick(0, -1)
            idle(1)
            base.require(abs(wall()["offset"]-offset_before) < 1, "Open view menu allowed background stick scrolling")
            stick(0)
            device.command("ui-back", preset=0, flag=False)
            device.command("ui-back", preset=0, flag=True)
            device.command("ui-back", preset=0, flag=False)
            base.require(not wall()["menuOpen"] and device.inspect()["emby"]["page"] == "Library", "Back did not close only the size menu")
            passed("Independent size offsets, unmodified stick scrolling and Back-first menu behavior")
            neutral()
            stick(0, -1)
            device.wait(wall, lambda value: any(key.endswith(":movie-203") for key in value["boundKeys"]), "Last film was not reachable in the small virtualized wall", timeout=40)
            stick(0)
            base.require(wall()["poolCount"] < 100, "Scrolling expanded the card pool with the catalogue")
            passed("Small artwork wall reaches the last of 205 films with bounded row reuse")

        else:
            value = choose_size("Small")
            base.require(value["columns"] == 7 and value["itemCount"] == 205, "Far-distance poster wall did not retain the small size")
            capture("Poster-Small-far")
            passed("Far-distance Emby wall retains interactive size controls and bounded small posters")

        device.command("emby-play", uri="ordinary", flag=False)
        device.wait(device.status, lambda value: value["snapshot"]["state"] == 3 and value["snapshot"]["generation"] > 0, "Fixture original video did not prepare", timeout=60)
        device.command("pause")
        device.command("menu", flag=True)
        neutral()
        grip(0, True)
        stick(0, 1)
        idle(2.7)
        stick(0)
        grip(0, False)
        base.require(abs(device.inspect()["mainPanelDistance"]-6) < .01, "Playing-film panel did not reach the far bound")
        capture("main-panel-far-playing")
        passed("Main panel remains rendered at 6 m while the paused video screen is at 3.5 m")
        before = device.status()
        distance_before = device.inspect()["mainPanelDistance"]
        grip(1, True)
        stick(1, -1)
        idle(.4)
        stick(1)
        grip(1, False)
        after = device.status()
        base.require(device.inspect()["mainPanelDistance"] < distance_before, "Playing-film panel gesture did not move the panel")
        base.require(before["distance"] == after["distance"] and before["panoramaZoom"] == after["panoramaZoom"], "Panel gesture changed video distance or panorama zoom")
        device.command("quick-menu", flag=True)
        neutral()
        quick_distance = device.inspect()["mainPanelDistance"]
        grip(1, True)
        stick(1, 1)
        idle(.4)
        stick(1)
        idle(.3)
        base.require(abs(device.inspect()["mainPanelDistance"]-quick_distance) < .01, "Quick panel changed the full panel distance")
        neutral()
        passed("Panel movement during playback preserves video geometry and stays isolated from quick controls")
        report["imageRequestCount"] = len(state.image_requests)
        base.require(report["imageRequestCount"] > 0, "No real device image requests reached the synthetic service")
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
                device.command("ui-input-reset")
                device.command("stop")
                device.settled_reports()
                current = device.inspect().get("emby", {}).get("accountId", "")
                if current and current not in original_ids:
                    temporary_id = current
                if temporary_id:
                    device.command("emby-remove", name=temporary_id)
                    device.settled_reports()
            except Exception:
                errors.append("Input reset, progress drain or fixture-account logout failed")
        device.awake.set()
        if device.wake_thread:
            device.wake_thread.join(timeout=5)
        try:
            device.adb("shell", "am", "force-stop", args.package)
        except Exception:
            errors.append("Test app force-stop failed")
        try:
            for identity, data in original_data.items():
                if state.user_data[identity] != data:
                    api.restore_user_data(identity, data)
            report["serverUserDataRestored"] = all(state.user_data[identity] == data for identity, data in original_data.items())
            base.require(report["serverUserDataRestored"], "Fixture UserData did not restore exactly")
            if api.token:
                api.request("/Sessions/Logout", "POST", {})
            report["sessionsLoggedOut"] = not state.tokens and not state.playing
            base.require(report["sessionsLoggedOut"], "Fixture sessions remained active")
        except Exception:
            errors.append("Fixture user-data or session restoration failed")
        if owns_commands:
            try:
                device.cleanup_commands()
                base.require(not device.exists(device.command_remote) and not device.exists(device.restore_remote) and
                             not device.exists("files/command.json", True) and not device.exists("files/command.tmp", True), "Owned command paths remain")
                report["commandsRemoved"] = True
            except Exception:
                errors.append("Owned command cleanup failed")
        if backed_up:
            try:
                device.restore()
                report["deviceStoresRestored"] = True
            except Exception:
                errors.append("Device stores did not restore byte-for-byte")
        if mapping_created:
            try:
                device.adb("reverse", "--remove", mapping)
                base.require(mapping not in device.adb("reverse", "--list").stdout.decode().split(), "Owned reverse mapping remains")
                report["reverseMappingRemoved"] = True
            except Exception:
                errors.append("Owned reverse cleanup failed")
        if usb_original is not None:
            try:
                report["usbSettingUnchanged"] = device.adb("shell", "settings", "get", "global", "stay_on_while_plugged_in").stdout.strip() == usb_original
                base.require(report["usbSettingUnchanged"], "USB setting changed unexpectedly")
            except Exception:
                errors.append("USB setting verification failed")
        if consent_source:
            try:
                base.require(device.read(consent_source) == consent_bytes, "Existing product consent record changed")
                report["sourceConsentUnchanged"] = True
            except Exception:
                errors.append("Original product consent changed")
        server.shutdown()
        server.server_close()
        server_thread.join(timeout=5)
        if server_thread.is_alive():
            errors.append("Owned fixture server did not stop")
        report["cleanupErrors"] = errors
        if errors:
            report["passed"] = False
        persist()
        print("Device report: " + str(validation / "report.json"), flush=True)
        if errors:
            raise RuntimeError("; ".join(errors))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--section", choices=("all", "visibility"), default="all")
    parser.add_argument("--adb", default=base.DEFAULT_ADB)
    parser.add_argument("--package", required=True)
    parser.add_argument("--media", required=True)
    parser.add_argument("--duration-ms", required=True, type=int)
    parser.add_argument("--consent-from-package")
    run(parser.parse_args())
