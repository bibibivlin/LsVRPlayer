"""Exercise quick controls through the development uGUI pointer driver on PICO.

Uses generated Recognition fixtures only. This does not establish physical trigger
feel or binocular comfort. Restores original player settings byte-for-byte.
Temporarily enables USB stay-awake and restores its exact original value; never
changes screen-off timeout.
"""
import argparse
import json
from pathlib import Path
import subprocess
import threading
import time

ROOT = Path(__file__).resolve().parent.parent
PACKAGE = "com.linxyi.LsVRPlayer"
EXTERNAL = "/sdcard/Android/data/" + PACKAGE + "/files/"
MEDIA = "file:///sdcard/Movies/VRPlayerTest/Recognition/"
parser = argparse.ArgumentParser()
parser.add_argument("--serial", required=True)
parser.add_argument("--adb", default=r"C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe")
parser.add_argument("--section", choices=("all", "panels", "distance"), default="all")
args = parser.parse_args()
validation = ROOT / "Docs/Validation"
captures = ROOT / "Docs/Captures"
validation.mkdir(parents=True, exist_ok=True)
captures.mkdir(parents=True, exist_ok=True)
backup = validation / "quick-controls-settings.backup.json"
usb_backup = validation / "quick-controls-usb-awake.backup.json"
output = validation / ("quick-controls-device.json" if args.section == "all" else "quick-controls-device-" + args.section + ".json")
report = {"checks": [], "passed": False, "section": args.section, "input": "development pointer and stick drivers through VrUiInput",
          "limitations": ["Physical controller feel and binocular comfort require wearing the headset."]}
awake = threading.Event()
backup_created = False
backup_missing = False
usb_awake_original = None


def adb(*command, check=True):
    return subprocess.run([args.adb, "-s", args.serial, *command], capture_output=True, check=check).stdout


def wait(read, predicate, timeout=30):
    until = time.monotonic() + timeout
    last = None
    while time.monotonic() < until:
        try:
            value = read()
            last = value
            if predicate(value):
                return value
        except (json.JSONDecodeError, subprocess.CalledProcessError):
            pass
        time.sleep(.25)
    detail = ""
    if isinstance(last, dict) and isinstance(last.get("snapshot"), dict):
        safe = {key: last.get(key) for key in ("uiSurface", "quickPopup", "projection", "stereo", "distance", "panoramaZoom")}
        safe["snapshot"] = {key: last["snapshot"].get(key) for key in ("state", "generation", "playing")}
        detail = ": " + json.dumps(safe, ensure_ascii=False)
    raise AssertionError("Timed out waiting for fixture state" + detail)


def command(action, **fields):
    local = ROOT / "TestMedia/quick-command.json"
    local.parent.mkdir(parents=True, exist_ok=True)
    local.write_text(json.dumps({"action": action, **fields}, ensure_ascii=False), encoding="utf-8")
    adb("shell", "run-as", PACKAGE, "rm", "-f", "files/command-result.json")
    adb("push", str(local), "/data/local/tmp/vrplayer-quick.json")
    adb("shell", "run-as", PACKAGE, "cp", "/data/local/tmp/vrplayer-quick.json", "files/command.tmp")
    adb("shell", "run-as", PACKAGE, "mv", "files/command.tmp", "files/command.json")
    return wait(lambda: json.loads(adb("shell", "run-as", PACKAGE, "cat", "files/command-result.json")),
                lambda item: item.get("accepted") and item.get("action") == action)


def status():
    return json.loads(adb("shell", "cat", EXTERNAL + "playback-diagnostics.json"))


def pointer(name="", pressed=False, value=.5, index=0):
    command("ui-pointer", name=name, flag=pressed, value=value, preset=index)


def click(name, value=.5):
    pointer(name, False, value)
    pointer(name, True, value)
    pointer(name, False, value)


