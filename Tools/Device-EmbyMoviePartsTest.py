"""Validate Emby movie-only browsing, official parts, and official extras on PICO.

Reuses Device-EmbyTest's private commands, store backups and verified server
user-data restoration. Credentials, names, paths and tokens are excluded from
the report. Use --require-parts when the designated real server has a known
AdditionalParts sample. Keep the headset worn and the app focused throughout.
"""
import argparse
import copy
from datetime import datetime
import getpass
import importlib.util
import json
import os
from pathlib import Path
import re
import time
import urllib.parse
import uuid

_spec = importlib.util.spec_from_file_location("emby_device_base", Path(__file__).with_name("Device-EmbyTest.py"))
base = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(base)
ROOT, PACKAGE, EXTERNAL = base.ROOT, base.PACKAGE, base.EXTERNAL
CheckFailure, require = base.CheckFailure, base.require


class DetailedServer(base.Server):
    def item(self, item_id):
        fields = "Overview,People,MediaSources,ParentId,SortName,Tags,Studios,ProductionLocations"
        value = self.request(self.user_path("/Items/"+urllib.parse.quote(item_id, safe=""))+"?Fields="+urllib.parse.quote(fields, safe=""))
        if not value.get("Tags"):
            value["Tags"] = [entry["Name"] for entry in value.get("TagItems", []) if entry.get("Name")]
        return value


class PresenceFailure(Exception):
    """Never caught by Device.wait: an interrupted run cannot resume itself."""


class GuardedDevice(base.Device):
    def __init__(self, args, run_id):
        super().__init__(args, run_id)
        self.guarded = False
        self.focus_samples = 0
        self.diagnostic_modified = -1
        self.continuous_runs = []
        self.resume_measurements = []
        self.inconsistent_diagnostic_reads = 0
        self.diagnostic_read_seconds = 0
        self.prepare_failures = []

    def _guard(self, value):
        if not value.get("hasFocus") or not value.get("presenceKnown") or not value.get("userPresent"):
            raise PresenceFailure("Application focus or verified headset presence interrupted the run")
        age = time.time() - self.diagnostic_modified
        if age > 7:
            raise PresenceFailure("Focus and presence diagnostics became stale")
        self.focus_samples += 1

    def status(self):
        path = EXTERNAL+"playback-diagnostics.json"
        # exec-out escapes each argument for the device shell. Pass this fixed
        # program directly as sh's -c argument; no host shell or credentials.
        program = "stat -c %Y "+path+" && cat "+path+" && printf '\\n' && stat -c %Y "+path
        for _ in range(3):
            try:
                started = time.monotonic()
                data = self.adb("exec-out", "sh", "-c", program).stdout.splitlines()
                self.diagnostic_read_seconds = time.monotonic()-started
                if len(data) < 3:
                    raise ValueError("Incomplete diagnostic envelope")
                before, after = int(data[0]), int(data[-1])
                if before != after:
                    self.inconsistent_diagnostic_reads += 1
                    continue
                value = json.loads(b"\n".join(data[1:-1]))
                if not isinstance(value, dict):
                    raise ValueError("Invalid diagnostic envelope")
                self.diagnostic_modified = before
                if self.guarded:
                    self._guard(value)
                return value
            except (CheckFailure, json.JSONDecodeError, ValueError):
                self.inconsistent_diagnostic_reads += 1
        if self.guarded:
            raise PresenceFailure("Fresh consistent device diagnostics are unavailable") from None
        raise CheckFailure("Fresh consistent device diagnostics are unavailable") from None

    def command(self, action, **fields):
        if self.guarded:
            self.status()
        value = super().command(action, **fields)
        if self.guarded:
            self.status()
        return value

    def capture(self, name):
        if self.guarded:
            self.status()
        super().capture("movie-parts-"+name)
        if self.guarded:
            self.status()

    def begin_guard(self, launch_time):
        deadline = time.monotonic() + 45
        while time.monotonic() < deadline:
            try:
                value = self.status()
                if self.diagnostic_modified >= int(launch_time) and value.get("snapshot", {}).get("generation") == 0:
                    self._guard(value)
                    self.guarded = True
                    return
            except (CheckFailure, json.JSONDecodeError, ValueError):
                pass
            time.sleep(.4)
        raise PresenceFailure("Fresh headset-presence and application-focus diagnostics did not become available")


def rows(value):
    return value.get("Items", []) if isinstance(value, dict) else value if isinstance(value, list) else []


def unique_playable(items, exclude=()):
    seen = set(exclude)
    result = []
    for item in items:
        identity = item.get("Id") if isinstance(item, dict) else None
        if not identity or identity in seen or item.get("Type") not in ("Movie", "Episode", "Video", "Trailer"):
            continue
        seen.add(identity)
        result.append(item)
    return result


def parts_for(server, item_id):
    path = "/Videos/" + urllib.parse.quote(item_id, safe="") + "/AdditionalParts?UserId=" + urllib.parse.quote(server.user, safe="")+"&Fields=ParentId,SortName,MediaSources"
    return unique_playable(rows(server.request(path)), (item_id,))


def extras_for(server, item_id):
    suffix = "/Items/" + urllib.parse.quote(item_id, safe="")
    # Official endpoints alone determine membership; filenames never classify extras.
    special = rows(server.request(server.user_path(suffix + "/SpecialFeatures")))
    trailers = rows(server.request(server.user_path(suffix + "/LocalTrailers")))
    return unique_playable(special + trailers, (item_id,))


def movie_query(server, **filters):
    fields = [field.strip() for field in (filters.get("Fields") or "").split(",") if field.strip()]
    if not any(field.lower() == "sortname" for field in fields):
        fields.append("SortName")
    filters = {**filters, "Fields": ",".join(fields)}
    found = []
    start = 0
    while start < 10000:
        query = {"Recursive": "true", "IncludeItemTypes": "Movie", "StartIndex": start, "Limit": 200,
                 "SortBy": "SortName", "SortOrder": "Ascending", "EnableTotalRecordCount": "true", **filters}
        result = server.request(server.user_path("/Items?")+urllib.parse.urlencode(query))
        batch = rows(result)
        found.extend(batch)
        require(len({item["Id"] for item in found}) == len(found), "Server movie query returned duplicate IDs")
        if len(found) >= result.get("TotalRecordCount", len(found)):
            found.sort(key=lambda item: ((item.get("SortName") or item.get("Name", "")).upper(), item["Id"]))
            return {"Items": found, "TotalRecordCount": len(found)}
        require(batch, "Server movie query ended before its reported total")
        start += len(batch)
    raise CheckFailure("Movie query exceeded the bounded validation catalogue")


def assert_queue(device, kind, ids, index, main_id):
    state = device.inspect()["emby"]
    require(state.get("playbackQueueKind") == kind and state.get("playbackQueueIds") == ids and
            state.get("playbackQueueIndex") == index and state.get("playbackMainId") == main_id,
            "Playback queue does not match the committed same-film group")
    require(state.get("canPrevious") == (index > 0) and state.get("canNext") == (0 <= index < len(ids)-1),
            "Playback navigation boundaries do not match the committed group")
    return state


