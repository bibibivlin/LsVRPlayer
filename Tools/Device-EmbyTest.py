"""Exercise Emby browsing and original-video playback on a development PICO APK.

Uses only the supplied server. Credentials stay out of command-line arguments and
reports. Device stores and modified server user-data are restored in finally.
"""
import argparse
import copy
from datetime import datetime, timedelta, timezone
import getpass
import json
import os
from pathlib import Path
import subprocess
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid


ROOT = Path(__file__).resolve().parent.parent
PACKAGE = "com.linxyi.LsVRPlayer"
EXTERNAL = "/sdcard/Android/data/" + PACKAGE + "/files/"
DEFAULT_ADB = r"C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
SHANGHAI = timezone(timedelta(hours=8))


class CheckFailure(Exception):
    """Messages in this exception are authored checks, never server responses."""


def require(condition, message):
    if not condition:
        raise CheckFailure(message)


def wall_comparison(wall, expected):
    items = expected.get("Items", [])
    expected_ids = [item["Id"] for item in items]
    actual_ids = wall.get("itemIds") or []
    first = next((index for index, (actual, wanted) in enumerate(zip(actual_ids, expected_ids)) if actual != wanted), -1)
    if first == -1 and len(actual_ids) != len(expected_ids):
        first = min(len(actual_ids), len(expected_ids))
    return {"expectedIdCount": len(expected_ids), "actualIdCount": len(actual_ids),
            "expectedTotalCount": expected.get("TotalRecordCount"), "reportedItemCount": wall.get("itemCount"),
            "reportedTotalCount": wall.get("totalCount"), "startIndex": wall.get("startIndex"),
            "firstMismatchIndex": first, "idOrderMatches": actual_ids == expected_ids,
            "missingIdCount": len(set(expected_ids)-set(actual_ids)),
            "unexpectedIdCount": len(set(actual_ids)-set(expected_ids)),
            "expectedSortNamePresentCount": sum(bool(item.get("SortName")) for item in items)}


def normalized_server(address):
    parsed = urllib.parse.urlsplit(address.strip())
    port = parsed.port or (443 if parsed.scheme.lower() == "https" else 80)
    return parsed.scheme.lower(), (parsed.hostname or "").lower(), port, parsed.path.rstrip("/")


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, new_url):
        return None