def stick(vertical=0, horizontal=0, index=0):
    command("ui-stick", preset=index, value=horizontal, offset=vertical)
    if vertical == 0 and horizontal == 0:
        # Surface changes suppress held sticks until at least one neutral frame.
        time.sleep(.3)


def release_stick(field, index=0):
    stick(index=index)
    # Diagnostics update every two seconds. Sample after release, then again to
    # ensure the neutral stick stopped motion instead of comparing stale values.
    time.sleep(2.3)
    released = status()[field]
    time.sleep(2.3)
    assert abs(status()[field] - released) < .001, "Released stick kept adjusting " + field
    return released


def passed(name):
    report["checks"].append({"name": name, "passed": True})
    print("PASS " + name, flush=True)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")


def capture(name):
    target = captures / ("quick-" + name + ".png")
    for attempt in range(6):
        adb("shell", "input", "keyevent", "KEYCODE_WAKEUP")
        time.sleep(.5)
        adb("shell", "screencap", "-p", "/sdcard/vrplayer-quick.png")
        adb("pull", "/sdcard/vrplayer-quick.png", str(target))
        # These colorful generated fixtures exceed 200 KB; a black PICO focus/loading
        # screen is not evidence of the rendered controls. Still review images by eye.
        if target.stat().st_size > 200_000:
            return
    raise AssertionError("PICO did not produce a reviewable fixture capture: " + name)


def wake_loop():
    while not awake.wait(1):
        adb("shell", "input", "keyevent", "KEYCODE_WAKEUP", check=False)