def sample_video_streams(item):
    sources = item.get("MediaSources") or []
    streams = (sources[0].get("MediaStreams") or []) if sources else (item.get("MediaStreams") or [])
    return [stream for stream in streams if stream.get("Type", "").lower() == "video"]


def unsupported_h264_sample(item):
    for stream in sample_video_streams(item):
        codec = re.sub(r"[^a-z0-9]", "", str(stream.get("Codec") or "").lower())
        profile = re.sub(r"[^a-z0-9]", "", str(stream.get("Profile") or "").lower())
        try:
            bits = int(stream.get("BitDepth") or 0)
        except (TypeError, ValueError):
            bits = 0
        if codec in ("h264", "avc", "avc1") and (bits > 8 or profile.startswith(("high10", "hi10"))):
            return True
    return False


def sample_video_rank(item):
    if unsupported_h264_sample(item):
        return 99
    streams = sample_video_streams(item)
    codecs = {re.sub(r"[^a-z0-9]", "", str(stream.get("Codec") or "").lower()) for stream in streams}
    if codecs & {"hevc", "h265"}:
        return 0
    if codecs & {"h264", "avc", "avc1"}:
        return 1
    return 2


def discover(server):
    all_movies = rows(movie_query(server, Fields="MediaSources,ParentId"))
    require(all_movies, "Designated server has no accessible movies")
    split, extras = None, None
    details = {}

    def detail(item):
        if item["Id"] not in details:
            details[item["Id"]] = server.item(item["Id"])
        return details[item["Id"]]

    # Preserve the designated server's multipart sample selection and order.
    for movie in all_movies:
        additional = parts_for(server, movie["Id"])
        if additional:
            resolved = [detail(movie)]+[detail(item) for item in additional]
            if not any(unsupported_h264_sample(item) for item in resolved):
                split = {"main": resolved[0], "parts": resolved}
                break

    # Hardware regression samples exclude an explicitly unsupported H.264
    # high-bit-depth source; the complete wall and official lists stay intact.
    for movie in sorted(all_movies, key=sample_video_rank):
        if unsupported_h264_sample(movie) or movie.get("RunTimeTicks", 0) < 12*10_000_000:
            continue
        main = detail(movie)
        if unsupported_h264_sample(main):
            continue
        attached = extras_for(server, movie["Id"])
        suitable = {}
        for item in attached:
            if item.get("RunTimeTicks", 0) < 12*10_000_000 or unsupported_h264_sample(item):
                continue
            video = detail(item)
            if video.get("RunTimeTicks", 0) >= 12*10_000_000 and not unsupported_h264_sample(video):
                suitable[video["Id"]] = video
        if not suitable:
            continue
        preferred = sorted(suitable.values(), key=lambda item: (sample_video_rank(item),
                           not any(term in item.get("Name", "").upper() for term in ("PV", "CM")), item.get("RunTimeTicks", 0)))
        representatives = []
        for label in ("PV", "CM"):
            candidate = next((item for item in preferred if label in item.get("Name", "").upper()), None)
            if candidate and candidate["Id"] not in {item["Id"] for item in representatives}:
                representatives.append(candidate)
        chosen = (representatives+[item for item in preferred if item["Id"] not in {entry["Id"] for entry in representatives}])[:2]
        pair = next(([suitable[attached[index]["Id"]], suitable[attached[index+1]["Id"]]]
                     for index in range(len(attached)-1)
                     if attached[index]["Id"] in suitable and attached[index+1]["Id"] in suitable), [])
        candidate = {"main": main, "extraItems": attached, "chosen": chosen, "navigationPair": pair}
        if extras is None:
            extras = candidate
        if len(chosen) == 2 and pair:
            extras = candidate
            break
    require(extras is not None, "Designated server needs an official playable extra of at least12 seconds")
    return all_movies, split, extras


def wait_snapshot(device, predicate, failure, after_stamp=None, timeout=75):
    # Command acknowledgement is immediate, but the playback file is emitted
    # every two seconds. A post-command mtime barrier prevents older state or
    # format fields from satisfying the next assertion.
    return device.wait(device.status, lambda value: (after_stamp is None or device.diagnostic_modified > after_stamp) and
                       predicate(value), failure, timeout=timeout)


def change_format(device, projection, stereo, expected_projection, expected_stereo):
    generation = device.status()["snapshot"]["generation"]
    device.command("projection", projection=projection, stereo=stereo)
    return wait_snapshot(device, lambda value: value["snapshot"]["generation"] == generation and
                         value["projection"] == expected_projection and value["stereo"] == expected_stereo,
                         "Requested manual format did not reach a fresh playback diagnostic",
                         after_stamp=device.diagnostic_modified, timeout=15)


def prepared_action(device, action, item_id, **fields):
    generation = device.status()["snapshot"]["generation"]
    device.command(action, uri=item_id, **fields)
    value = wait_snapshot(device, lambda state: state["snapshot"]["generation"] > generation and
                          (state["snapshot"]["state"] == 3 or state["snapshot"].get("error")),
                          "Requested official video did not prepare", after_stamp=device.diagnostic_modified, timeout=120)
    if value["snapshot"].get("error"):
        snapshot = value["snapshot"]
        code = re.search(r"\bERROR_CODE_[A-Z0-9_]+\b", str(snapshot.get("error", "")))
        mime = str(snapshot.get("codec") or "").lower()
        device.prepare_failures.append({"action": action, "generation": snapshot["generation"], "state": snapshot["state"],
                                        "errorCode": code.group() if code else "unknown",
                                        "videoMime": mime if mime in ("video/avc", "video/hevc", "video/mp4v-es", "video/x-vnd.on2.vp9", "video/av01") else "unknown"})
        raise CheckFailure("Requested official video did not prepare")
    inspect = device.inspect()["emby"]
    require(inspect.get("currentId") == item_id, "Prepared video does not match the selected official item")
    return value


def pause_and_seek(device, position):
    generation = device.status()["snapshot"]["generation"]
    device.command("pause")
    wait_snapshot(device, lambda value: value["snapshot"]["generation"] == generation and
                  not value["snapshot"]["playing"], "Playback did not pause", after_stamp=device.diagnostic_modified)
    device.command("seek", position=position)
    return wait_snapshot(device, lambda value: value["snapshot"]["generation"] == generation and
                         value["snapshot"]["state"] == 3 and not value["snapshot"]["playing"] and
                         abs(value["snapshot"]["position"]-position) < 1500,
                         "Authenticated paused seek did not settle", after_stamp=device.diagnostic_modified)