class Server:
    def __init__(self, address):
        parsed = urllib.parse.urlsplit(address.strip())
        require(parsed.scheme in ("http", "https") and parsed.hostname and not parsed.username and
                not parsed.password and not parsed.query and not parsed.fragment,
                "Server must be an HTTP(S) address without URL credentials, query or fragment")
        self.address = address.strip().rstrip("/")
        self.token = ""
        self.user = ""
        self.opener = urllib.request.build_opener(NoRedirect())
        self.device_id = "lsvrplayer-validation-" + uuid.uuid4().hex

    def request(self, path, method="GET", payload=None):
        headers = {"Accept": "application/json", "Authorization":
                   'Emby Client="LsVRPlayer validation", Device="Validation", DeviceId="' + self.device_id + '", Version="1.0"'}
        if self.token:
            headers["X-Emby-Token"] = self.token
        body = None
        if payload is not None:
            body = json.dumps(payload).encode("utf-8")
            headers["Content-Type"] = "application/json"
        request = urllib.request.Request(self.address + path, data=body, headers=headers, method=method)
        try:
            with self.opener.open(request, timeout=30) as response:
                raw = response.read(8 * 1024 * 1024 + 1)
            require(len(raw) <= 8 * 1024 * 1024, "Server JSON response exceeded the validation limit")
            return json.loads(raw) if raw else None
        except (urllib.error.URLError, json.JSONDecodeError):
            raise CheckFailure("Server API request failed; no private response was recorded") from None

    def login(self, username, password):
        result = self.request("/Users/AuthenticateByName", "POST", {"Username": username, "Pw": password})
        self.token = result.get("AccessToken", "")
        self.user = result.get("User", {}).get("Id", "")
        require(self.token and self.user, "Independent server login failed")

    def user_path(self, suffix):
        return "/Users/" + urllib.parse.quote(self.user, safe="") + suffix

    def item(self, item_id):
        return self.request(self.user_path("/Items/" + urllib.parse.quote(item_id, safe="")))

    def children(self, parent_id, start=0, collection_type="", page_size=30):
        query = urllib.parse.urlencode({"ParentId": parent_id, "StartIndex": start, "Limit": page_size,
                                      "Recursive": "false", "SortBy": "SortName", "SortOrder": "Ascending",
                                      "EnableTotalRecordCount": "true", "Fields": "Overview,People,MediaSources,SortName"})
        if collection_type.lower() == "movies":
            query = urllib.parse.urlencode({"ParentId": parent_id, "StartIndex": start, "Limit": page_size,
                                          "Recursive": "true", "IncludeItemTypes": "Movie", "SortBy": "SortName", "SortOrder": "Ascending",
                                          "EnableTotalRecordCount": "true", "Fields": "Overview,People,MediaSources,SortName"})
        return self.request(self.user_path("/Items?") + query)

    def all_children(self, parent_id, collection_type=""):
        items, start = [], 0
        while start < 10000:
            page = self.children(parent_id, start, collection_type, page_size=200)
            batch = page.get("Items", [])
            items.extend(batch)
            require(len({item["Id"] for item in items}) == len(items), "Library query returned duplicate item IDs")
            if len(items) >= page.get("TotalRecordCount", len(items)):
                items.sort(key=lambda item: ((item.get("SortName") or item.get("Name", "")).upper(), item["Id"]))
                return {"Items": items, "TotalRecordCount": len(items), "StartIndex": 0}
            require(batch, "Library query ended before its reported total")
            start += len(batch)
        raise CheckFailure("Library query exceeded the bounded validation catalogue")

    def movies(self):
        query = urllib.parse.urlencode({"Recursive": "true", "IncludeItemTypes": "Movie,Episode,Video",
                                      "MediaTypes": "Video", "Limit": 80, "Fields": "People,Overview,MediaSources"})
        items = self.request(self.user_path("/Items?") + query).get("Items", [])
        candidates = [item for item in items if item.get("RunTimeTicks", 0) >= 90 * 10_000_000]

        def rank(item):
            source = next(iter(item.get("MediaSources", [])), {})
            video = next((stream for stream in source.get("MediaStreams", []) if stream.get("Type") == "Video"), {})
            codec = video.get("Codec", "").lower()
            return (codec in ("h264", "hevc", "h265"), 0 < video.get("Width", 0) <= 4096,
                    bool(item.get("People")), item.get("Type") == "Movie")

        candidates.sort(key=rank, reverse=True)
        require(len(candidates) >= 2, "Server needs two accessible videos of at least 90 seconds for independent-film regression")
        return [self.item(item["Id"]) for item in candidates[:2]]

    def restore_user_data(self, item_id, original):
        path = self.user_path("/Items/" + urllib.parse.quote(item_id, safe="") + "/UserData")
        defaults = {"PlaybackPositionTicks": 0, "Played": False, "PlayCount": 0,
                    "IsFavorite": False, "LastPlayedDate": None}
        supported = set(defaults) | {"Rating", "PlayedPercentage", "Key", "ItemId", "UnplayedItemCount"}
        payload = {**defaults, **{key: value for key, value in original.items() if key in supported}}
        current = self.item(item_id).get("UserData", {})
        if original.get("LastPlayedDate") is None and current.get("LastPlayedDate") is not None:
            # Emby 4.10 ignores a null LastPlayedDate in UserData updates. Its
            # official unplayed endpoint clears the date; restore the other
            # backed-up fields immediately afterward and verify every field.
            self.request(self.user_path("/PlayedItems/" + urllib.parse.quote(item_id, safe="")), "DELETE")
        self.request(path, "POST", payload)
        restored = self.item(item_id).get("UserData", {})
        for key, default in defaults.items():
            require(restored.get(key, default) == original.get(key, default), "Original server user-data did not restore: " + key)
        if "Rating" in original:
            require(restored.get("Rating") == original["Rating"], "Original server user-data did not restore: Rating")


