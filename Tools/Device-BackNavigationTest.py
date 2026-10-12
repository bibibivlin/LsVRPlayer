"""Exercise global back and directory position restoration on a development APK.

Uses the shared VrUiInput pointer, stick and Y/B drivers on PICO hardware. It
does not establish physical button bindings or worn-headset comfort. The headset
must remain awake, focused and present for the entire run. No system settings are
changed. Only generated Recognition media and this run's owned fixture are used.
"""
import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import threading
import time
import uuid


ROOT = Path(__file__).resolve().parent.parent
PACKAGE = "com.linxyi.LsVRPlayer"
EXTERNAL = "/sdcard/Android/data/" + PACKAGE + "/files/"
MEDIA = "file:///sdcard/Movies/VRPlayerTest/Recognition/Unmarked-A.mp4"
ADB_DEFAULT = r"C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"


class BackNavigationRun:
    def __init__(self, args):
        self.args = args
        self.token = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ-") + uuid.uuid4().hex[:10]
        self.output = ROOT / "Docs/Validation" / ("back-navigation-" + self.token)
        self.output.mkdir(parents=True)
        self.fixture = self.output / "fixture"
        self.remote_fixture = "/sdcard/Movies/VRPlayerTest/BackNavigation-" + self.token
        self.fixture_uri = "file://" + self.remote_fixture
        self.remote_temp = "/data/local/tmp/vrplayer-back-" + self.token
        self.private_temp = "files/back-command-" + self.token + ".tmp"
        self.backups = []
        self.owned_fixture = False
        self.owned_temp = False
        self.fixture_creation_attempted = False
        self.temp_creation_attempted = False
        self.launch_time = None
        self.monitor_stop = threading.Event()
        self.monitor_thread = None
        self.monitor_error = None
        self.diag_stamp = None
        self.diag_frame = None
        self.diag_changed_at = None
        self.report = {
            "run": self.token, "passed": False, "checks": [],
            "input": "development Y/B, pointer and stick drivers through shared VrUiInput",
            "systemSettingsChanged": False,
            "limitations": ["Physical Y/B button bindings and worn-headset acceptance require manual operation.",
                            "Local generated directories and independent subtitle browsing are covered; SMB/SAF/Emby servers are not exercised."],
        }

    def adb(self, *arguments, check=True):
        result = subprocess.run([self.args.adb, "-s", self.args.serial, *arguments],
                                capture_output=True, timeout=30)
        if check and result.returncode:
            # Device output can contain user media or account details. Keep it in
            # the ignored run directory; do not include it in console exceptions.
            with (self.output / "adb-errors.bin").open("ab") as log:
                log.write(result.stdout + result.stderr + b"\n")
            raise RuntimeError("ADB operation failed; see this run's ignored adb-errors.bin")
        return result

    def exists(self, path, private=False):
        command = "if [ -e " + path + " ]; then echo present; else echo missing; fi"
        arguments = ("shell", "run-as " + PACKAGE + " sh -c '" + command + "'") if private else ("shell", command)
        value = self.adb(*arguments).stdout.strip()
        if value not in (b"present", b"missing"):
            raise RuntimeError("Cannot positively determine device file presence")
        return value == b"present"

    def read_bytes(self, path, private=False):
        arguments = ("exec-out", "run-as", PACKAGE, "cat", path) if private else ("exec-out", "cat", path)
        return self.adb(*arguments).stdout

    def backup_file(self, path, label, private=False):
        present = self.exists(path, private)
        local = self.output / (label + ".backup")
        if present:
            local.write_bytes(self.read_bytes(path, private))
        self.backups.append({"path": path, "private": private, "present": present, "local": local, "label": label})
        self.write_report()

    def guard(self):
        if self.monitor_error:
            raise AssertionError(self.monitor_error)

    def sleep(self, seconds):
        until = time.monotonic() + seconds
        while time.monotonic() < until:
            self.guard()
            time.sleep(min(.15, max(0, until - time.monotonic())))
        self.guard()

    @staticmethod
    def valid_presence(state):
        return state.get("hasFocus") is True and state.get("presenceKnown") is True and state.get("userPresent") is True

    def read_status(self):
        return json.loads(self.read_bytes(EXTERNAL + "playback-diagnostics.json"))

    def monitor(self):
        while not self.monitor_stop.wait(.4):
            try:
                state = self.read_status()
                stamp, frame = state.get("timestampUtc"), state.get("frame")
                if not stamp or not isinstance(frame, int):
                    raise AssertionError("APK lacks fresh diagnostic timestamp/frame fields")
                if not self.valid_presence(state):
                    raise AssertionError("Application focus or known headset presence was interrupted; restart the entire run")
                if self.diag_frame is not None and frame < self.diag_frame:
                    raise AssertionError("Diagnostic frame sequence restarted during the run")
                if stamp != self.diag_stamp:
                    self.diag_stamp, self.diag_frame = stamp, frame
                    self.diag_changed_at = time.monotonic()
                    safe = {key: state.get(key) for key in ("timestampUtc", "frame", "hasFocus", "presenceKnown", "userPresent", "uiSurface", "quickPopup", "fullOffset", "quickOffset")}
                    with (self.output / "presence-and-freshness.jsonl").open("a", encoding="utf-8") as log:
                        log.write(json.dumps(safe) + "\n")
                elif time.monotonic() - self.diag_changed_at > 6:
                    raise AssertionError("Playback diagnostics became stale for more than six seconds")
            except (json.JSONDecodeError, RuntimeError):
                # A file may be observed during its in-place write. A sustained
                # read failure still fails the continuous freshness requirement.
                if time.monotonic() - self.diag_changed_at > 6:
                    self.monitor_error = "Playback diagnostics could not be read continuously"
                    return
            except Exception as error:
                self.monitor_error = str(error)
                return

    def wait(self, read, predicate, label, timeout=30):
        until = time.monotonic() + timeout
        last = None
        while time.monotonic() < until:
            self.guard()
            try:
                last = read()
                if predicate(last):
                    return last
            except (json.JSONDecodeError, RuntimeError):
                pass
            self.sleep(.2)
        safe = {} if not isinstance(last, dict) else {key: last.get(key) for key in
            ("uiSurface", "quickPopup", "loading", "quickBrowserLoading", "fullCount", "quickBrowserCount", "fullOffset", "quickOffset", "fullModal", "keyboardVisible", "hasFocus", "presenceKnown", "userPresent")}
        raise AssertionError("Timed out: " + label + "; state=" + json.dumps(safe))

    def command(self, action, **fields):
        self.guard()
        packet = self.output / "command.json"
        packet.write_text(json.dumps({"action": action, **fields}, ensure_ascii=False), encoding="utf-8")
        self.adb("shell", "run-as", PACKAGE, "rm", "-f", "files/command-result.json")
        self.adb("push", str(packet), self.remote_temp + "/command.json")
        self.adb("shell", "run-as", PACKAGE, "cp", self.remote_temp + "/command.json", self.private_temp)
        self.adb("shell", "run-as", PACKAGE, "mv", self.private_temp, "files/command.json")
        result = self.wait(lambda: json.loads(self.read_bytes("files/command-result.json", True)),
                           lambda state: state.get("accepted") is True and state.get("action") == action,
                           "command acknowledgement for " + action)
        with (self.output / "command-results.jsonl").open("a", encoding="utf-8") as log:
            log.write(json.dumps(result, ensure_ascii=False) + "\n")
        if not self.valid_presence(result):
            raise AssertionError("Command observed interrupted focus or headset presence")
        self.guard()
        return result

    def inspect(self):
        return self.command("inspect")

    def wait_ui(self, predicate, label):
        return self.wait(self.inspect, predicate, label)

    def pointer(self, name="", pressed=False, value=.5):
        self.command("ui-pointer", name=name, flag=pressed, value=value, preset=0)

    def click(self, name):
        self.pointer(name)
        self.pointer(name, True)
        self.pointer(name)

    def release_back(self):
        self.command("ui-back", preset=2, flag=False, valid=True)
        self.sleep(.1)

    def back(self, hand, predicate, label, hold=0):
        self.release_back()
        self.command("ui-back", preset=hand, flag=True, valid=True)
        result = self.wait_ui(predicate, label)
        if hold:
            self.sleep(hold)
            assert predicate(self.inspect()), "Held back press repeated: " + label
        self.release_back()
        return result

    def directory(self, uri, quick=False):
        key = "quickBrowserUri" if quick else "directory"
        loading = "quickBrowserLoading" if quick else "loading"
        return self.wait_ui(lambda state: state.get(key, "").rstrip("/") == uri.rstrip("/") and not state.get(loading),
                            "completed fixture directory")

    def scroll(self, quick=False):
        field = "quickOffset" if quick else "fullOffset"
        self.pointer()
        self.command("ui-stick", preset=0, value=0, offset=0)
        self.command("ui-stick", preset=1, value=0, offset=0)
        self.sleep(.3)
        self.command("ui-stick", preset=0, value=0, offset=-.35)
        try:
            self.wait_ui(lambda state: state.get(field, 0) >= 1080, "stick scroll to D20")
        finally:
            self.command("ui-stick", preset=0, value=0, offset=0)
        self.sleep(.3)
        offset = self.inspect()[field]
        # D20 begins at 20 * 64 pixels. It must be visible before the pointer is
        # used, so a miss cannot silently turn into an unrelated trigger press.
        height = 472 if quick else 352
        assert 0 < offset <= 1280 and offset + height >= 1336, "D20 did not remain visible after stick neutral"
        return offset

    @staticmethod
    def same_offset(state, field, expected):
        return abs(state[field] - expected) <= 2

    def passed(self, name, **observed):
        self.guard()
        self.report["checks"].append({"name": name, "passed": True, **observed})
        self.write_report()
        print("PASS " + name, flush=True)

    def write_report(self):
        (self.output / "report.json").write_text(json.dumps(self.report, ensure_ascii=False, indent=2), encoding="utf-8")

    def create_fixture(self):
        self.fixture.mkdir()
        (self.fixture / ".owner").write_text(self.token, encoding="ascii")
        level = self.fixture
        for _ in range(3):
            for index in range(40):
                directory = level / ("D%02d" % index)
                directory.mkdir(exist_ok=True)
                (directory / ".keep").write_bytes(b"")
                (level / ("Caption%02d.srt" % index)).write_text("1\n00:00:00,000 --> 00:00:02,000\nGenerated back navigation fixture\n", encoding="utf-8")
            level = level / "D20"
        if self.exists(self.remote_fixture) or self.exists(self.remote_temp):
            raise RuntimeError("Run-owned device path unexpectedly already exists")
        self.temp_creation_attempted = True
        self.adb("shell", "mkdir", "-p", self.remote_temp)
        self.adb("push", str(self.fixture / ".owner"), self.remote_temp + "/.owner")
        self.owned_temp = True
        self.fixture_creation_attempted = True
        self.adb("shell", "mkdir", "-p", self.remote_fixture)
        self.adb("push", str(self.fixture / ".owner"), self.remote_fixture + "/.owner")
        self.owned_fixture = True
        assert self.read_bytes(self.remote_fixture + "/.owner").decode("ascii") == self.token
        self.adb("push", str(self.fixture) + "/.", self.remote_fixture)

    def prepare(self):
        self.adb("shell", "ls", "/sdcard/Movies/VRPlayerTest/Recognition/Unmarked-A.mp4")
        self.adb("shell", "am", "force-stop", PACKAGE)
        self.backup_file(EXTERNAL + "settings.json", "settings")
        self.backup_file(EXTERNAL + "playback-diagnostics.json", "diagnostics")
        self.backup_file("files/command.json", "command", True)
        self.backup_file("files/command-result.json", "command-result", True)
        self.create_fixture()
        settings = self.output / "fixture-settings.json"
        favorites = [{"name": "Shortcut%02d" % index,
                      "uri": self.fixture_uri + ("/D20/D20" if index == 20 else "/D%02d" % index)}
                     for index in range(40)]
        settings.write_text(json.dumps({"version": 3, "history": [], "favorites": favorites, "volume": .8, "speed": 1,
                                       "autoHide": False, "autoLoadSubtitles": True, "formatDetection": 1, "loop": True,
                                       "subtitleDistance": 3, "subtitleSize": 42, "subtitleOffset": -.75, "subtitleFollow": True}), encoding="utf-8")
        self.adb("push", str(settings), EXTERNAL + "settings.json")
        self.adb("shell", "rm", "-f", EXTERNAL + "playback-diagnostics.json")
        self.adb("shell", "run-as", PACKAGE, "rm", "-f", "files/command.json", "files/command-result.json")
        self.adb("shell", "monkey", "-p", PACKAGE, "-c", "android.intent.category.LAUNCHER", "1")
        initial = self.wait(self.read_status, lambda state: self.valid_presence(state) and bool(state.get("timestampUtc"))
                            and isinstance(state.get("frame"), int) and state.get("snapshot", {}).get("generation") == 0,
                            "fresh startup diagnostics with focus and known presence")
        self.diag_stamp, self.diag_frame = initial["timestampUtc"], initial["frame"]
        self.diag_changed_at = time.monotonic()
        self.monitor_thread = threading.Thread(target=self.monitor, daemon=True)
        self.monitor_thread.start()
        self.command("open", uri=MEDIA, name="Unmarked-A.mp4")
        self.wait(self.read_status, lambda state: state.get("snapshot", {}).get("state") == 3
                  and state.get("snapshot", {}).get("generation", 0) > 0, "generated playback ready")
        self.command("pause")
        self.wait(self.read_status, lambda state: state.get("snapshot", {}).get("playing") is False, "generated playback paused")
        self.command("recenter", flag=True)
        self.command("ui-input-reset")
        self.release_back()

    def run_main(self):
        root = self.fixture_uri
        child, grandchild = root + "/D20", root + "/D20/D20"
        self.command("source", name="home")
        self.command("tab", name="文件")
        self.command("menu", flag=True)
        self.command("browse", uri=root)
        assert self.directory(root)["fullCount"] == 40
        parent_offset = self.scroll()
        self.click(child)
        assert self.directory(child)["fullCount"] == 40
        child_offset = self.scroll()
        self.click(grandchild)
        assert self.directory(grandchild)["fullCount"] == 40
        state = self.back(1, lambda state: state["directory"].rstrip("/") == child and not state["loading"], "right B to parent")
        assert self.same_offset(state, "fullOffset", child_offset)
        self.passed("Right B restores the parent directory position", saved=child_offset, restored=state["fullOffset"])

        self.click(grandchild)
        self.directory(grandchild)
        self.click("上级")
        state = self.directory(child)
        assert self.same_offset(state, "fullOffset", child_offset)
        self.passed("Main up button restores the same directory position", saved=child_offset, restored=state["fullOffset"])
        self.back(0, lambda state: state["directory"].rstrip("/") == grandchild and not state["loading"], "Y reverses physical up navigation")
        state = self.back(1, lambda state: state["directory"].rstrip("/") == child and not state["loading"], "B reverses the preceding child open")
        assert self.same_offset(state, "fullOffset", child_offset)
        self.passed("Main back reverses physical up navigation through the visited directory history")

        self.click(grandchild)
        self.directory(grandchild)
        self.release_back()
        self.command("ui-back", preset=0, flag=True, valid=False)
        self.sleep(1.2)
        assert self.inspect()["directory"].rstrip("/") == grandchild
        self.command("ui-back", preset=0, flag=True, valid=True)
        self.sleep(1.2)
        assert self.inspect()["directory"].rstrip("/") == grandchild
        self.passed("Tracking recovery while Y is held requires release before returning")

        state = self.back(0, lambda state: state["directory"].rstrip("/") == child and not state["loading"], "left Y one-level return", hold=2.5)
        assert self.same_offset(state, "fullOffset", child_offset)
        self.passed("Held left Y returns one level and restores its position", saved=child_offset, restored=state["fullOffset"])
        state = self.back(2, lambda state: state["directory"].rstrip("/") == root and not state["loading"], "simultaneous Y/B single return", hold=2.5)
        assert self.same_offset(state, "fullOffset", parent_offset)
        self.passed("Simultaneous Y/B returns once and restores the outer position", saved=parent_offset, restored=state["fullOffset"])
        self.click("刷新")
        state = self.directory(root)
        assert self.same_offset(state, "fullOffset", parent_offset)
        self.passed("Main directory refresh retains its current scroll position")

        self.click("Input")
        self.wait_ui(lambda state: state["keyboardVisible"], "main virtual keyboard shown")
        self.back(0, lambda state: not state["keyboardVisible"] and state["directory"].rstrip("/") == root and state["uiSurface"] == "full", "Y closes keyboard first")
        self.passed("Y closes the active main keyboard before navigating")

        self.command("tab", name="设置")
        self.click("选择音轨 / 内嵌字幕")
        self.wait_ui(lambda state: state["fullModal"] == "tracks", "track secondary panel")
        self.back(1, lambda state: state["fullModal"] == "" and state["tab"] == "设置" and state["uiSurface"] == "full", "B closes secondary panel")
        self.passed("B closes the secondary panel while keeping the main panel")
        self.back(0, lambda state: state["uiSurface"] == "hidden", "Y closes main settings panel")
        self.command("menu", flag=True)
        self.command("tab", name="文件")
        self.command("source", name="home")
        self.back(1, lambda state: state["uiSurface"] == "hidden", "B closes main source panel")
        self.passed("Y/B closes main settings and source panels")

    def run_source_history(self):
        deep = self.fixture_uri + "/D20/D20"
        parent = self.fixture_uri + "/D20"
        self.command("menu", flag=True)
        self.command("tab", name="文件")
        self.click("本地")
        self.wait_ui(lambda state: state["source"] == "Home" and state["directory"] == "", "local source homepage")
        # The local homepage contains fixed storage roots rather than arbitrary
        # folder shortcuts. browse uses the same PlayerApp entry point as the
        # address bar, without enumerating user-owned Movies or storage roots.
        self.command("browse", uri=deep)
        self.directory(deep)
        self.back(0, lambda state: state["source"] == "Home" and state["directory"] == "" and state["uiSurface"] == "full",
                  "Y from direct deep address to local homepage")
        self.passed("Y from a direct deep address returns to the local source homepage")

        self.click("收藏")
        state = self.wait_ui(lambda state: state["source"] == "Favorites" and not state["loading"] and state["fullCount"] == 40,
                             "generated favorite shortcuts")
        saved_query = state["fullQuery"]
        favorites_offset = self.scroll()
        self.click(deep)
        self.directory(deep)
        state = self.back(1, lambda state: state["source"] == "Favorites" and state["directory"] == "" and not state["loading"],
                          "B from deep favorite shortcut to favorite source")
        assert state["uiSurface"] == "full" and state["fullCount"] == 40
        assert self.same_offset(state, "fullOffset", favorites_offset) and state["fullQuery"] == saved_query
        self.passed("B from a deep favorite shortcut restores its source view and position",
                    saved=favorites_offset, restored=state["fullOffset"])

        self.click(deep)
        self.directory(deep)
        self.click("上级")
        state = self.directory(parent)
        assert state["source"] == "Directory" and state["uiSurface"] == "full"
        self.passed("The up button opens the physical parent of a deep favorite shortcut")
        self.back(0, lambda state: state["directory"].rstrip("/") == deep and not state["loading"], "Y reverses the deep shortcut up navigation")
        state = self.back(1, lambda state: state["source"] == "Favorites" and not state["loading"], "B returns to favorite origin after reversing up")
        assert self.same_offset(state, "fullOffset", favorites_offset)
        self.passed("Back after physical up returns to the deep shortcut before its favorite origin")

        self.click(deep)
        self.directory(deep)
        self.click("设置")
        self.wait_ui(lambda state: state["tab"] == "设置", "settings tab opened during browsing")
        self.click("文件")
        self.directory(deep)
        state = self.back(0, lambda state: state["source"] == "Favorites" and not state["loading"], "Y keeps browsing history across top tabs")
        assert self.same_offset(state, "fullOffset", favorites_offset)
        self.passed("Changing the top panel tab preserves the directory's source return history")

        self.click(deep)
        self.directory(deep)
        self.click("字幕")
        self.wait_ui(lambda state: state["tab"] == "字幕" and state["uiSurface"] == "full", "main subtitle tab opened with older file history")
        self.click("手动选择 SRT")
        self.wait_ui(lambda state: state["tab"] == "文件" and state["directory"].rstrip("/") == deep
                     and state["fullCount"] == 80 and not state["loading"], "manual subtitle entry directory")
        state = self.back(0, lambda state: state["tab"] == "字幕" and state["uiSurface"] == "full",
                          "Y returns directly from manual SRT entry to its subtitle tab")
        assert state["source"] == "Directory" and state["directory"].rstrip("/") == deep
        self.passed("Main manual SRT entry returns to the subtitle tab without consuming older file history")

        self.click("手动选择 SRT")
        self.wait_ui(lambda state: state["tab"] == "文件" and state["directory"].rstrip("/") == deep
                     and state["fullCount"] == 80 and not state["loading"], "manual SRT entry reopened")
        nested = deep + "/D00"
        self.click(nested)
        self.directory(nested)
        state = self.back(1, lambda state: state["tab"] == "文件" and state["directory"].rstrip("/") == deep
                          and not state["loading"], "B reverses an internal manual SRT directory open")
        assert state["fullCount"] == 80 and state["uiSurface"] == "full"
        self.back(0, lambda state: state["tab"] == "字幕" and state["uiSurface"] == "full",
                  "Y leaves manual SRT browsing only at its entry boundary")
        self.click("文件")
        self.directory(deep)
        state = self.back(1, lambda state: state["source"] == "Favorites" and not state["loading"],
                          "B still returns through the older favorite file history")
        assert state["fullCount"] == 40 and self.same_offset(state, "fullOffset", favorites_offset)
        self.passed("Main manual SRT directory history returns to its entry before preserving the older source history",
                    saved=favorites_offset, restored=state["fullOffset"])

        self.click("收藏")
        self.wait_ui(lambda state: state["source"] == "Favorites" and state["fullCount"] == 40, "favorite source selected again")
        # The source view cache can restore D20's viewport directly; neutralize
        # sticks and require its row to remain visible before the real click.
        self.pointer()
        self.click(deep)
        self.directory(deep)
        self.click("本地")
        self.wait_ui(lambda state: state["source"] == "Home" and state["directory"] == "", "source switch clears browsing history")
        state = self.back(1, lambda state: state["uiSurface"] == "hidden", "B hides local homepage after source switch")
        assert state["source"] == "Home" and state["directory"] == ""
        self.passed("Changing source clears old favorite browsing history before returning")

    def run_quick(self):
        self.command("quick-menu", flag=True)
        self.click("Mode")
        self.wait_ui(lambda state: state["quickPopup"] == "mode", "quick mode popup")
        self.back(0, lambda state: state["quickPopup"] == "" and state["uiSurface"] == "quick", "Y closes quick popup", hold=1.5)
        self.back(1, lambda state: state["uiSurface"] == "hidden", "B closes quick panel")
        self.passed("Y closes a quick popup first; B then closes the quick panel")

        self.command("quick-menu", flag=True)
        self.click("Subtitles")
        self.wait_ui(lambda state: state["quickPopup"] == "subtitles", "quick subtitle popup")
        self.click("选择字幕文件")
        self.wait_ui(lambda state: state["quickPopup"] == "files" and not state["quickBrowserLoading"], "quick subtitle browser")
        self.back(0, lambda state: state["quickPopup"] == "subtitles" and state["uiSurface"] == "quick", "Y from initial subtitle directory to its opening popup")
        self.passed("Quick subtitle browser returns to its opening popup from the initial directory")
        self.click("选择字幕文件")
        self.wait_ui(lambda state: state["quickPopup"] == "files" and not state["quickBrowserLoading"], "quick subtitle browser reopened")
        before = self.inspect()
        root = self.fixture_uri
        child, grandchild = root + "/D20", root + "/D20/D20"
        self.command("quick-browse", uri=root)
        assert self.directory(root, True)["quickBrowserCount"] == 80
        parent_offset = self.scroll(True)
        self.click(child)
        assert self.directory(child, True)["quickBrowserCount"] == 80
        child_offset = self.scroll(True)
        self.click(grandchild)
        self.directory(grandchild, True)
        state = self.back(1, lambda state: state["quickBrowserUri"].rstrip("/") == child and not state["quickBrowserLoading"], "B to quick browser parent")
        assert self.same_offset(state, "quickOffset", child_offset)
        self.passed("Quick subtitle B return restores its independent parent position", saved=child_offset, restored=state["quickOffset"])
        self.click(grandchild)
        self.directory(grandchild, True)
        self.click("上级")
        state = self.directory(child, True)
        assert self.same_offset(state, "quickOffset", child_offset)
        self.passed("Quick subtitle up button restores the same parent position", saved=child_offset, restored=state["quickOffset"])
        self.back(0, lambda state: state["quickBrowserUri"].rstrip("/") == grandchild and not state["quickBrowserLoading"], "Y reverses quick physical up navigation")
        state = self.back(1, lambda state: state["quickBrowserUri"].rstrip("/") == child and not state["quickBrowserLoading"], "B reverses quick child open")
        assert self.same_offset(state, "quickOffset", child_offset)
        self.passed("Quick subtitle back reverses physical up through the visited history")
        state = self.back(0, lambda state: state["quickBrowserUri"].rstrip("/") == root and not state["quickBrowserLoading"], "Y to quick outer directory")
        assert self.same_offset(state, "quickOffset", parent_offset)
        assert state["source"] == before["source"] and state["directory"] == before["directory"]
        self.passed("Quick subtitle Y return restores the outer position and preserves main source", saved=parent_offset, restored=state["quickOffset"])
        self.click("刷新")
        state = self.directory(root, True)
        assert self.same_offset(state, "quickOffset", parent_offset)
        self.passed("Quick subtitle refresh retains its current scroll position")
        self.click("来源")
        self.wait_ui(lambda state: state["quickBrowserUri"] == "" and state["quickPopup"] == "files", "quick browser sources")
        state = self.back(1, lambda state: state["quickBrowserUri"].rstrip("/") == root and not state["quickBrowserLoading"], "B from sources to the directory that opened it")
        assert self.same_offset(state, "quickOffset", parent_offset)
        self.back(0, lambda state: state["quickBrowserUri"] == before["quickBrowserUri"] and not state["quickBrowserLoading"], "Y to the original subtitle directory")
        self.back(1, lambda state: state["quickPopup"] == "subtitles" and state["uiSurface"] == "quick", "B from sources to subtitles")
        self.back(0, lambda state: state["quickPopup"] == "" and state["uiSurface"] == "quick", "Y from subtitles to quick bar")
        self.back(1, lambda state: state["uiSurface"] == "hidden", "B from quick bar to hidden")
        self.passed("Quick source list returns through visited directories, subtitle popup and bar in order")

    def restore(self):
        self.monitor_stop.set()
        if self.monitor_thread:
            self.monitor_thread.join(timeout=5)
        errors = []
        if self.monitor_error:
            self.report["passed"] = False
            self.report["continuityError"] = self.monitor_error
        try:
            self.adb("shell", "am", "force-stop", PACKAGE)
        except Exception:
            errors.append("Cannot force-stop player before restoring its files")
        for backup in self.backups:
            try:
                if backup["present"]:
                    if backup["private"]:
                        if not self.owned_temp:
                            # Backup may fail before owned paths are created.
                            # Existing private files have not yet been mutated.
                            assert self.read_bytes(backup["path"], True) == backup["local"].read_bytes()
                        else:
                            stage = self.remote_temp + "/restore-" + backup["label"]
                            self.adb("push", str(backup["local"]), stage)
                            self.adb("shell", "run-as", PACKAGE, "cp", stage, backup["path"])
                    else:
                        self.adb("push", str(backup["local"]), backup["path"])
                    assert self.read_bytes(backup["path"], backup["private"]) == backup["local"].read_bytes()
                else:
                    arguments = ("shell", "run-as", PACKAGE, "rm", "-f", backup["path"]) if backup["private"] else ("shell", "rm", "-f", backup["path"])
                    self.adb(*arguments)
                    assert not self.exists(backup["path"], backup["private"])
                backup["restored"] = True
            except Exception:
                backup["restored"] = False
                errors.append("Byte-for-byte/absence restoration failed: " + backup["label"])
        try:
            self.adb("shell", "run-as", PACKAGE, "rm", "-f", self.private_temp)
        except Exception:
            errors.append("Run-owned private command staging cleanup failed")
        for path, owned, expected_prefix in ((self.remote_fixture, self.owned_fixture, "/sdcard/Movies/VRPlayerTest/BackNavigation-"),
                                             (self.remote_temp, self.owned_temp, "/data/local/tmp/vrplayer-back-")):
            if not owned:
                continue
            try:
                # Both the exact generated path and ownership marker must match
                # before any recursive deletion on the device.
                assert path == expected_prefix + self.token
                assert self.read_bytes(path + "/.owner").decode("ascii") == self.token
                self.adb("shell", "rm", "-rf", path)
                assert not self.exists(path)
            except Exception:
                errors.append("Run-owned fixture/staging cleanup failed")
        for path, attempted, owned in ((self.remote_fixture, self.fixture_creation_attempted, self.owned_fixture),
                                       (self.remote_temp, self.temp_creation_attempted, self.owned_temp)):
            if attempted and not owned:
                try:
                    if self.exists(path):
                        errors.append("A partially created run path could not be verified for cleanup")
                except Exception:
                    errors.append("Cannot verify absence of a partially created run path")
        self.report["restoredFiles"] = [{"name": item["label"], "originallyPresent": item["present"], "verified": item.get("restored", False)} for item in self.backups]
        self.report["cleanupPassed"] = not errors and len(self.backups) == 4
        if errors:
            self.report["cleanupErrors"] = errors
            self.report["passed"] = False
        self.write_report()
        if not self.report["cleanupPassed"]:
            raise RuntimeError("Device cleanup/restoration did not pass; see ignored report")
        print("Original player settings and temporary command files restored; owned fixtures removed.", flush=True)

    def execute(self):
        try:
            self.prepare()
            self.run_main()
            self.run_source_history()
            self.run_quick()
            self.release_back()
            self.command("ui-input-reset")
            self.guard()
            self.report["passed"] = True
        except Exception as error:
            self.report["error"] = str(error)
            raise
        finally:
            self.restore()
            print("Report: " + str(self.output / "report.json"), flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--adb", default=ADB_DEFAULT)
    BackNavigationRun(parser.parse_args()).execute()


if __name__ == "__main__":
    main()