def continuous_sample(device, seconds, label):
    measurement = {"label": label, "requestedSeconds": seconds, "passed": False, "samples": 0}
    device.continuous_runs.append(measurement)
    try:
        device.command("play")
        first = wait_snapshot(device, lambda value: value["snapshot"]["state"] == 3 and
                              value["snapshot"]["playing"] and not value["snapshot"].get("error"),
                              "Playback did not start in a fresh diagnostic", after_stamp=device.diagnostic_modified)
        generation = first["snapshot"]["generation"]
        start_snapshot_ms = first["snapshot"].get("monotonicMs", 0)
        require(start_snapshot_ms > 0, "Continuous playback needs a native monotonic snapshot clock")
        start_position = last_position = first["snapshot"]["position"]
        start_frames = first.get("frames", 0)
        measurement.update(generation=generation, startPositionMs=start_position, startDecodedFrames=start_frames,
                           durationMs=first["snapshot"]["duration"], startDiagnosticModified=device.diagnostic_modified,
                           startSnapshotMonotonicMs=start_snapshot_ms, positionAdvancedMs=0, decodedFramesAdvanced=0)
        wall_start = last_new_snapshot_wall = time.monotonic()
        last_snapshot_ms = last_progress_ms = start_snapshot_ms
        while True:
            value = device.status()
            snapshot = value["snapshot"]
            snapshot_ms = snapshot.get("monotonicMs", 0)
            elapsed = (snapshot_ms-start_snapshot_ms)/1000
            wall_elapsed = time.monotonic()-wall_start
            position = snapshot["position"]
            measurement.update(observedSeconds=round(elapsed, 3), positionAdvancedMs=position-start_position,
                               decodedFramesAdvanced=value.get("frames", 0)-start_frames,
                               endDiagnosticModified=device.diagnostic_modified, finalState=snapshot["state"],
                               finalGeneration=snapshot["generation"], finalPlaying=snapshot["playing"],
                               endSnapshotMonotonicMs=snapshot_ms, wallElapsedSeconds=round(wall_elapsed, 3),
                               finalPlayWhenReady=snapshot.get("playWhenReady"),
                               finalSuppressionReason=snapshot.get("suppressionReason"),
                               finalHasFocus=value.get("hasFocus"), finalPresenceKnown=value.get("presenceKnown"),
                               finalUserPresent=value.get("userPresent"))
            measurement["samples"] += 1
            require(snapshot["generation"] == generation and snapshot["state"] == 3 and snapshot["playing"] and
                    not snapshot.get("error"), "Playback paused, changed generation or buffered during the continuous run")
            require(snapshot_ms >= last_snapshot_ms, "Native playback snapshot clock moved backwards")
            require(position >= last_position, "Playback position moved backwards during the continuous run")
            if snapshot_ms > last_snapshot_ms:
                last_new_snapshot_wall = time.monotonic()
                last_snapshot_ms = snapshot_ms
            if position > last_position:
                last_position = position
                last_progress_ms = snapshot_ms
            require(snapshot_ms-last_progress_ms < 5000, "Playback diagnostics stopped advancing")
            require(time.monotonic()-last_new_snapshot_wall <= 7,
                    "Fresh native playback snapshots stopped arriving")
            if elapsed >= seconds:
                # Use the player clock represented by this consistent sample.
                # ADB transfer/wait time does not establish observed playback.
                break
            require(wall_elapsed < seconds+20, "A fresh continuous-run terminal diagnostic did not arrive")
            time.sleep(.45)
        require(measurement["positionAdvancedMs"] >= elapsed*700 and measurement["decodedFramesAdvanced"] > 0,
                "Continuous run did not advance decoded video for the required duration")
        measurement["passed"] = True
        return measurement
    except Exception as error:
        measurement["failure"] = str(error) if isinstance(error, (CheckFailure, PresenceFailure)) else type(error).__name__
        raise


def end_without_transition(device, prepared, failure):
    generation = prepared["snapshot"]["generation"]
    duration = prepared["snapshot"]["duration"]
    require(duration > 4000, "Selected end-of-video sample is too short")
    pause_and_seek(device, duration-1000)
    device.command("play")
    device.wait(device.status, lambda value: value["snapshot"]["generation"] == generation and
                value["snapshot"]["state"] == 4, failure, timeout=35)
    deadline = time.monotonic()+3
    while time.monotonic() < deadline:
        state = device.status()["snapshot"]
        require(state["generation"] == generation and state["state"] == 4, failure)
        time.sleep(.45)


def stop_and_settle(device):
    device.command("stop")
    device.settled_reports()


def check_recent_location(device, item_id, main_id, passed, label):
    before = device.inspect()["emby"]
    require(before.get("currentId") == item_id and before.get("playbackMainId") == main_id and before.get("accountId"),
            "Recent-location sample is not the expected prepared Emby item")
    current = device.status()
    generation = current["snapshot"]["generation"]
    require(current["snapshot"]["state"] == 3 and not current["snapshot"]["playing"],
            "Recent-location regression needs a prepared, paused video")
    uri = "emby://"+urllib.parse.quote(before["accountId"], safe="")+"/"+urllib.parse.quote(item_id, safe="")
    device.command("source", name="recent")
    recent = device.inspect()
    require(recent.get("source") == "Recent" and any(item.get("uri") == uri for item in recent.get("files", [])),
            "Current paused Emby item did not appear in recent history")
    device.command("locate", uri=uri)
    detail = device.page("Details", selectedId=main_id)["emby"]
    require(detail.get("currentId") == item_id, "Locating a recent Emby item reopened a different video")
    assert_queue(device, before["playbackQueueKind"], before["playbackQueueIds"], before["playbackQueueIndex"], main_id)
    wait_snapshot(device, lambda value: value["snapshot"]["generation"] == generation and
                  value["snapshot"]["state"] == 3 and not value["snapshot"]["playing"] and
                  value["projection"] == current["projection"] and value["stereo"] == current["stereo"],
                  "Recent containing-location changed paused playback or its VR format", after_stamp=device.diagnostic_modified)
    passed("Recent containing-location opens "+label+" details without changing playback")


def assert_part_navigation(device, ids, index, generation=None, after_stamp=None):
    assert_queue(device, "Parts", ids, index, ids[0])
    inspected = device.inspect()["emby"]
    require(inspected.get("currentId") == ids[index] and inspected.get("partPlaylistIds") == ids and
            inspected.get("partPlaylistIndex") == index,
            "Adjacent navigation left the committed same-movie part queue")
    require(inspected.get("canPrevious") == (index > 0) and inspected.get("canNext") == (index < len(ids)-1),
            "Adjacent navigation boundaries do not match the committed part queue")
    return wait_snapshot(device, lambda value: (generation is None or value["snapshot"]["generation"] == generation) and
                         value["snapshot"]["state"] == 3 and not value["snapshot"].get("error") and
                         value["projection"] == "Equirect360" and value["stereo"] == "SideBySide",
                         "Adjacent same-movie navigation lost inherited VR format",
                         after_stamp=after_stamp, timeout=15)


def navigate_part(device, action, ids, index):
    prepared = prepared_action(device, action, ids[index])
    generation = prepared["snapshot"]["generation"]
    device.command("pause")
    wait_snapshot(device, lambda value: value["snapshot"]["generation"] == generation and
                  value["snapshot"]["state"] == 3 and not value["snapshot"]["playing"],
                  "Adjacent movie part did not pause after preparation", after_stamp=device.diagnostic_modified)
    return assert_part_navigation(device, ids, index, generation=generation)


def unchanged_part_boundary(device, action, ids, index):
    before = assert_part_navigation(device, ids, index)
    generation = before["snapshot"]["generation"]
    device.command(action)
    assert_part_navigation(device, ids, index, generation=generation, after_stamp=device.diagnostic_modified)
    deadline = time.monotonic()+2
    while time.monotonic() < deadline:
        value = assert_part_navigation(device, ids, index, generation=generation)
        require(value["snapshot"]["generation"] == generation and value["snapshot"]["state"] == 3,
                "Movie-part boundary wrapped or opened another video")
        time.sleep(.35)