class Device:
    def __init__(self, args, run_id):
        self.args = args
        self.run_id = run_id
        self.command_local = ROOT / "TestMedia" / ("emby-command-" + run_id + ".json")
        self.command_remote = "/data/local/tmp/vrplayer-emby-" + run_id + ".json"
        self.restore_remote = "/data/local/tmp/vrplayer-emby-restore-" + run_id + ".bin"
        self.backups = []
        self.awake = threading.Event()
        self.wake_thread = None

    def adb(self, *command, check=True):
        result = subprocess.run([self.args.adb, "-s", self.args.serial, *command],
                                capture_output=True, timeout=40)
        if check and result.returncode:
            raise CheckFailure("ADB operation failed; check the development APK and device connection")
        return result

    def read(self, path, private=False):
        command = ("exec-out", "run-as", PACKAGE, "cat", path) if private else ("exec-out", "cat", path)
        return self.adb(*command).stdout

    def exists(self, path, private=False):
        command = ("shell", "run-as", PACKAGE, "test", "-f", path) if private else ("shell", "test", "-f", path)
        result = self.adb(*command, check=False)
        require(result.returncode in (0, 1), "Could not inspect a device backup target")
        return result.returncode == 0

    def remove(self, path, private=False):
        command = ("shell", "run-as", PACKAGE, "rm", "-f", path) if private else ("shell", "rm", "-f", path)
        self.adb(*command)

    def backup(self, validation):
        self.adb("shell", "am", "force-stop", PACKAGE)
        self.adb("shell", "run-as", PACKAGE, "id")
        entries = [("settings", EXTERNAL + "settings.json", False),
                   ("accounts", EXTERNAL + "emby-accounts.json", False),
                   ("credentials", "shared_prefs/emby_accounts.xml", True),
                   ("credentials-backup", "shared_prefs/emby_accounts.xml.bak", True)]
        manifest = {}
        for label, path, private in entries:
            present = self.exists(path, private)
            data = self.read(path, private) if present else None
            local = validation / ("emby-" + self.run_id + "-" + label + ".backup." + ("xml" if private else "json"))
            if present:
                local.write_bytes(data)
            self.backups.append({"label": label, "path": path, "private": private, "bytes": data, "local": local})
            manifest[label] = {"present": present}
        (validation / ("emby-" + self.run_id + "-stores.backup.json")).write_text(json.dumps(manifest, indent=2), encoding="utf-8")

    def write_external(self, path, data):
        local = ROOT / "TestMedia" / ("emby-settings-" + self.run_id + ".json")
        local.parent.mkdir(parents=True, exist_ok=True)
        try:
            local.write_bytes(data)
            self.adb("push", str(local), path)
        finally:
            local.unlink(missing_ok=True)

    def restore(self):
        self.adb("shell", "am", "force-stop", PACKAGE)
        errors = []
        for entry in self.backups:
            try:
                if entry["bytes"] is None:
                    self.remove(entry["path"], entry["private"])
                    require(not self.exists(entry["path"], entry["private"]), "Absent device store was not removed")
                elif entry["private"]:
                    self.adb("push", str(entry["local"]), self.restore_remote)
                    try:
                        self.adb("shell", "run-as", PACKAGE, "mkdir", "-p", "shared_prefs")
                        self.adb("shell", "run-as", PACKAGE, "cp", self.restore_remote, entry["path"])
                    finally:
                        self.remove(self.restore_remote)
                    require(self.read(entry["path"], True) == entry["bytes"], "Private account vault was not restored byte-for-byte")
                else:
                    self.adb("push", str(entry["local"]), entry["path"])
                    require(self.read(entry["path"]) == entry["bytes"], "External device store was not restored byte-for-byte")
            except Exception:
                errors.append("Device store restoration failed: " + entry["label"])
        require(not errors, "; ".join(errors))

    def launch(self):
        self.adb("shell", "input", "keyevent", "KEYCODE_WAKEUP")
        self.adb("shell", "monkey", "-p", PACKAGE, "-c", "android.intent.category.LAUNCHER", "1")

        def wake():
            while not self.awake.wait(1):
                try:
                    self.adb("shell", "input", "keyevent", "KEYCODE_WAKEUP", check=False)
                except Exception:
                    pass

        self.wake_thread = threading.Thread(target=wake, daemon=True)
        self.wake_thread.start()

    def command(self, action, **fields):
        self.command_local.parent.mkdir(parents=True, exist_ok=True)
        try:
            self.adb("shell", "run-as", PACKAGE, "rm", "-f", "files/command-result.json")
            try:
                self.command_local.write_text(json.dumps({"action": action, **fields}, ensure_ascii=False), encoding="utf-8")
                self.adb("push", str(self.command_local), self.command_remote)
            finally:
                self.command_local.unlink(missing_ok=True)
            self.adb("shell", "run-as", PACKAGE, "cp", self.command_remote, "files/command.tmp")
            self.adb("shell", "run-as", PACKAGE, "mv", "files/command.tmp", "files/command.json")
            self.remove(self.command_remote)
            return self.wait(lambda: json.loads(self.read("files/command-result.json", True)),
                             lambda value: value.get("accepted") and value.get("action") == action,
                             "Development command was not acknowledged", timeout=35)
        finally:
            self.command_local.unlink(missing_ok=True)
            self.remove(self.command_remote)

    def inspect(self):
        return self.command("inspect")

    def status(self):
        return json.loads(self.read(EXTERNAL + "playback-diagnostics.json"))

    @staticmethod
    def wait(read, predicate, failure, timeout=75):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            try:
                value = read()
                if predicate(value):
                    return value
            except (CheckFailure, json.JSONDecodeError, KeyError):
                pass
            time.sleep(.45)
        raise CheckFailure(failure)

    def page(self, name, **expected):
        return self.wait(self.inspect, lambda value: value.get("source") == "Emby" and
                         value["emby"].get("page") == name and not value["emby"].get("loading") and
                         not value["emby"].get("error") and
                         all(value["emby"].get(key) == item for key, item in expected.items()),
                         "Expected Emby page did not load: " + name)

    def playback(self, item_id, resume=False):
        generation = self.status()["snapshot"]["generation"]
        self.command("emby-play", uri=item_id, flag=resume)
        return self.wait(self.status, lambda value: value["snapshot"]["generation"] > generation and
                         value["snapshot"]["state"] == 3 and not value["snapshot"].get("error"),
                         "Original video did not prepare in the current playback backend", timeout=120)

    def settled_reports(self):
        self.wait(self.inspect, lambda value: value["emby"].get("pendingReportCount") == 0,
                  "Emby progress reports did not finish before restoration", timeout=95)

    def capture(self, name):
        self.command("menu", flag=True)
        self.command("tab", name="文件")
        self.command("recenter", flag=False)
        time.sleep(1)
        target = ROOT / "Docs/Captures" / ("emby-" + self.run_id + "-" + name + ".png")
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(self.adb("exec-out", "screencap", "-p").stdout)
        require(target.stat().st_size > 1000, "Device screenshot was empty")

    def cleanup_commands(self):
        self.command_local.unlink(missing_ok=True)
        self.remove(self.command_remote)
        self.remove(self.restore_remote)
        for path in ("files/command.json", "files/command.tmp"):
            self.remove(path, True)


