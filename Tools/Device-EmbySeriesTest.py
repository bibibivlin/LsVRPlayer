"""Check real Emby TV hierarchy, artwork and Back on a development PICO APK.

Uses a designated existing account which must not already be on the device.
No video is played and no watched/progress mutation is requested. Four device
stores plus the original command-result file are restored byte-for-byte; every
sampled server UserData dictionary is verified unchanged. Screenshots and private
metadata stay under ignored Docs/Validation. Keep the headset present/focused.
"""
import argparse
import copy
from datetime import datetime
import getpass
import importlib.util
import json
import os
from pathlib import Path
import threading
import time
import urllib.parse
import uuid

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("series_device_parts", ROOT / "Tools/Device-EmbyMoviePartsTest.py")
parts = importlib.util.module_from_spec(spec)
spec.loader.exec_module(parts)
base = parts.base


def collect(api, path, query):
    items, seen, start = [], set(), 0
    while start < 10000:
        result = api.request(path + "?" + urllib.parse.urlencode({**query, "StartIndex": start, "Limit": 200,
            "EnableTotalRecordCount": "true", "EnableImages": "true", "EnableUserData": "true"}))
        batch = result.get("Items", [])
        for item in batch:
            base.require(item["Id"] not in seen, "TV endpoint repeated an item across pages")
            seen.add(item["Id"])
            items.append(item)
        start += len(batch)
        if start >= result.get("TotalRecordCount", start):
            return items
        base.require(batch, "TV endpoint ended before its reported total")
    raise base.CheckFailure("TV catalogue exceeded the validation bound")


def seasons(api, identity):
    return collect(api, "/Shows/" + urllib.parse.quote(identity, safe="") + "/Seasons", {"UserId": api.user})


def episodes(api, series_id, season_id, search=""):
    return collect(api, "/Shows/" + urllib.parse.quote(series_id, safe="") + "/Episodes",
                   {"UserId": api.user, "SeasonId": season_id, "SearchTerm": search})


def discover(api):
    shows = collect(api, api.user_path("/Items"), {"Recursive": "true", "IncludeItemTypes": "Series",
                    "Fields": "ParentId,SortName", "SortBy": "SortName", "SortOrder": "Ascending"})
    # Prefer the two user-provided reference examples when present; no names enter reports.
    shows.sort(key=lambda show: (show.get("Name") not in ("白色相簿", "缘之空"), show.get("Name", "")))
    found = {}
    for show in shows[:120]:
        entries = seasons(api, show["Id"])
        regular = [entry for entry in entries if entry.get("IndexNumber", -1) != 0]
        special = [entry for entry in entries if entry.get("IndexNumber") == 0]
        if len(regular) == 1:
            kind = "single-with-specials" if special else "single"
        elif len(regular) > 1:
            kind = "multiple"
        else:
            continue
        found.setdefault(kind, {"show": show, "seasons": entries, "regular": regular, "special": special})
        if len(found) == 3:
            break
    base.require("multiple" in found and any(key.startswith("single") for key in found),
                 "Designated server lacks both single-season and multi-season samples")
    return found, len(shows)


