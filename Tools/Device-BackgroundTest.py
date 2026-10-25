"""Check cinema backgrounds through development UI input on an explicitly selected PICO.

Requires Pillow and imageio-ffmpeg. Generates only synthetic media, retains raw
records in ignored folders, and restores settings/command files byte-for-byte.
Screenshots and simulated input do not establish optical or worn-headset comfort.
"""
import argparse
import datetime as dt
import json
import re
from pathlib import Path
import subprocess
import sys
import threading
import time
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parent / ".runtime"))
import imageio_ffmpeg
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser()
parser.add_argument("--serial", required=True)
parser.add_argument("--apk", type=Path)
parser.add_argument("--reuse-installed", action="store_true", help="Preserve and test an existing backgroundtest app; install its update separately")
parser.add_argument("--comfort", action="store_true", help="Also check highlight/subtitle controls and SRT/ASS rendering")
parser.add_argument("--package", default="com.linxyi.LsVRPlayer", choices=["com.linxyi.LsVRPlayer", "com.linxyi.LsVRPlayer.backgroundtest"])
parser.add_argument("--adb", default=r"C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe")
args = parser.parse_args()
PACKAGE = args.package
EXTERNAL = f"/sdcard/Android/data/{PACKAGE}/files/"
run_id = "background-" + dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex[:8]
records = ROOT / "Docs/Validation" / run_id
captures = ROOT / "Docs/Captures" / run_id
records.mkdir(parents=True, exist_ok=False)
captures.mkdir(parents=True, exist_ok=False)
stage = "/data/local/tmp/" + run_id + ".json"
# App-owned fixtures avoid changing shared-storage permissions on a reused app.
media_remote = EXTERNAL + run_id + ".mp4"
subtitle_remotes = [EXTERNAL + run_id + extension for extension in (".srt", ".ass")] if args.comfort else []
report = {"checks": [], "passed": False, "cleanup": [], "healthSamples": 0,
          "limitations": ["Synthetic short clip, simulated uGUI input; no optical glare measurement or physical-controller/headset comfort acceptance."]}
report["timingSamples"] = []
backups = []
owned_remote = []
monitor_stop = threading.Event()
monitor = None
health_error = []
device_validated = False
isolated_install = False


def adb(*command, check=True):
    result = subprocess.run([args.adb, "-s", args.serial, *command], capture_output=True, timeout=120)
    if check and result.returncode:
        code = re.search(rb"INSTALL_[A-Z_]+", result.stdout + result.stderr) if command[0] == "install" else None
        raise RuntimeError("ADB failed: " + command[0] + (" (" + code.group().decode() + ")" if code else ""))
    return result.stdout


def private(*command):
    return adb("shell", "run-as", PACKAGE, *command)


def read_file(path, internal=False):
    if internal:
        exists = private("sh", "-c", f"'[ -e {path} ] && echo yes || echo no'").strip()
    else:
        exists = adb("shell", "sh", "-c", f"'[ -e {path} ] && echo yes || echo no'").strip()
    if exists == b"no":
        return None
    if exists != b"yes":
        raise RuntimeError("Could not determine file presence")
    return private("cat", path) if internal else adb("exec-out", "cat", path)


def backup(path, internal=False):
    data = read_file(path, internal)
    destination = records / (str(len(backups)) + ".backup.bin")
    if data is not None:
        destination.write_bytes(data)
    backups.append((path, internal, data, destination))


def push_bytes(data, remote, internal=False):
    local = records / "transfer.bin"
    local.write_bytes(data)
    if internal:
        adb("push", str(local), stage)
        private("cp", stage, remote)
    else:
        adb("push", str(local), remote)


def status():
    # Diagnostics use a normal file write. Retry only a torn JSON read; every
    # successful sample still goes through the same focus/presence/freshness gate.
    for attempt in range(3):
        try:
            return json.loads(adb("exec-out", "cat", EXTERNAL + "playback-diagnostics.json"))
        except ValueError:
            if attempt == 2:
                raise
            time.sleep(.05)


def health(state):
    if not all(state.get(key) is True for key in ("hasFocus", "presenceKnown", "userPresent")):
        flags = {key: state.get(key) for key in ("hasFocus", "presenceKnown", "userPresent")}
        raise RuntimeError("PICO focus or headset presence lost; restart the run: " + json.dumps(flags))
    stamp = dt.datetime.fromisoformat(state["timestampUtc"].replace("Z", "+00:00"))
    if abs((dt.datetime.now(dt.timezone.utc) - stamp).total_seconds()) > 8:
        raise RuntimeError("Stale player diagnostics; restart the run")