def check_adjacent_parts(device, ids, passed):
    device.command("pause")
    device.wait(device.status, lambda value: not value["snapshot"]["playing"], "Main movie part did not pause")
    change_format(device, 2, 1, "Equirect360", "SideBySide")
    unchanged_part_boundary(device, "previous", ids, 0)
    navigate_part(device, "next", ids, 1)
    if len(ids) >= 3:
        navigate_part(device, "next", ids, 2)
        navigate_part(device, "previous", ids, 1)
        current = 1
    else:
        navigate_part(device, "previous", ids, 0)
        current = 0
    passed("Previous and next navigate only the same-movie parts and inherit VR format", partCount=len(ids))
    # Browsing Home must not replace the queue with its resume/latest posters.
    device.command("emby-home")
    device.page("Home")
    assert_part_navigation(device, ids, current)
    navigate_part(device, "next", ids, current+1)
    current += 1
    passed("Browsing another Emby page retains adjacent navigation within the committed movie")
    while current < len(ids)-1:
        current += 1
        navigate_part(device, "next", ids, current)
    unchanged_part_boundary(device, "next", ids, current)
    while current > 0:
        current -= 1
        navigate_part(device, "previous", ids, current)
    unchanged_part_boundary(device, "previous", ids, 0)
    passed("First and last movie-part navigation boundaries do not wrap or open foreign items")


def check_ordinary_movie_isolation(device, ordinary, passed):
    device.command("sequential", flag=False)
    value = prepared_action(device, "emby-play", ordinary["Id"], flag=False)
    pause_and_seek(device, 0)
    assert_queue(device, "None", [], -1, ordinary["Id"])
    check_recent_location(device, ordinary["Id"], ordinary["Id"], passed, "the ordinary film's own")
    change_format(device, 2, 1, "Equirect360", "SideBySide")
    generation = value["snapshot"]["generation"]
    for action in ("previous", "next"):
        device.command(action)
        wait_snapshot(device, lambda state: state["snapshot"]["generation"] == generation and
                      state["snapshot"]["state"] == 3 and not state["snapshot"]["playing"],
                      "Ordinary movie adjacent navigation opened another film", after_stamp=device.diagnostic_modified)
        assert_queue(device, "None", [], -1, ordinary["Id"])
    device.command("sequential", flag=True)
    end_without_transition(device, value, "Ordinary movie completion automatically opened another film")
    assert_queue(device, "None", [], -1, ordinary["Id"])
    passed("Ordinary movies never cross to another film through previous, next or automatic playback")
    stop_and_settle(device)


def check_pending_part_browsing(device, server, sample, passed):
    ids = [part["Id"] for part in sample["parts"]]
    device.command("sequential", flag=True)
    prepared = prepared_action(device, "emby-play", ids[0], flag=False)
    change_format(device, 2, 1, "Equirect360", "SideBySide")
    generation = prepared["snapshot"]["generation"]
    pause_and_seek(device, prepared["snapshot"]["duration"]-1000)
    delay_ms = 12000
    server.request("/Fixture/DelayPlaybackInfo", "POST", {"itemId": ids[1], "delayMs": delay_ms})
    try:
        device.command("play")

        def pending_state():
            # Polling the simulator never bypasses the worn/focused guard.
            device.status()
            return server.request("/Fixture/State")

        device.wait(pending_state, lambda value: ids[1] in value.get("pendingPlaybackInfoIds", []),
                    "Fixture did not observe the delayed automatic next-part request", timeout=25)
        device.command("emby-home")
        device.page("Home")
        pending = pending_state()
        require(ids[1] in pending.get("pendingPlaybackInfoIds", []),
                "PlaybackInfo delay ended before Emby Home browsing could verify the pending request")
        device.command("source", name="emby")
        device.page("Home")
        pending = pending_state()
        require(ids[1] in pending.get("pendingPlaybackInfoIds", []),
                "PlaybackInfo delay ended before the same-source Emby button could verify the pending request")
        transitioned = wait_snapshot(device, lambda value: value["snapshot"]["generation"] > generation and
                                     value["snapshot"]["state"] == 3 and not value["snapshot"].get("error"),
                                     "Header Home or the same-source Emby button cancelled a pending automatic movie-part transition",
                                     after_stamp=device.diagnostic_modified, timeout=120)
        current_generation = transitioned["snapshot"]["generation"]
        device.command("pause")
        wait_snapshot(device, lambda value: value["snapshot"]["generation"] == current_generation and
                      not value["snapshot"]["playing"], "Delayed next part did not pause",
                      after_stamp=device.diagnostic_modified)
        assert_part_navigation(device, ids, 1, generation=current_generation)
        require(device.inspect()["emby"].get("page") == "Home", "Delayed part transition replaced the selected Emby Home page")
        stop_and_settle(device)
    finally:
        server.request("/Fixture/DelayPlaybackInfo", "POST", {"itemId": ids[1], "delayMs": 0})
        require(ids[1] not in server.request("/Fixture/State").get("scheduledPlaybackInfoDelayIds", []),
                "Fixture PlaybackInfo delay configuration did not reset")
    passed("Header Home and the same-source Emby button preserve pending automatic part preparation",
           delayMs=delay_ms, browsingActions=["emby-home", "source:emby"])