def run_panels():
    pointer()
    pointer(pressed=True)
    wait(status, lambda item: item["uiSurface"] == "quick" and not item["snapshot"]["playing"])
    pointer()
    passed("Empty trigger press opens quick controls without clicking playback")
    capture("bar")

    pointer("Volume")
    wait(status, lambda item: item["quickVolumeVisible"])
    pointer("Volume slider", value=.65)
    wait(status, lambda item: item["quickVolumeVisible"])
    click("Volume slider", .65)
    wait(status, lambda item: abs(item["volume"] - .65) < .04)
    capture("volume")
    click("Volume")
    wait(status, lambda item: item["volume"] == 0)
    click("Volume")
    wait(status, lambda item: abs(item["volume"] - .65) < .04)
    pointer("Play pause")
    wait(status, lambda item: not item["quickVolumeVisible"])
    passed("Hover-to-slider stays visible, adjusts volume and restores mute level")

    duration = status()["snapshot"]["duration"]
    click("Seek surface", .4)
    wait(status, lambda item: not item["snapshot"]["playing"] and abs(item["snapshot"]["position"] - duration * .4) < 600)
    passed("Upper bar click seeks while preserving pause")

    click("Mode")
    wait(status, lambda item: item["quickPopup"] == "mode")
    click("VR360")
    wait(status, lambda item: item["projection"] == "Equirect360")
    click("Distance", .75)
    wait(status, lambda item: abs(item["panoramaZoom"] - 1.625) < .04)
    capture("mode")
    for zoom in (.5, 1, 2):
        command("panorama-zoom", value=zoom)
        wait(status, lambda item: abs(item["panoramaZoom"] - zoom) < .001)
        capture("zoom-" + str(zoom))
    passed("Mode popup changes projection and panorama zoom on Android GLES")
    click("3D 左右")
    wait(status, lambda item: item["stereo"] == "SideBySide")
    click("重置远近")
    wait(status, lambda item: item["projection"] == "Equirect360" and item["stereo"] == "SideBySide" and abs(item["panoramaZoom"] - 1) < .001)
    passed("Panorama distance reset restores 1x while preserving projection and stereo")
    click("2D")
    wait(status, lambda item: item["stereo"] == "Mono")

    click("Advanced")
    wait(status, lambda item: item["quickPopup"] == "advanced")
    click("1.25×")
    wait(status, lambda item: abs(item["speed"] - 1.25) < .001)
    capture("advanced")
    passed("Speed popup replaces mode popup and applies 1.25x preset")

    click("Subtitles")
    wait(status, lambda item: item["quickPopup"] == "subtitles")
    automatic = wait(status, lambda item: item["subtitleSelection"] == "Auto" and item["subtitle"].endswith(".chs.srt"))["subtitle"]
    command("subtitle-auto", flag=False)
    click("字幕：开")
    wait(status, lambda item: item["subtitleSelection"] == "Off")
    click("字幕：开")
    wait(status, lambda item: item["subtitleSelection"] == "Auto" and item["subtitle"] == automatic)
    passed("Reenabling restores the automatic subtitle with global autoload disabled")
    click("选择字幕文件")
    wait(status, lambda item: item["quickPopup"] == "files" and not item["quickBrowserLoading"])
    capture("files")
    source = status()["source"]
    click(MEDIA + "Unmarked-A.en.srt")
    wait(status, lambda item: item["quickPopup"] == "subtitles" and item["subtitle"].endswith(".en.srt"))
    click("字幕：开")
    wait(status, lambda item: item["subtitleSelection"] == "Off" and not item["subtitle"])
    click("字幕：开")
    wait(status, lambda item: item["subtitleSelection"] == "Manual" and item["subtitle"].endswith(".en.srt"))
    click("+")
    capture("subtitles")
    assert status()["source"] == source
    passed("Independent subtitle browser selects SRT and restores it after off/on")

    old_generation = status()["snapshot"]["generation"]
    click("Next")
    advanced = wait(status, lambda item: item["uiSurface"] == "quick" and item["quickPopup"] == "" and item["snapshot"]["generation"] > old_generation and item["snapshot"]["state"] == 3)
    report["playingAfterNext"] = advanced["snapshot"]["playing"]
    passed("Next video preserves quick controls and closes obsolete popup")
    command("pause")
    wait(status, lambda item: not item["snapshot"]["playing"])
    pointer()
    time.sleep(6)
    assert status()["uiSurface"] == "quick"
    command("auto-hide", flag=True)
    command("play")
    wait(status, lambda item: item["snapshot"]["playing"])
    wait(status, lambda item: item["uiSurface"] == "hidden", timeout=18)
    passed("Pause keeps controls visible; playing with no pointer hides after idle timeout")
    pointer(pressed=True)
    wait(status, lambda item: item["uiSurface"] == "quick")
    pointer()
    command("menu", flag=True)
    wait(status, lambda item: item["uiSurface"] == "full" and not item["quickVolumeVisible"])
    passed("Full menu replaces quick controls")