def monitor_health():
    while not monitor_stop.wait(.7):
        try:
            state = status()
            health(state)
            report["healthSamples"] += 1
            report["timingSamples"].append({key: state.get(key) for key in ("timestampUtc", "unityFps", "frames", "cinemaHighlights", "cinemaSubtitleBrightness")})
        except Exception as error:
            health_error.append(type(error).__name__ + ": " + str(error))
            return


def wait(read, predicate, timeout=30, check_health=True):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if health_error and check_health:
            raise RuntimeError(health_error[0])
        try:
            value = read()
            if predicate(value):
                return value
        except (ValueError, KeyError):
            pass
        time.sleep(.25)
    raise RuntimeError("Timed out waiting for player state during " + report.get("lastAction", "startup"))


def command(action, **fields):
    report["lastAction"] = action + (" " + fields["name"] if action in ("tab", "ui-pointer") and "name" in fields else "")
    private("rm", "-f", "files/command-result.json")
    push_bytes(json.dumps(dict(action=action, **fields), ensure_ascii=False).encode(), "files/command.tmp", True)
    private("mv", "files/command.tmp", "files/command.json")
    return wait(lambda: json.loads(adb("shell", "run-as", PACKAGE, "cat", "files/command-result.json", check=False)), lambda r: r.get("action") == action and r.get("accepted"))


def click(name, fraction=.5):
    for pressed in (False, True, False):
        command("ui-pointer", preset=0, name=name, value=fraction, flag=pressed)


def passed(name):
    report["checks"].append(name)
    print("PASS " + name, flush=True)


def screenshot(name):
    (captures / (name + ".png")).write_bytes(adb("exec-out", "screencap", "-p"))