def check_parts(device, sample, passed):
    main, parts = sample["main"], sample["parts"]
    ids = [part["Id"] for part in parts]
    device.command("emby-item", uri=main["Id"], name="验证正片")
    detail = device.page("Details", selectedId=main["Id"])["emby"]
    require(detail.get("partIds") == ids, "Parts detail does not preserve authoritative server order")
    require(not set(ids[1:]) & set(detail.get("extraIds", [])), "Additional parts were mixed into official extras")
    passed("Official additional-parts order and extras separation", partCount=len(ids))
    if device.args.capture:
        device.capture("parts-details")
    device.command("sequential", flag=False)
    first = prepared_action(device, "emby-play", ids[0], flag=False)
    check_adjacent_parts(device, ids, passed)
    # The explicit CD selection below is a Details action, so restore its page
    # after verifying that unrelated page browsing cannot change the queue.
    device.command("emby-item", uri=main["Id"], name="验证正片")
    device.page("Details", selectedId=main["Id"])
    change_format(device, 2, 1, "Equirect360", "SideBySide")
    second = prepared_action(device, "emby-part", ids[1])
    inspected = device.inspect()["emby"]
    require(inspected.get("partPlaylistIds") == ids and inspected.get("partPlaylistIndex") == 1,
            "Explicit part selection did not retain the same-movie queue")
    require(second["projection"] == "Equirect360" and second["stereo"] == "SideBySide",
            "Explicit same-movie part selection lost format inheritance")
    passed("Explicit same-movie part selection inherits format")
    pause_and_seek(device, 0)
    assert_queue(device, "Parts", ids, 1, main["Id"])
    check_recent_location(device, ids[1], main["Id"], passed, "the additional part's main-film")
    stop_and_settle(device)
    first = prepared_action(device, "emby-play", ids[0], flag=False)
    end_without_transition(device, first, "Disabled sequential playback unexpectedly opened another part")
    require(device.inspect()["emby"].get("partPlaylistIndex") == 0, "Disabled sequential playback changed the part index")
    passed("Disabled sequential playback stops at the current part")
    stop_and_settle(device)
    device.command("sequential", flag=True)
    first = prepared_action(device, "emby-play", ids[0], flag=False)
    change_format(device, 2, 1, "Equirect360", "SideBySide")
    generation = first["snapshot"]["generation"]
    pause_and_seek(device, first["snapshot"]["duration"]-1000)
    device.command("play")
    transitioned = device.wait(device.status, lambda value: value["snapshot"]["generation"] > generation and
                               value["snapshot"]["state"] == 3 and not value["snapshot"].get("error"),
                               "Enabled sequential playback did not open the next part", timeout=120)
    device.command("pause")
    inspected = device.inspect()["emby"]
    require(inspected.get("currentId") == ids[1] and inspected.get("partPlaylistIndex") == 1 and
            inspected.get("partPlaylistIds") == ids, "Automatic transition opened an item outside the same-movie queue")
    require(transitioned["projection"] == "Equirect360" and transitioned["stereo"] == "SideBySide",
            "Automatic same-movie transition lost format inheritance")
    passed("Enabled sequential playback advances only to the next same-movie part")
    if device.args.capture:
        device.capture("automatic-next-part")
    stop_and_settle(device)
    last = prepared_action(device, "emby-part", ids[-1])
    end_without_transition(device, last, "Last movie part automatically opened another item")
    require(device.inspect()["emby"].get("partPlaylistIndex") == len(ids)-1,
            "Last-part completion changed the authoritative queue index")
    passed("Last movie part stops without opening extras or another movie")
    stop_and_settle(device)


def choose_actor(server, main, max_candidates=6):
    seen, fallback = set(), (None, None)
    checked = 0
    for person in main.get("People", []):
        if not person.get("Id") or person.get("Type") != "Actor" or person["Id"] in seen:
            continue
        seen.add(person["Id"])
        expected = movie_query(server, PersonIds=person["Id"])
        checked += 1
        if fallback[0] is None:
            fallback = person, expected
        if len(rows(expected)) >= 2 and any(item["Id"] != main["Id"] for item in rows(expected)):
            return person, expected, checked
        if checked >= max_candidates:
            break
    return *fallback, checked


def check_metadata_browsing(device, server, library, main, passed, report):
    person, expected, checked = choose_actor(server, main)
    report["actorCandidatesChecked"] = checked
    if person:
        detail = server.item(person["Id"])
        device.command("emby-item", uri=main["Id"], name="验证正片")
        device.page("Details", selectedId=main["Id"])
        device.command("emby-person", uri=person["Id"])
        actor = device.page("Person", selectedId=person["Id"])["emby"]
        require(actor.get("itemIds") == [item["Id"] for item in rows(expected)] and
                actor.get("itemCount") == actor.get("totalCount") == expected["TotalRecordCount"] and
                actor.get("startIndex") == 0 and actor.get("personOverviewPresent") == bool((detail.get("Overview") or "").strip()),
                "Actor biography or complete filmography differs from the official server queries")
        other = next((item for item in rows(expected) if item["Id"] != main["Id"]), None)
        require(other is not None or not device.args.synthetic_fixture, "Synthetic actor needs another feature movie")
        if other:
            device.command("emby-item", uri=other["Id"], name="验证演员其他影片")
            device.page("Details", selectedId=other["Id"])
        passed("Actor details show biography and complete filmography", filmCount=len(rows(expected)), openedOtherFilm=other is not None)
        if other:
            passed("Actor filmography opens another movie's details")
        else:
            report["checks"].append({"name": "Actor filmography opens another movie's details", "passed": None,
                                     "reason": "Selected actor has no other accessible feature movie"})
        if device.args.capture:
            device.command("emby-person", uri=person["Id"])
            device.page("Person", selectedId=person["Id"])
            device.capture("actor-details")
    else:
        require(not device.args.synthetic_fixture, "Synthetic main film needs an actor")
        report["checks"].append({"name": "Actor biography and filmography", "passed": None, "reason": "Selected main has no actor identifier"})
    categories = [("Genre", value, "", {"Genres": value}) for value in main.get("Genres", [])[:1]]
    categories += [("Tag", value, "", {"Tags": value}) for value in main.get("Tags", [])[:1]]
    for studio in main.get("Studios", [])[:1]:
        if studio.get("Name"):
            identity = str(studio.get("Id", "")) if studio.get("Id") else ""
            categories.append(("Studio", studio["Name"], identity,
                               {"StudioIds": identity} if identity else {"Studios": studio["Name"]}))
    require(len(categories) == 3 or not device.args.synthetic_fixture, "Synthetic main needs Genre, Tag and Studio metadata")
    for kind in ("Genre", "Tag", "Studio"):
        if not any(category[0] == kind for category in categories):
            report["checks"].append({"name": "Complete movie wall filters by "+kind, "passed": None,
                                     "reason": "Selected main has no corresponding category metadata"})
    for kind, value, identity, query in categories:
        expected = movie_query(server, **query)
        device.command("emby-filter", name=kind, uri=value, subtitle=identity)
        filtered = device.page("Library", filterKind=kind, filterValue=value)["emby"]
        require(filtered.get("itemIds") == [item["Id"] for item in rows(expected)] and
                filtered.get("itemCount") == filtered.get("totalCount") == expected["TotalRecordCount"] and
                filtered.get("startIndex") == 0,
                "Category filtering did not return one complete, ordered movie wall")
        passed("Complete movie wall filters by "+kind, movieCount=expected["TotalRecordCount"])
        search = main.get("Name", "")
        if search:
            expected_search = movie_query(server, SearchTerm=search, **query)
            device.command("emby-search", subtitle=search)
            # The command starts an async request on the same page. Include the
            # new result in the wait predicate to reject the previous wall.
            searched = device.page("Library", filterKind=kind, filterValue=value,
                                   itemIds=[item["Id"] for item in rows(expected_search)])["emby"]
            require(searched.get("itemIds") == [item["Id"] for item in rows(expected_search)] and
                    searched.get("itemCount") == searched.get("totalCount") == expected_search["TotalRecordCount"] and
                    searched.get("startIndex") == 0,
                    "Search did not preserve the selected category on the same complete wall")
        if device.args.capture:
            device.capture("filter-"+kind.lower())
    if categories:
        passed("Search remains within the selected category on the same wall", categoryKindsChecked=len(categories))
    device.command("emby-library", uri=library["Id"], name="验证电影库", collectionType="movies")
    device.page("Library", startIndex=0)