def run_distance():
    # Use the new development stick driver, which runs the same stick-control
    # path as the physical controllers. Setter commands below only establish
    # format or visibility; distance motion must come from ui-stick commands.
    command("pause")
    wait(status, lambda item: not item["snapshot"]["playing"])
    command("auto-hide", flag=False)
    if args.section == "distance":
        command("projection", projection=2, stereo=1)
        command("panorama-zoom", value=1)
        wait(status, lambda item: item["projection"] == "Equirect360" and item["stereo"] == "SideBySide" and abs(item["panoramaZoom"] - 1) < .001)
    command("quick-menu", flag=True)
    click("Mode")
    wait(status, lambda item: item["quickPopup"] == "mode")
    click("VR360")
    if args.section == "distance":
        click("Distance", .75)
        wait(status, lambda item: abs(item["panoramaZoom"] - 1.625) < .04)
    click("重置远近")
    wait(status, lambda item: item["projection"] == "Equirect360" and abs(item["panoramaZoom"] - 1) < .001)
    if args.section == "distance":
        assert status()["stereo"] == "SideBySide"
        passed("Panorama distance reset restores 1x while preserving projection and stereo")
        click("2D")
        wait(status, lambda item: item["stereo"] == "Mono")
    command("quick-menu", flag=False)
    command("menu", flag=False)
    command("play")
    wait(status, lambda item: item["uiSurface"] == "hidden" and item["snapshot"]["playing"])
    pointer()
    stick()
    stick(index=1)
    stick(1)
    wait(status, lambda item: item["panoramaZoom"] > 1.2)
    raised_zoom = release_stick("panoramaZoom")
    stick(-1)
    wait(status, lambda item: item["panoramaZoom"] < raised_zoom - .15)
    release_stick("panoramaZoom")
    passed("Hidden panorama stick up/down adjusts zoom and neutral stops motion")
    stick(1)
    wait(status, lambda item: abs(item["panoramaZoom"] - 2) < .001)
    stick()
    stick(-1)
    wait(status, lambda item: abs(item["panoramaZoom"] - .5) < .001)
    stick()
    passed("Hidden panorama stick remains within 0.5x to 2x bounds")

    command("projection", projection=0, stereo=1)
    wait(status, lambda item: item["projection"] == "Flat" and item["stereo"] == "SideBySide")
    stick(index=1)
    flat_start = status()["distance"]
    stick(1, index=1)
    wait(status, lambda item: item["distance"] < flat_start - .3)
    closer_distance = release_stick("distance", index=1)
    stick(-1, index=1)
    wait(status, lambda item: item["distance"] > closer_distance + .3)
    release_stick("distance", index=1)
    passed("Hidden flat stick up/down adjusts distance and neutral stops motion")
    stick(1, index=1)
    wait(status, lambda item: abs(item["distance"] - 1.2) < .001)
    stick(index=1)
    stick(-1, index=1)
    wait(status, lambda item: abs(item["distance"] - 10) < .001)
    stick(index=1)
    passed("Hidden flat stick remains within 1.2m to 10m bounds")

    command("quick-menu", flag=True)
    click("Mode")
    wait(status, lambda item: item["quickPopup"] == "mode")
    click("重置远近")
    wait(status, lambda item: item["projection"] == "Flat" and item["stereo"] == "SideBySide" and abs(item["distance"] - 3.5) < .001)
    passed("Flat distance reset restores 3.5m while preserving projection and stereo")
    stick(index=1)
    stick(1, index=1)
    time.sleep(2.5)
    assert abs(status()["distance"] - 3.5) < .001
    stick(index=1)
    command("menu", flag=True)
    wait(status, lambda item: item["uiSurface"] == "full")
    stick(index=1)
    stick(-1, index=1)
    time.sleep(2.5)
    assert abs(status()["distance"] - 3.5) < .001
    stick(index=1)
    passed("Visible quick and full panels block vertical stick distance adjustment")