def continuous_playback(device, seconds=15):
    device.command("play")
    first = device.wait(device.status, lambda value: value["snapshot"]["playing"], "Playback did not start")
    generation = first["snapshot"]["generation"]
    start_position = last_position = first["snapshot"]["position"]
    start_frames = first.get("frames", 0)
    start = last_progress = time.monotonic()
    presence_known = bool(first.get("presenceKnown"))
    while time.monotonic() - start < seconds:
        value = device.status()
        snapshot = value["snapshot"]
        require("hasFocus" in value and "presenceKnown" in value, "APK is missing focus/presence diagnostics")
        require(value["hasFocus"] and (not value["presenceKnown"] or value["userPresent"]),
                "Focus or headset presence interrupted the continuous playback run")
        presence_known = presence_known and bool(value["presenceKnown"])
        require(snapshot["generation"] == generation and snapshot["state"] == 3 and snapshot["playing"] and
                not snapshot.get("error"), "Playback paused, changed generation or buffered during the continuous run")
        position = snapshot["position"]
        require(position >= last_position, "Playback position moved backwards during the continuous run")
        if position > last_position:
            last_position = position
            last_progress = time.monotonic()
        require(time.monotonic() - last_progress < 5, "Playback diagnostics stopped advancing")
        time.sleep(.7)
    require(last_position - start_position >= seconds * 700 and device.status().get("frames", 0) > start_frames,
            "Continuous run did not advance decoded video for the required duration")
    return {"seconds": seconds, "headsetPresenceKnown": presence_known,
            "headsetPresenceEvidence": "Sensor reported presence throughout" if presence_known else "Sensor unavailable; no headset-presence evidence",
            "positionAdvancedMs": last_position - start_position}