def run(args):
    base.PACKAGE = parts.PACKAGE = args.package
    base.EXTERNAL = parts.EXTERNAL = "/sdcard/Android/data/" + args.package + "/files/"
    run_id = datetime.now(base.SHANGHAI).strftime("%Y%m%d-%H%M%S") + "-" + uuid.uuid4().hex[:6]
    validation = ROOT / "Docs/Validation/EmbySeries" / run_id
    validation.mkdir(parents=True)
    captures = validation / "captures"
    captures.mkdir()
    report = {"runId": run_id, "started": datetime.now(base.SHANGHAI).isoformat(), "passed": False,
              "scope": "real designated Emby server and PICO browsing; no playback", "checks": [],
              "limitations": ["Development pointers and Back do not verify physical controller bindings or worn-headset comfort.",
                              "No long-film or playback compatibility acceptance."]}
    api = base.Server(args.server)
    device = parts.GuardedDevice(args, run_id)
    monitor_stop = threading.Event()
    monitor_thread = None
    monitor_errors = []
    original_data = {}
    original_ids = set()
    temporary_id = ""
    owns_commands = backed_up = launched = False
    usb_original = None

    def persist():
        report["focusSamples"] = device.focus_samples
        (validation / "report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

    def guard():
        if monitor_errors:
            raise parts.PresenceFailure(monitor_errors[0])

    def monitor():
        while not monitor_stop.wait(1):
            try:
                device.status()
            except Exception as error:
                monitor_errors.append(str(error))
                return

    def passed(name, **fields):
        guard()
        report["checks"].append({"name": name, "passed": True, **fields})
        persist()
        print("PASS " + name, flush=True)

    def labels():
        return device.command("localization-inspect").get("labels", [])

    def capture(name):
        guard()
        device.command("ui-pointer", name="", flag=False, preset=0)
        time.sleep(3)
        device.status()
        path = captures / (name + ".png")
        path.write_bytes(device.adb("exec-out", "screencap", "-p").stdout)
        base.require(path.stat().st_size > 40000, "Headset capture was not reviewable")

    def click(key):
        for pressed in (False, True, False):
            device.command("ui-pointer", name=key, flag=pressed, value=.5, preset=0)

    def back():
        device.command("ui-pointer", name="", flag=False, preset=0)
        for pressed in (False, True, False):
            device.command("ui-back", preset=1, flag=pressed, valid=True)

    def remember(item):
        if item["Id"] not in original_data:
            original_data[item["Id"]] = copy.deepcopy(api.item(item["Id"]).get("UserData", {}))

    def wall(expected, total=None):
        ids = [item["Id"] for item in expected]
        return device.page("Library", itemIds=ids, itemCount=len(ids), totalCount=len(ids) if total is None else total)

    try:
        password = os.environ.get("EMBY_TEST_PASSWORD") or getpass.getpass("Emby test password: ")
        api.login(args.username, password)
        if device.exists(base.EXTERNAL + "emby-accounts.json"):
            current = json.loads(device.read(base.EXTERNAL + "emby-accounts.json"))
            base.require(not any(entry.get("userId") == api.user and
                base.normalized_server(entry.get("serverUrl", "")) == base.normalized_server(api.address)
                for entry in current.get("accounts", [])), "Use a dedicated account not already registered on this device")
        samples, show_count = discover(api)
        report["availableSampleKinds"] = list(samples)
        report["seriesCount"] = show_count
        print("Discovered TV samples: " + ", ".join(samples), flush=True)
        (validation / "private-samples.json").write_text(json.dumps(samples, ensure_ascii=False), encoding="utf-8")
        base.require(not device.exists(device.command_remote) and not device.exists(device.restore_remote), "Owned staging paths already exist")
        base.require(not device.exists("files/command.json", True) and not device.exists("files/command.tmp", True), "Another development command is pending")
        owns_commands = True
        device.backup(validation)
        backed_up = True
        for label, result_path, private in (("command-result", "files/command-result.json", True),
                ("privacy-consent", base.EXTERNAL + "privacy-consent.json", False)):
            result_bytes = device.read(result_path, private) if device.exists(result_path, private) else None
            result_local = validation / (label + ".backup.json")
            if result_bytes is not None:
                result_local.write_bytes(result_bytes)
            device.backups.append({"label": label, "path": result_path, "private": private,
                                   "bytes": result_bytes, "local": result_local})
        if args.consent_from_package:
            base.require(args.consent_from_package != args.package, "Consent source must be a separate existing installation")
            source = "/sdcard/Android/data/" + args.consent_from_package + "/files/privacy-consent.json"
            base.require(device.exists(source), "Existing installation has no consent record to reuse")
            device.write_external(base.EXTERNAL + "privacy-consent.json", device.read(source))
        original_accounts = next(entry["bytes"] for entry in device.backups if entry["label"] == "accounts")
        if original_accounts:
            original_ids = {entry["id"] for entry in json.loads(original_accounts).get("accounts", [])}
        usb_original = device.adb("shell", "settings", "get", "global", "stay_on_while_plugged_in").stdout.strip()
        settings_bytes = next(entry["bytes"] for entry in device.backups if entry["label"] == "settings")
        settings = json.loads(settings_bytes) if settings_bytes else {"version": 3}
        settings.update(version=3, autoHide=False, autoLoadSubtitles=False, formatDetection=0, language=1, volume=0, history=[])
        device.write_external(base.EXTERNAL + "settings.json", json.dumps(settings).encode())
        launch_time = time.time()
        device.launch()
        launched = True
        device.begin_guard(launch_time)
        monitor_thread = threading.Thread(target=monitor, daemon=True)
        monitor_thread.start()
        device.command("emby-login", uri=api.address, username=args.username, password=password, flag=False)
        password = None
        home = device.page("Home")
        temporary_id = home["emby"]["accountId"]
        base.require(temporary_id not in original_ids, "Test account was already registered")
        device.command("menu", flag=True)
        device.command("tab", name="文件")
        for kind, sample in samples.items():
            guard()
            show = sample["show"]
            remember(show)
            for season in sample["seasons"]:
                remember(season)
            device.command("emby-home")
            device.page("Home")
            # The metadata is resolved by the app; no fabricated Season/Series type is injected.
            device.command("emby-library", uri=show["Id"], name="验证剧集")
            if kind.startswith("single"):
                regular = sample["regular"][0]
                expected = episodes(api, show["Id"], regular["Id"])
                special = [episode for season in sample["special"] for episode in episodes(api, show["Id"], season["Id"])]
                wall(expected, len(expected) + len(special))
                for item in expected + special:
                    remember(item)
                texts = labels()
                base.require(not any(entry["parent"] == "Emby view selector" for entry in texts), "Episode page retained the view selector")
                if special:
                    base.require(any(entry["text"] == "特别篇" for entry in texts), "Single-season page omitted its separate Specials section")
                capture(kind)
                passed("Single-season hierarchy matches official episode order: " + kind, episodes=len(expected), specials=len(special))
                if expected:
                    click(temporary_id + ":" + expected[0]["Id"])
                    device.page("Details", selectedId=expected[0]["Id"])
                    capture(kind + "-episode-details")
                    back()
                    wall(expected, len(expected) + len(special))
                    passed("Episode details and global Back restore the direct single-season page: " + kind)
            else:
                wall(sample["seasons"])
                base.require(not any(entry["parent"] == "Emby view selector" for entry in labels()), "Season page retained the view selector")
                capture("multiple-seasons")
                missing = sum(not season.get("ImageTags", {}).get("Primary") for season in sample["seasons"])
                passed("Multi-season page matches official seasons and hides the view selector", seasons=len(sample["seasons"]), missingSeasonPosters=missing)
                for index, season in enumerate(sample["regular"][:2]):
                    expected = episodes(api, show["Id"], season["Id"])
                    for item in expected:
                        remember(item)
                    click(temporary_id + ":" + season["Id"])
                    wall(expected)
                    capture("multiple-season-" + str(index + 1))
                    if expected:
                        click(temporary_id + ":" + expected[0]["Id"])
                        device.page("Details", selectedId=expected[0]["Id"])
                        back()
                        wall(expected)
                    back()
                    wall(sample["seasons"])
                    passed("Season card, episode card and global Back preserve hierarchy: " + str(index + 1), episodes=len(expected))
            back()
            device.page("Home")
        base.require(device.status()["snapshot"]["generation"] == 0, "Browsing unexpectedly started video playback")
        passed("TV browsing never starts video or changes playback generation")
        guard()
        report["passed"] = True
    except Exception as error:
        report["error"] = str(error) if isinstance(error, (base.CheckFailure, parts.PresenceFailure)) else type(error).__name__
        raise
    finally:
        errors = []
        monitor_stop.set()
        if monitor_thread:
            monitor_thread.join(timeout=5)
        device.guarded = False
        report["continuousPresencePassed"] = bool(monitor_thread) and not monitor_errors
        if monitor_errors:
            report["passed"] = False
        if launched:
            try:
                device.command("stop")
                device.settled_reports()
                if temporary_id and temporary_id not in original_ids:
                    device.command("emby-remove", name=temporary_id)
                    device.settled_reports()
                report["deviceTestSessionLoggedOut"] = True
            except Exception:
                errors.append("Test-account logout or report drain failed")
        device.awake.set()
        if device.wake_thread:
            device.wake_thread.join(timeout=5)
        if launched:
            try:
                device.adb("shell", "am", "force-stop", base.PACKAGE)
            except Exception:
                errors.append("Application force-stop failed")
        try:
            for identity, original in original_data.items():
                if api.item(identity).get("UserData", {}) != original:
                    api.restore_user_data(identity, original)
                base.require(api.item(identity).get("UserData", {}) == original, "Server UserData did not restore exactly")
            report["serverUserDataRestored"] = len(original_data)
            if api.token:
                api.request("/Sessions/Logout", "POST", {})
            report["independentSessionLoggedOut"] = True
        except Exception:
            errors.append("Server UserData verification or independent logout failed")
        # Remove only this run's command/staging paths before restoring the original result file.
        if owns_commands:
            try:
                device.cleanup_commands()
                base.require(not device.exists(device.command_remote) and not device.exists(device.restore_remote)
                    and not device.exists("files/command.json", True) and not device.exists("files/command.tmp", True), "Owned commands remain")
                report["commandsRemoved"] = True
            except Exception:
                errors.append("Owned command cleanup failed")
        if backed_up:
            try:
                device.restore()
                report["deviceStoresRestored"] = True
            except Exception:
                errors.append("Device stores did not restore byte-for-byte")
        if usb_original is not None:
            try:
                base.require(device.adb("shell", "settings", "get", "global", "stay_on_while_plugged_in").stdout.strip() == usb_original,
                             "USB setting changed unexpectedly")
                report["usbSettingUnchanged"] = True
            except Exception:
                errors.append("USB setting verification failed")
        report["cleanupErrors"] = errors
        if errors:
            report["passed"] = False
        persist()
        print("TV device report: " + str(validation / "report.json"), flush=True)
        if errors:
            raise RuntimeError("; ".join(errors))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--adb", default=base.DEFAULT_ADB)
    parser.add_argument("--server", required=True)
    parser.add_argument("--username", required=True)
    parser.add_argument("--package", default=base.PACKAGE, help="Installed development package, including a separate validation application ID")
    parser.add_argument("--consent-from-package", help="Reuse the user's existing consent record for the same product in a separate validation installation")
    run(parser.parse_args())