try:
    adb("shell", "ls", "/sdcard/Movies/VRPlayerTest/Recognition/Unmarked-A.mp4")
    adb("shell", "ls", "/sdcard/Movies/VRPlayerTest/Recognition/Unmarked-A.en.srt")
    adb("shell", "am", "force-stop", PACKAGE)
    original = subprocess.run([args.adb, "-s", args.serial, "pull", EXTERNAL + "settings.json", str(backup)], capture_output=True)
    backup_created = original.returncode == 0
    if not backup_created:
        # Fail before modifying settings unless absence has been positively verified.
        presence = adb("shell", "if [ -e " + EXTERNAL + "settings.json ]; then echo present; else echo missing; fi").strip()
        if presence != b"missing":
            raise RuntimeError("Cannot back up existing player settings")
        backup_missing = True
    usb_original = adb("shell", "settings", "get", "global", "stay_on_while_plugged_in").decode("utf-8").strip()
    try:
        usb_current = 0 if usb_original == "null" else int(usb_original)
    except ValueError:
        raise RuntimeError("Cannot back up USB stay-awake setting") from None
    usb_backup.write_text(json.dumps({"value": usb_original}), encoding="utf-8")
    # Mark restoration necessary before the write, including a failed adb write.
    usb_awake_original = usb_original
    usb_temporary = str(usb_current | 2)
    try:
        adb("shell", "settings", "put", "global", "stay_on_while_plugged_in", usb_temporary)
        if adb("shell", "settings", "get", "global", "stay_on_while_plugged_in").decode("utf-8").strip() != usb_temporary:
            raise RuntimeError("USB stay-awake verification failed")
    except Exception:
        # Do not print the backed-up or temporary system-setting value.
        raise RuntimeError("Cannot enable temporary USB stay-awake") from None
    fixture = validation / "quick-controls-fixture-settings.json"
    fixture.write_text(json.dumps({"version": 3, "history": [], "favorites": [], "volume": .8, "speed": 1,
                                  "autoHide": False, "autoLoadSubtitles": True, "formatDetection": 1,
                                  "loop": True, "subtitleDistance": 3, "subtitleSize": 42,
                                  "subtitleOffset": -.75, "subtitleFollow": True}), encoding="utf-8")
    adb("push", str(fixture), EXTERNAL + "settings.json")
    threading.Thread(target=wake_loop, daemon=True).start()
    adb("shell", "input", "keyevent", "KEYCODE_WAKEUP")
    adb("shell", "monkey", "-p", PACKAGE, "-c", "android.intent.category.LAUNCHER", "1")
    wait(status, lambda item: item["snapshot"]["generation"] == 0)
    command("open", uri=MEDIA + "Unmarked-A.mp4", name="Unmarked-A.mp4")
    wait(status, lambda item: item["snapshot"]["state"] == 3 and item["snapshot"]["generation"] > 0)
    command("pause")
    command("recenter", flag=True)
    command("menu", flag=False)
    if args.section != "distance":
        run_panels()
    if args.section != "panels":
        run_distance()
    command("ui-input-reset")
    report["passed"] = True
except Exception as error:
    report["error"] = str(error)
    raise
finally:
    awake.set()
    adb("shell", "am", "force-stop", PACKAGE, check=False)
    report["settingsRestored"] = False
    report["usbStayAwakeRestored"] = usb_awake_original is None
    restoration_errors = []
    try:
        if backup_created:
            adb("push", str(backup), EXTERNAL + "settings.json")
            assert adb("exec-out", "cat", EXTERNAL + "settings.json") == backup.read_bytes(), "Original settings restoration failed"
            report["settingsRestored"] = True
        elif backup_missing:
            adb("shell", "rm", "-f", EXTERNAL + "settings.json")
            presence = adb("shell", "if [ -e " + EXTERNAL + "settings.json ]; then echo present; else echo missing; fi").strip()
            report["settingsRestored"] = presence == b"missing"
            assert report["settingsRestored"], "Original absence of settings was not restored"
    except Exception:
        restoration_errors.append("Player settings restoration failed")
    # Restore independently: player restoration failures must not leave USB awake.
    try:
        if usb_awake_original is not None:
            if usb_awake_original == "null":
                adb("shell", "settings", "delete", "global", "stay_on_while_plugged_in")
            else:
                adb("shell", "settings", "put", "global", "stay_on_while_plugged_in", usb_awake_original)
            restored_usb = adb("shell", "settings", "get", "global", "stay_on_while_plugged_in").decode("utf-8").strip()
            report["usbStayAwakeRestored"] = restored_usb == usb_awake_original
            if not report["usbStayAwakeRestored"]:
                raise RuntimeError("USB stay-awake restoration failed")
    except Exception:
        restoration_errors.append("USB stay-awake restoration failed")
    if report["settingsRestored"]:
        print("Original player settings restored.", flush=True)
    if report["usbStayAwakeRestored"]:
        print("Original USB stay-awake setting restored.", flush=True)
    if not report["settingsRestored"] or not report["usbStayAwakeRestored"]:
        report["passed"] = False
    if restoration_errors:
        report["restorationErrors"] = restoration_errors
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    if restoration_errors:
        raise RuntimeError("; ".join(restoration_errors))