def run(args):
    run_id = datetime.now(SHANGHAI).strftime("%Y%m%d-%H%M%S") + "-" + uuid.uuid4().hex[:6]
    validation = ROOT / "Docs/Validation"
    validation.mkdir(parents=True, exist_ok=True)
    output = validation / ("emby-device-" + run_id + ".json")
    report = {"started": datetime.now(SHANGHAI).isoformat(), "passed": False, "checks": [],
              "limitations": ["Short original-video run; no long-film compatibility claim.",
                              "Development commands do not validate physical controller interaction or headset comfort.",
                              "Private posters, media names and server addresses are excluded from this report."]}
    device = Device(args, run_id)
    server = Server(args.server)
    password = os.environ.get("EMBY_TEST_PASSWORD")
    if password is None:
        password = getpass.getpass("Emby test password: ")
    movies = []
    original_data = {}
    account_id = ""
    original_account_ids = set()
    launched = False
    failure = None
    cleanup_errors = []

    def passed(name, **metrics):
        report["checks"].append({"name": name, "passed": True, **metrics})
        print("PASS " + name, flush=True)

    try:
        device.backup(validation)
        original_settings = next(entry["bytes"] for entry in device.backups if entry["label"] == "settings")
        original_accounts = next(entry["bytes"] for entry in device.backups if entry["label"] == "accounts")
        accounts = json.loads(original_accounts).get("accounts", []) if original_accounts else []
        original_account_ids = {entry.get("id") for entry in accounts}
        server.login(args.username, password)
        require(not any(entry.get("userId") == server.user and
                        normalized_server(entry.get("serverUrl", "")) == normalized_server(args.server)
                        for entry in accounts),
                "Use a dedicated test account that is not already registered on this device; existing sessions will not be reused")
        settings = json.loads(original_settings) if original_settings else {"version": 3}
        settings.update(version=3, autoLoadSubtitles=False, autoHide=False, formatDetection=0,
                        loop=False, sequential=False, speed=1, volume=0, history=[])
        device.write_external(EXTERNAL + "settings.json", json.dumps(settings).encode("utf-8"))
        views = server.request(server.user_path("/Views")).get("Items", [])
        require(views, "Test account has no media libraries")
        libraries = [(view, server.all_children(view["Id"], collection_type=view.get("CollectionType", ""))) for view in views]
        library, children = max(libraries, key=lambda pair: pair[1].get("TotalRecordCount", 0))
        movies = server.movies()
        for index, movie in enumerate(movies):
            original_data[movie["Id"]] = copy.deepcopy(movie.get("UserData", {}))
            (validation / ("emby-" + run_id + "-media-" + str(index) + ".backup.json")).write_text(
                json.dumps({"itemId": movie["Id"], "userData": original_data[movie["Id"]]}, indent=2), encoding="utf-8")
        passed("Independent server authentication and two original videos discovered", libraryCount=len(views))
        device.launch()
        launched = True
        device.wait(device.status, lambda value: value["snapshot"]["generation"] == 0,
                    "Development app did not launch into a fresh playback session")
        device.command("emby-login", uri=args.server, username=args.username, password=password, flag=False)
        password = ""
        home = device.page("Home")
        account_id = home["emby"]["accountId"]
        require(home["emby"]["viewCount"] == len(views) and
                set(home["emby"]["viewIds"]) == {view["Id"] for view in views}, "Home did not show the account's accessible media libraries")
        passed("Home libraries, continue-watching and latest sections loaded", libraries=home["emby"]["viewCount"],
               resumeItems=home["emby"]["resumeCount"], latestItems=home["emby"]["latestCount"])
        if args.capture:
            device.capture("home")
        device.command("emby-library", uri=library["Id"], name="验证媒体库", collectionType=library.get("CollectionType", ""))
        page = device.page("Library", startIndex=0)
        report["wallCompare"] = wall_comparison(page["emby"], children)
        require(page["emby"]["itemCount"] == page["emby"]["totalCount"] == children.get("TotalRecordCount", 0),
                "Complete library wall count does not match the server")
        require(page["emby"]["itemIds"] == [item["Id"] for item in children.get("Items", [])], "Poster wall items do not match the server order")
        passed("Complete library poster wall matches all server metadata", itemCount=page["emby"]["itemCount"], totalCount=page["emby"]["totalCount"])
        if args.capture:
            device.capture("library")
        movie = movies[0]
        item_id = movie["Id"]
        device.command("emby-item", uri=item_id, name="验证影片")
        details = device.page("Details", selectedId=item_id)
        require(details["emby"]["castCount"] == len(movie.get("People", [])), "Details did not preserve server cast metadata")
        passed("Film details and cast metadata loaded", castCount=details["emby"]["castCount"],
               overviewAvailable=bool(movie.get("Overview")), posterAvailable=bool(movie.get("ImageTags", {}).get("Primary")))
        if args.capture:
            device.capture("details")
        playback = device.playback(item_id, resume=False)
        snapshot = playback["snapshot"]
        require(snapshot["duration"] > 0 and snapshot["width"] > 0 and snapshot["height"] > 0, "Media3 did not report decoded original-video metadata")
        source = next(iter(movie.get("MediaSources", [])), {})
        video = next((stream for stream in source.get("MediaStreams", []) if stream.get("Type") == "Video"), {})
        if video.get("Width") and video.get("Height"):
            require((snapshot["width"], snapshot["height"]) == (video["Width"], video["Height"]), "Playback dimensions differ from the original server source")
        passed("Original source uses current playback backend without changing dimensions", width=snapshot["width"], height=snapshot["height"])
        passed("Continuous original-video playback with focus and presence guard", **continuous_playback(device))
        device.command("pause")
        device.wait(device.status, lambda value: not value["snapshot"]["playing"], "Pause did not take effect")
        target = min(snapshot["duration"] - 20_000, max(60_000, int(snapshot["duration"] * .2)))
        device.command("seek", position=target)
        device.wait(device.status, lambda value: value["snapshot"]["state"] == 3 and
                    not value["snapshot"]["playing"] and abs(value["snapshot"]["position"] - target) < 1500,
                    "Paused seeking did not reach the requested position")
        passed("Authenticated original-video seeking preserves paused state")
        device.command("projection", projection=2, stereo=1)
        device.wait(device.status, lambda value: value["projection"] == "Equirect360" and value["stereo"] == "SideBySide",
                    "Manual VR format did not apply through the current presenter")
        device.command("stop")
        device.settled_reports()
        saved_position = server.item(item_id).get("UserData", {}).get("PlaybackPositionTicks", 0) // 10_000
        require(abs(saved_position - target) < 2000, "Stopped playback progress was not synchronized to Emby")
        passed("Stopped playback progress synchronizes to the server")
        second = movies[1]
        device.playback(second["Id"], resume=False)
        device.command("projection", projection=0, stereo=0)
        device.command("pause")
        device.command("stop")
        device.settled_reports()
        resumed = device.playback(item_id, resume=True)
        require(saved_position - 2000 <= resumed["snapshot"]["position"] <= saved_position + 10_000,
                "Continue playback did not resume the server position")
        require(resumed["projection"] == "Equirect360" and resumed["stereo"] == "SideBySide",
                "Ordinary Emby reopening lost the saved manual VR format")
        device.command("pause")
        device.command("stop")
        device.settled_reports()
        stored = json.loads(device.read(EXTERNAL + "settings.json"))
        first_uri = "emby://" + account_id + "/" + item_id
        second_uri = "emby://" + account_id + "/" + second["Id"]
        records = {entry["uri"]: entry for entry in stored.get("history", [])}
        require(first_uri in records and second_uri in records, "History did not store stable per-account Emby identities")
        require(records[first_uri].get("formatLocked") and records[first_uri]["view"]["projection"] == 2 and
                records[first_uri]["view"]["stereo"] == 1 and records[second_uri]["view"]["projection"] == 0 and
                records[second_uri]["view"]["stereo"] == 0, "Independent films did not retain their own manual formats")
        require("accessToken" not in json.dumps(stored) and "api_key" not in json.dumps(stored), "Playback history contains authentication data")
        passed("Server resume and independent per-film VR formats persist under stable identities")
        recent = device.command("source", name="recent")
        require(recent.get("source") == "Recent" and first_uri in {entry.get("uri") for entry in recent.get("files", [])},
                "Existing Recent source cannot see Emby playback history")
        passed("Existing Recent page includes the Emby playback record")
        device.command("emby-home")
        device.command("source", name="local")
        time.sleep(4)
        require(device.inspect().get("source") == "Home", "An obsolete Emby request replaced the local source")
        passed("Returning to local cancels obsolete Emby browsing")
        report["passed"] = True
    except Exception as error:
        failure = error
        report["failure"] = str(error) if isinstance(error, CheckFailure) else type(error).__name__
    finally:
        if launched:
            try:
                device.command("stop")
                device.settled_reports()
            except Exception:
                cleanup_errors.append("Final app playback reports did not finish")
            temporary_accounts = {account_id} if account_id and account_id not in original_account_ids else set()
            try:
                path = EXTERNAL + "emby-accounts.json"
                if device.exists(path):
                    metadata = json.loads(device.read(path))
                    require(isinstance(metadata, dict) and isinstance(metadata.get("accounts"), list),
                            "Temporary account metadata is malformed")
                    for entry in metadata["accounts"]:
                        require(isinstance(entry, dict) and isinstance(entry.get("id"), str) and bool(entry["id"]),
                                "Temporary account metadata is malformed")
                        if entry["id"] not in original_account_ids:
                            temporary_accounts.add(entry["id"])
            except Exception:
                cleanup_errors.append("Temporary account recovery from device metadata failed")
            try:
                current_account = device.inspect().get("emby", {}).get("accountId", "")
                if isinstance(current_account, str) and current_account and current_account not in original_account_ids:
                    temporary_accounts.add(current_account)
            except Exception:
                cleanup_errors.append("Temporary account recovery from app diagnostics failed")
            for temporary_account in temporary_accounts:
                try:
                    device.command("emby-remove", name=temporary_account)
                    device.settled_reports()
                except Exception:
                    cleanup_errors.append("Temporary account removal or logout failed")
        device.awake.set()
        if device.wake_thread is not None:
            device.wake_thread.join(timeout=45)
        try:
            device.adb("shell", "am", "force-stop", PACKAGE)
        except Exception:
            cleanup_errors.append("Could not stop the app before restoring stores")
        for item_id, data in original_data.items():
            try:
                server.restore_user_data(item_id, data)
            except Exception:
                cleanup_errors.append("Original server user-data restoration or verification failed")
        if server.token:
            try:
                server.request("/Sessions/Logout", "POST", {})
                server.token = ""
            except Exception:
                cleanup_errors.append("Independent server validation session logout failed")
        try:
            device.restore()
            report["deviceStoresRestored"] = True
        except Exception:
            cleanup_errors.append("Device store restoration or byte verification failed")
        try:
            device.cleanup_commands()
            report["temporaryCommandsRemoved"] = True
        except Exception:
            cleanup_errors.append("Private temporary command cleanup failed")
        report["serverUserDataRestored"] = bool(original_data) and not any("user-data" in error for error in cleanup_errors)
        if cleanup_errors:
            report["passed"] = False
            report["cleanupErrors"] = cleanup_errors
        report["finished"] = datetime.now(SHANGHAI).isoformat()
        output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print("PASS Emby device regression" if report["passed"] else "FAIL Emby device regression", flush=True)
    print("Private validation record: " + str(output), flush=True)
    if failure:
        print("Failure: " + report["failure"], flush=True)
    for error in cleanup_errors:
        print("Cleanup failure: " + error, flush=True)
    return 0 if report["passed"] else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--server", required=True, help="User-designated Emby server URL")
    parser.add_argument("--username", required=True)
    parser.add_argument("--adb", default=DEFAULT_ADB)
    parser.add_argument("--capture", action="store_true", help="Save private Home/Library/Details screenshots to ignored Docs/Captures")
    return run(parser.parse_args())


if __name__ == "__main__":
    raise SystemExit(main())