def check_extra_navigation(device, sample, passed, report):
    pair = sample.get("navigationPair", [])
    if len(pair) != 2:
        report["checks"].append({"name": "Same-film extra previous/next and direct-card saved formats", "passed": None,
                                 "reason": "No consecutive official extras of at least12 seconds"})
        return
    ids = [item["Id"] for item in sample["extraItems"]]
    main_id = sample["main"]["Id"]
    first_index, second_index = (ids.index(item["Id"]) for item in pair)
    require(second_index == first_index+1, "Extra navigation samples must be adjacent in authoritative server order")
    for item, projection, stereo, expected_projection, expected_stereo in (
            (pair[0], 0, 0, "Flat", "Mono"), (pair[1], 2, 1, "Equirect360", "SideBySide")):
        prepared_action(device, "emby-extra", item["Id"])
        pause_and_seek(device, 0)
        change_format(device, projection, stereo, expected_projection, expected_stereo)
        stop_and_settle(device)
    first = prepared_action(device, "emby-extra", pair[0]["Id"])
    require(first["projection"] == "Flat" and first["stereo"] == "Mono", "Direct extra card did not restore its saved format")
    pause_and_seek(device, 0)
    assert_queue(device, "Extras", ids, first_index, main_id)
    if first_index == 0:
        generation = device.status()["snapshot"]["generation"]
        device.command("previous")
        wait_snapshot(device, lambda value: value["snapshot"]["generation"] == generation,
                      "First extra previous crossed to the main film", after_stamp=device.diagnostic_modified)
        assert_queue(device, "Extras", ids, first_index, main_id)
    for action, target, index in (("next", pair[1], second_index), ("previous", pair[0], first_index)):
        value = prepared_action(device, action, target["Id"])
        pause_and_seek(device, 0)
        require(value["projection"] == "Flat" and value["stereo"] == "Mono", "Explicit extra adjacency did not inherit current VR format")
        assert_queue(device, "Extras", ids, index, main_id)
    change_format(device, 2, 1, "Equirect360", "SideBySide")
    second = prepared_action(device, "emby-extra", pair[1]["Id"])
    pause_and_seek(device, 0)
    require(second["projection"] == "Flat" and second["stereo"] == "Mono", "Direct extra card inherited another extra instead of restoring its record")
    assert_queue(device, "Extras", ids, second_index, main_id)
    if second_index == len(ids)-1:
        generation = device.status()["snapshot"]["generation"]
        device.command("next")
        wait_snapshot(device, lambda value: value["snapshot"]["generation"] == generation,
                      "Last extra next crossed to a main film or another movie", after_stamp=device.diagnostic_modified)
        assert_queue(device, "Extras", ids, second_index, main_id)
    stop_and_settle(device)
    report["extrasAdjacentNavigationVerified"] = True
    passed("Same-film extras inherit format for previous/next and restore saved format for direct cards")


def check_extras(device, server, sample, passed, report):
    main = sample["main"]
    additional = parts_for(server, main["Id"])
    expected_parts = [main["Id"]]+[item["Id"] for item in additional] if additional else []
    expected_extras = [item["Id"] for item in sample["extraItems"]]
    device.command("emby-item", uri=main["Id"], name="验证正片")
    detail = device.page("Details", selectedId=main["Id"])["emby"]
    require(detail.get("extraIds") == expected_extras and detail.get("extraCount") == len(expected_extras),
            "Official extras detail differs from authoritative special-features and local-trailers endpoints")
    require(detail.get("partIds") == expected_parts, "Extras detail polluted the additional-parts section")
    passed("Film details include authoritative special features and trailers", extraCount=len(expected_extras), partCount=len(expected_parts))
    if device.args.capture:
        device.capture("extras-details")
    device.command("sequential", flag=False)
    original = prepared_action(device, "emby-play", main["Id"], flag=False)
    continuous_sample(device, seconds=5, label="main-before-extras")
    duration = original["snapshot"]["duration"]
    # A minute of a long film can fall below the server's minimum resume
    # percentage. A position within the film avoids that policy boundary.
    target = duration//3
    require(target > 1000, "Original movie sample is too short for resume isolation")
    resume_measurement = {"label": "main-stop-before-extras", "passed": False, "durationMs": duration,
                          "expectedPositionMs": target, "expectedFraction": target/duration}
    device.resume_measurements.append(resume_measurement)
    seeked = pause_and_seek(device, target)
    resume_measurement["actualPositionMs"] = seeked["snapshot"]["position"]
    change_format(device, 2, 1, "Equirect360", "SideBySide")
    stop_and_settle(device)
    server_data = server.item(main["Id"]).get("UserData", {})
    saved = server_data.get("PlaybackPositionTicks", 0)//10000
    resume_measurement.update(serverSavedPositionMs=saved, serverPlayed=server_data.get("Played", False))
    require(abs(saved-target) < 2000, "Original movie stop did not synchronize its independent resume point")
    resume_measurement["passed"] = True
    for extra_index, extra in enumerate(sample["chosen"], 1):
        value = prepared_action(device, "emby-extra", extra["Id"])
        # Format controls, diagnostic barriers and screenshots must not consume
        # a short extra's playback window. Its backed-up server data is restored
        # after the run; the controlled playback sample starts from its head.
        pause_and_seek(device, 0)
        change_format(device, 0, 0, "Flat", "Mono")
        inspected = device.inspect()["emby"]
        require(not inspected.get("partPlaylistIds") and inspected.get("partPlaylistIndex") == -1,
                "Playing an official extra retained a main-film or adjacent-film playlist")
        extra_ids = [item["Id"] for item in sample["extraItems"]]
        assert_queue(device, "Extras", extra_ids, extra_ids.index(extra["Id"]), main["Id"])
        if extra_index == 1:
            check_recent_location(device, extra["Id"], main["Id"], passed, "the extra's main-film")
        if device.args.capture:
            device.capture("extra-prepared-"+str(extra_index))
        continuous_sample(device, seconds=3, label="extra-"+str(extra_index))
        device.command("sequential", flag=True)
        end_without_transition(device, value, "Official extra completion automatically opened a main-film part or other item")
        inspected = device.inspect()["emby"]
        require(inspected.get("currentId") == extra["Id"] and not inspected.get("partPlaylistIds"),
                "Extra completion replaced its independent playback identity")
        stop_and_settle(device)
        current = server.item(main["Id"]).get("UserData", {}).get("PlaybackPositionTicks", 0)//10000
        extra_resume = {"label": "main-after-extra-"+str(extra_index), "passed": abs(current-saved) < 2000,
                        "expectedPositionMs": saved, "serverSavedPositionMs": current}
        device.resume_measurements.append(extra_resume)
        require(abs(current-saved) < 2000, "Extra playback changed the original movie resume position")
    passed("Official extras play independently and do not auto-join main-film queues", extraVideosPlayed=len(sample["chosen"]))
    check_extra_navigation(device, sample, passed, report)
    current = server.item(main["Id"]).get("UserData", {}).get("PlaybackPositionTicks", 0)//10000
    require(abs(current-saved) < 2000, "Explicit extra navigation changed the original movie resume position")
    resumed = prepared_action(device, "emby-play", main["Id"], flag=True)
    reopened_resume = {"label": "main-reopen-after-extras", "passed": False, "expectedPositionMs": saved,
                       "actualPositionMs": resumed["snapshot"]["position"]}
    device.resume_measurements.append(reopened_resume)
    require(saved-2000 <= resumed["snapshot"]["position"] <= saved+10000,
            "Original movie did not resume independently after extra playback")
    require(resumed["projection"] == "Equirect360" and resumed["stereo"] == "SideBySide",
            "Extra formats replaced the original movie manual format")
    reopened_resume["passed"] = True
    stop_and_settle(device)
    passed("Main movie resume and manual VR format remain independent of extras")