try:
    assert adb("get-state").strip() == b"device", "Selected device is not connected"
    model = adb("shell", "getprop", "ro.product.model").decode().strip()
    assert "A8110" in model or "PICO" in model.upper(), "Selected device does not identify as PICO"
    device_validated = True
    report["model"] = model
    installed_before = bool(adb("shell", "pm", "path", PACKAGE, check=False).strip())
    if args.reuse_installed:
        assert PACKAGE.endswith(".backgroundtest") and installed_before and not args.apk, "Reuse only an installed isolated package; no install or uninstall in this mode"
    if PACKAGE.endswith(".backgroundtest"):
        assert args.reuse_installed or not installed_before, "Isolated test package already exists; use --reuse-installed to preserve it"
        assert args.reuse_installed or args.apk, "New isolated test requires its APK"
    planned_remote = [stage, media_remote] + subtitle_remotes
    assert all(read_file(path) is None for path in planned_remote), "Run path collision"
    owned_remote = planned_remote
    (records / "ownership.json").write_text(json.dumps(owned_remote))
    # Stop before backing up any file the running app may write.
    adb("shell", "am", "force-stop", PACKAGE)
    backup(EXTERNAL + "settings.json")
    if args.apk:
        isolated_install = PACKAGE.endswith(".backgroundtest")
        installed = adb("install", "-r", str(args.apk.resolve()))
        assert b"Success" in installed, "APK install failed"
    for name in ("command.json", "command.tmp", "command-result.json"):
        backup("files/" + name, True)
    for name in ("command.json", "command.tmp", "command-result.json"):
        private("rm", "-f", "files/" + name)
    if isolated_install:
        adb("shell", "mkdir", "-p", EXTERNAL)
        # Synthetic consent fixture only in a new, run-owned test application.
        consent = {"version": "2026-10-17.1", "acceptedAtUtc": dt.datetime.now(dt.timezone.utc).isoformat()}
        push_bytes(json.dumps(consent).encode(), EXTERNAL + "privacy-consent.json")
    fixture = ROOT / "TestMedia" / (run_id + ".mp4")
    fixture.parent.mkdir(exist_ok=True)
    ffmpeg = subprocess.Popen([imageio_ffmpeg.get_ffmpeg_exe(), "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", "512x288", "-r", "24", "-i", "-", "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p", str(fixture)], stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    for frame in range(24 * 12):
        image = Image.new("RGB", (512, 288), (4, 4, 4))
        draw = ImageDraw.Draw(image)
        x = 70 + frame % 260
        draw.rectangle((x, 70, x + 50, 120), fill=(235, 235, 235))
        draw.text((160, 246), "BACKGROUND TEST", fill="white")
        ffmpeg.stdin.write(image.tobytes())
    ffmpeg.stdin.close()
    assert ffmpeg.wait(timeout=30) == 0, "Fixture generation failed"
    adb("push", str(fixture), media_remote)
    if args.comfort:
        srt = "1\n00:00:00,000 --> 00:00:12,000\nCOMFORT SUBTITLE / 字幕亮度\n"
        ass = """[Script Info]
ScriptType: v4.00+
PlayResX: 512
PlayResY: 288
[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Default,Arial,24,&H00FFFFFF,&H000000FF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,1,0,2,10,10,20,1
[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
Dialogue: 0,0:00:00.00,0:00:12.00,Default,,0,0,0,,ASS COMFORT TEST
"""
        for remote, text in zip(subtitle_remotes, (srt, ass)):
            push_bytes(text.encode("utf-8"), remote)
    fixture_settings = {"version": 3, "language": 1, "autoHide": False, "history": [], "favorites": [], "loop": True,
                        "formatDetection": 0, "volume": 0, "background": {"mode": 0, "grayLevel": 24, "featherDegrees": 12}}
    push_bytes(json.dumps(fixture_settings).encode(), EXTERNAL + "settings.json")
    launch_time = dt.datetime.now(dt.timezone.utc)
    adb("shell", "input", "keyevent", "KEYCODE_WAKEUP")
    adb("shell", "monkey", "-p", PACKAGE, "-c", "android.intent.category.LAUNCHER", "1")
    initial = wait(status, lambda s: "backgroundMode" in s and dt.datetime.fromisoformat(s["timestampUtc"].replace("Z", "+00:00")) > launch_time and all(s.get(key) is True for key in ("hasFocus", "presenceKnown", "userPresent")), timeout=45)
    health(initial)
    monitor = threading.Thread(target=monitor_health, daemon=True)
    monitor.start()
    command("open", uri="file://" + media_remote, name="Background test.mp4")
    wait(status, lambda s: s["snapshot"]["state"] == 3 and s["snapshot"]["playing"])
    command("menu", flag=True)
    command("tab", name="设置")
    click("Background settings")
    modes = ("Black", "NeutralGray", "SoftSurround", "QuietTheater", "MatteWall") if args.comfort else ("Black", "NeutralGray", "SoftSurround", "QuietTheater")
    for mode in modes:
        click("Background " + mode)
        current = wait(status, lambda s: s["backgroundMode"] == mode and s["backgroundVisible"] == (mode != "Black"))
        screenshot(mode + "-settings")
        command("menu", flag=False)
        wait(status, lambda s: s["uiSurface"] == "hidden")
        screenshot(mode)
        passed("Select and render " + mode)
        command("menu", flag=True)
        command("tab", name="设置")
        click("Background settings")
    click("Background QuietTheater")
    click("Background gray", 2/3)
    click("Background feather", .75)
    current = wait(status, lambda s: s["backgroundGray"] == 32 and s["backgroundFeather"] == 20)
    command("ui-back", preset=0, flag=False)
    command("ui-back", preset=0, flag=True)
    command("ui-back", preset=0, flag=False)
    wait(status, lambda s: s["fullModal"] == "" and s["fullVisible"])
    saved = json.loads(read_file(EXTERNAL + "settings.json"))
    assert saved["background"] == {"mode": 3, "grayLevel": 32.0, "featherDegrees": 20.0}
    passed("Adjust values, Back to Settings, persist settings")
    if args.comfort:
        click("Cinema comfort settings")
        for level in ("Off", "Gentle", "Medium", "Strong"):
            click("Cinema highlights " + level)
            wait(status, lambda s: s["cinemaHighlights"] == level and s["cinemaComfortActive"])
        for preset in range(4):
            click("Cinema comparison " + str(preset))
            expected_level = "Medium" if preset >= 2 else "Off"
            expected_gain = .7 if preset in (1, 3) else 1
            wait(status, lambda s: s["backgroundMode"] == "Black" and s["cinemaHighlights"] == expected_level and abs(s["cinemaSubtitleBrightness"] - expected_gain) < .001)
            saved = json.loads(read_file(EXTERNAL + "settings.json"))["cinemaComfort"]
            assert saved["highlights"] == (2 if preset >= 2 else 0) and abs(saved["subtitleBrightness"] - expected_gain) < .001
            command("menu", flag=False)
            wait(status, lambda s: s["uiSurface"] == "hidden")
            screenshot("Comfort-" + str(preset))
            command("menu", flag=True)
            command("tab", name="设置")
            click("Cinema comfort settings")
        screenshot("Comfort-settings")
        passed("Four highlight levels and independent comparison presets persist")
        click("Cinema subtitle brightness", 3/7)
        wait(status, lambda s: abs(s["cinemaSubtitleBrightness"] - .6) < .01)
        for pressed in (False, True, False):
            command("ui-back", preset=0, flag=pressed)
        wait(status, lambda s: s["fullModal"] == "")
        assert abs(json.loads(read_file(EXTERNAL + "settings.json"))["cinemaComfort"]["subtitleBrightness"] - .6) < .01
        passed("Subtitle brightness slider saves on Back")
        for remote, kind in zip(subtitle_remotes, ("SRT", "ASS")):
            command("subtitle", uri="file://" + remote)
            wait(status, lambda s: (s["assVisible"] if kind == "ASS" else "COMFORT SUBTITLE" in s["caption"]))
            for preset in (0, 3):
                command("menu", flag=True)
                command("tab", name="设置")
                click("Cinema comfort settings")
                click("Cinema comparison " + str(preset))
                command("menu", flag=False)
                wait(status, lambda s: s["uiSurface"] == "hidden" and s["cinemaHighlights"] == ("Off" if preset == 0 else "Medium"))
                screenshot(kind + "-" + str(preset))
            passed(kind + " subtitles render with original and combined controls")
        command("menu", flag=True)
        command("tab", name="设置")
        click("Background settings")
        click("Background MatteWall")
    for projection in range(1, 6):
        command("projection", projection=projection, stereo=0)
        wait(status, lambda s: not s["backgroundVisible"] and (not args.comfort or not s["cinemaComfortActive"]))
    command("projection", projection=0, stereo=0)
    wait(status, lambda s: s["backgroundVisible"] and s["backgroundMode"] == ("MatteWall" if args.comfort else "QuietTheater") and (not args.comfort or s["cinemaComfortActive"] and s["cinemaHighlights"] == "Medium" and abs(s["cinemaSubtitleBrightness"] - .7) < .001))
    passed("All panoramas suspend background; flat restores selection")
    command("stop")
    wait(status, lambda s: s["backgroundVisible"])
    passed("Background remains available after playback stops")
    if health_error:
        raise RuntimeError(health_error[0])
    report["passed"] = True
except Exception as error:
    report["error"] = type(error).__name__ + ": " + str(error)
    if device_validated:
        try:
            state = status()
            report["failureState"] = {key: state.get(key) for key in ("timestampUtc", "hasFocus", "presenceKnown", "userPresent", "uiSurface", "fullModal", "backgroundMode", "cinemaHighlights", "cinemaSubtitleBrightness")}
            report["failurePlayback"] = {key: state.get("snapshot", {}).get(key) for key in ("state", "playing", "error", "generation", "position", "duration")}
        except Exception:
            report["failureDiagnosticsUnavailable"] = True
    print(report["error"], flush=True)
finally:
    monitor_stop.set()
    if monitor:
        monitor.join(timeout=5)
    cleanup_errors = []
    try:
        if device_validated:
            adb("shell", "am", "force-stop", PACKAGE)
    except Exception:
        cleanup_errors.append("Could not force-stop app before restoring files")
    for remote, internal, data, destination in backups:
        try:
            if data is None:
                (private if internal else lambda *a: adb("shell", *a))("rm", "-f", remote)
            else:
                push_bytes(data, remote, internal)
            assert read_file(remote, internal) == data, "Restoration verification failed"
            report["cleanup"].append({"file": Path(remote).name, "restored": True, "originalAbsent": data is None})
        except Exception:
            cleanup_errors.append("File restoration failed: " + Path(remote).name)
    for remote in owned_remote:
        try:
            assert remote in json.loads((records / "ownership.json").read_text()) and run_id in remote
            adb("shell", "rm", "-f", remote)
            assert read_file(remote) is None
        except Exception:
            cleanup_errors.append("Owned fixture cleanup failed")
    if isolated_install:
        try:
            assert PACKAGE == "com.linxyi.LsVRPlayer.backgroundtest" and not installed_before
            if adb("shell", "pm", "path", PACKAGE, check=False).strip():
                assert b"Success" in adb("uninstall", PACKAGE)
            assert not adb("shell", "pm", "path", PACKAGE, check=False).strip()
            report["isolatedAppRemoved"] = True
        except Exception:
            cleanup_errors.append("Isolated test app removal failed")
    if cleanup_errors:
        report["passed"] = False
        report["cleanupErrors"] = cleanup_errors
    report["systemSettingsChanged"] = False
    (records / "result.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print("Report: " + str(records / "result.json"), flush=True)
    print("PASS" if report["passed"] else "FAIL", flush=True)
raise SystemExit(0 if report["passed"] else 1)