def run(args):
    run_id = datetime.now(base.SHANGHAI).strftime("%Y%m%d-%H%M%S")+"-"+uuid.uuid4().hex[:6]
    validation = ROOT/"Docs/Validation"
    validation.mkdir(parents=True, exist_ok=True)
    output = validation/("emby-movie-parts-device-"+run_id+".json")
    report = {"started": datetime.now(base.SHANGHAI).isoformat(), "passed": False, "checks": [],
              "validationScope": "synthetic-fixture-adb-reverse" if args.synthetic_fixture else "real-designated-server-LAN",
              "measurementClock": "snapshot.monotonicMs", "diagnosticReadMethod": "single-adb-stat-json-stat",
              "navigationPolicy": "ordinary-isolated;parts-same-main;extras-explicit-only",
              "realMultipartPlaybackVerified": False, "syntheticMultipartPlaybackVerified": False,
              "ordinaryMovieIsolationVerified": False, "extrasAdjacentNavigationVerified": False,
              "syntheticPendingPartBrowsingVerified": False,
              "limitations": ["Short official-video checks cannot establish long-film compatibility.",
                              "Development commands do not validate physical controllers, readability or headset comfort.",
                              "Missing real AdditionalParts samples remain explicitly unverified."]}
    if args.synthetic_fixture:
        report["limitations"].append("Synthetic AdditionalParts over adb reverse validate the hardware path, not real-server multipart compatibility.")
    device, server = GuardedDevice(args, run_id), DetailedServer(args.server)
    password = os.environ.get("EMBY_TEST_PASSWORD")
    if password is None:
        password = getpass.getpass("Emby test password: ")
    account_id, original_ids = "", set()
    original_data = {}
    launched = False
    cleanup_errors = []

    def passed(name, **metrics):
        report["checks"].append({"name": name, "passed": True, **metrics})
        print("PASS "+name, flush=True)

    try:
        device.backup(validation)
        original_settings = next(entry["bytes"] for entry in device.backups if entry["label"] == "settings")
        original_accounts = next(entry["bytes"] for entry in device.backups if entry["label"] == "accounts")
        accounts = json.loads(original_accounts).get("accounts", []) if original_accounts else []
        original_ids = {entry.get("id") for entry in accounts}
        server.login(args.username, password)
        require(not any(entry.get("userId") == server.user and base.normalized_server(entry.get("serverUrl", "")) ==
                        base.normalized_server(args.server) for entry in accounts),
                "Use a dedicated test account that is not already registered on the device")
        all_movies, split, extras = discover(server)
        require(split is not None or not args.require_parts,
                "Known official AdditionalParts sample was not discovered; required real multipart validation cannot be skipped")
        ordinary = None
        if args.synthetic_fixture:
            require(split is not None, "Synthetic server needs an official multipart movie")
            state = server.request("/Fixture/State")
            require("scheduledPlaybackInfoDelayIds" in state and "pendingPlaybackInfoIds" in state and
                    not state["scheduledPlaybackInfoDelayIds"] and not state["pendingPlaybackInfoIds"],
                    "Synthetic server needs isolated, initially clear PlaybackInfo-delay controls")
            report["fixtureDelayControlsInitiallyClear"] = True
            ordinary = server.item("ordinary")
            require(ordinary.get("Type") == "Movie" and not parts_for(server, ordinary["Id"]),
                    "Synthetic ordinary movie must have no official additional parts")
        else:
            excluded = {extras["main"]["Id"]} | ({split["main"]["Id"]} if split else set())
            for movie in sorted(all_movies, key=sample_video_rank):
                if (movie["Id"] not in excluded and movie.get("RunTimeTicks", 0) >= 12*10_000_000 and
                        not unsupported_h264_sample(movie) and not parts_for(server, movie["Id"])):
                    candidate = server.item(movie["Id"])
                    if not unsupported_h264_sample(candidate):
                        ordinary = candidate
                        break
        planned = ([extras["main"]]+extras["chosen"]+extras.get("navigationPair", [])+
                   (split["parts"] if split else [])+([ordinary] if ordinary else []))
        for item in planned:
            if item["Id"] in original_data:
                continue
            original_data[item["Id"]] = copy.deepcopy(server.item(item["Id"]).get("UserData", {}))
            (validation/("emby-parts-"+run_id+"-media-"+str(len(original_data))+".backup.json")).write_text(
                json.dumps({"itemId": item["Id"], "userData": original_data[item["Id"]]}, indent=2), encoding="utf-8")
        report["accessibleMovieCount"] = len(all_movies)
        report["plannedServerUserDataBackupCount"] = len(original_data)
        settings = json.loads(original_settings) if original_settings else {"version": 3}
        settings.update(version=3, autoLoadSubtitles=False, autoHide=False, formatDetection=0,
                        loop=False, sequential=False, speed=1, volume=0, history=[])
        device.write_external(EXTERNAL+"settings.json", json.dumps(settings).encode("utf-8"))
        launch_time = time.time()
        device.launch()
        launched = True
        device.begin_guard(launch_time)
        device.command("emby-login", uri=args.server, username=args.username, password=password, flag=False)
        password = ""
        home = device.page("Home")
        account_id = home["emby"]["accountId"]
        require(all(field in home["emby"] for field in ("partPlaylistIds", "extraIds", "playbackQueueKind",
                                                       "playbackQueueIds", "playbackQueueIndex", "playbackMainId",
                                                       "filterKind", "filterValue", "personOverviewPresent")),
                "APK lacks current same-film navigation and metadata diagnostics")
        views = rows(server.request(server.user_path("/Views")))
        library = next((view for view in views if view.get("CollectionType") == "movies"), None)
        require(library is not None, "Designated account needs a movies collection for poster-wall regression")
        expected = movie_query(server, ParentId=library["Id"])
        device.command("emby-library", uri=library["Id"], name="验证电影库", collectionType="movies")
        poster = device.page("Library", startIndex=0)["emby"]
        report["wallCompare"] = base.wall_comparison(poster, expected)
        require(poster.get("itemIds") == [item["Id"] for item in rows(expected)] and
                poster.get("itemCount") == poster.get("totalCount") == expected["TotalRecordCount"] and
                poster.get("startIndex") == 0,
                "Movie poster wall is incomplete or differs from the complete recursive Movie-only query")
        require(all(item.get("Type") == "Movie" for item in rows(expected)), "Movie-only server query returned directory or extra items")
        require(not args.synthetic_fixture or poster["itemCount"] == 205,
                "Synthetic wall must aggregate its 205 movies across internal 200-item API pages")
        passed("Movies collection shows one complete wall of main-film posters without directory wrappers", totalMovies=poster["totalCount"])
        report["completeMovieWallVerified"] = True
        if args.capture:
            device.capture("movie-library")
        # A short purpose-made CD sample can have no scraped actor/category
        # metadata. Prefer a rich detail among the already discovered films.
        metadata_candidates = [extras["main"]]+([split["main"]] if split else [])+([ordinary] if ordinary else [])
        metadata_main = max(metadata_candidates, key=lambda item: sum((
            any(person.get("Id") and person.get("Type") == "Actor" for person in item.get("People", [])),
            bool(item.get("Genres")), bool(item.get("Tags")), bool(item.get("Studios")))))
        check_metadata_browsing(device, server, library, metadata_main, passed, report)
        if ordinary:
            check_ordinary_movie_isolation(device, ordinary, passed)
            report["ordinaryMovieIsolationVerified"] = True
        else:
            report["checks"].append({"name": "Ordinary movies never cross to another film through previous, next or automatic playback",
                                     "passed": None, "reason": "No separate ordinary film of at least12 seconds was available"})
        if split:
            check_parts(device, split, passed)
            report["realMultipartPlaybackVerified"] = not args.synthetic_fixture
            report["syntheticMultipartPlaybackVerified"] = args.synthetic_fixture
            if args.synthetic_fixture:
                check_pending_part_browsing(device, server, split, passed)
                report["syntheticPendingPartBrowsingVerified"] = True
        else:
            for name in ("Previous and next same-movie part navigation", "Same-movie navigation after unrelated page browsing",
                         "First and last part navigation boundaries", "Explicit same-movie part selection",
                         "Automatic same-movie part transition", "Last-part stop", "Disabled sequential part playback"):
                report["checks"].append({"name": name, "passed": None, "reason": "Designated server has no official AdditionalParts sample"})
            print("SKIP Real multipart checks: designated server has no official AdditionalParts sample", flush=True)
        if not args.synthetic_fixture:
            report["checks"].append({"name": "Header Home and the same-source Emby button preserve pending automatic part preparation", "passed": None,
                                     "reason": "A controlled PlaybackInfo delay requires the isolated synthetic server"})
        check_extras(device, server, extras, passed, report)
        device.command("emby-item", uri=extras["main"]["Id"], name="验证正片")
        device.command("source", name="local")
        time.sleep(3)
        require(device.inspect().get("source") == "Home", "Obsolete details/extra requests replaced the selected local source")
        passed("Leaving details cancels obsolete part and extras requests")
        report["focusPresenceGuardSamples"] = device.focus_samples
        report["passed"] = True
    except Exception as error:
        report["failure"] = str(error) if isinstance(error, (CheckFailure, PresenceFailure)) else type(error).__name__
        report["focusPresenceInterrupted"] = isinstance(error, PresenceFailure)
    finally:
        device.guarded = False
        report["continuousRuns"] = device.continuous_runs
        report["prepareFailures"] = device.prepare_failures
        report["resumeMeasurements"] = device.resume_measurements
        report["inconsistentDiagnosticReadsDiscarded"] = device.inconsistent_diagnostic_reads
        if launched:
            try:
                device.command("stop")
                device.settled_reports()
            except Exception:
                cleanup_errors.append("Final playback reports did not settle")
            temporary_ids = {account_id} if account_id and account_id not in original_ids else set()
            try:
                if device.exists(EXTERNAL+"emby-accounts.json"):
                    metadata = json.loads(device.read(EXTERNAL+"emby-accounts.json"))
                    require(isinstance(metadata, dict) and isinstance(metadata.get("accounts"), list), "Temporary account metadata is malformed")
                    for entry in metadata["accounts"]:
                        require(isinstance(entry, dict) and isinstance(entry.get("id"), str) and entry["id"], "Temporary account metadata is malformed")
                        if entry["id"] not in original_ids:
                            temporary_ids.add(entry["id"])
            except Exception:
                cleanup_errors.append("Temporary account recovery failed")
            try:
                current = device.inspect().get("emby", {}).get("accountId", "")
                if current and current not in original_ids:
                    temporary_ids.add(current)
            except Exception:
                cleanup_errors.append("Temporary account diagnostics recovery failed")
            for identity in temporary_ids:
                try:
                    device.command("emby-remove", name=identity)
                    device.settled_reports()
                except Exception:
                    cleanup_errors.append("Temporary account logout failed")
        device.awake.set()
        if device.wake_thread is not None:
            device.wake_thread.join(timeout=45)
        try:
            device.adb("shell", "am", "force-stop", PACKAGE)
        except Exception:
            cleanup_errors.append("Could not stop the app before store restoration")
        restored_count = 0
        for identity, data in original_data.items():
            try:
                server.restore_user_data(identity, data)
                restored_count += 1
            except Exception:
                cleanup_errors.append("Server user-data restoration or verification failed")
        report["restoredServerUserDataCount"] = restored_count
        report["serverUserDataRestored"] = bool(original_data) and restored_count == len(original_data)
        if args.synthetic_fixture and server.token and report.get("fixtureDelayControlsInitiallyClear"):
            try:
                server.request("/Fixture/DelayPlaybackInfo", "POST", {"itemId": split["parts"][1]["Id"], "delayMs": 0})
                base.Device.wait(lambda: server.request("/Fixture/State"),
                                 lambda state: not state.get("scheduledPlaybackInfoDelayIds") and
                                 not state.get("pendingPlaybackInfoIds"),
                                 "Synthetic delay controls or delayed requests remained active", timeout=15)
                report["fixtureDelayConfigurationRestored"] = True
            except Exception:
                cleanup_errors.append("Synthetic PlaybackInfo delay reset or verification failed")
        if server.token:
            try:
                server.request("/Sessions/Logout", "POST", {})
                server.token = ""
                report["independentSessionLoggedOut"] = True
            except Exception:
                cleanup_errors.append("Independent validation session logout failed")
        try:
            device.restore()
            report["deviceStoresRestored"] = True
        except Exception:
            cleanup_errors.append("Device store restoration or byte verification failed")
        try:
            device.cleanup_commands()
            report["temporaryCommandsRemoved"] = True
        except Exception:
            cleanup_errors.append("Temporary command cleanup failed")
        if cleanup_errors:
            report["passed"] = False
            report["cleanupErrors"] = cleanup_errors
        report["finished"] = datetime.now(base.SHANGHAI).isoformat()
        output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print("PASS Emby movie parts/extras regression" if report["passed"] else "FAIL Emby movie parts/extras regression", flush=True)
    print("Private validation record: "+str(output), flush=True)
    if "failure" in report:
        print("Failure: "+report["failure"], flush=True)
    for error in cleanup_errors:
        print("Cleanup failure: "+error, flush=True)
    return 0 if report["passed"] else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--server", required=True, help="User-designated Emby server")
    parser.add_argument("--username", required=True)
    parser.add_argument("--adb", default=base.DEFAULT_ADB)
    parser.add_argument("--capture", action="store_true", help="Save private screenshots to ignored Docs/Captures")
    parser.add_argument("--synthetic-fixture", action="store_true",
                        help="Label fixture playback over adb reverse separately from real LAN validation; does not configure adb reverse")
    parser.add_argument("--require-parts", action="store_true",
                        help="Fail instead of skipping when a known real AdditionalParts sample is not discovered")
    return run(parser.parse_args())


if __name__ == "__main__":
    raise SystemExit(main())
