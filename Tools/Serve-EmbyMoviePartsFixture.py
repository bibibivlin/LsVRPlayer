"""Serve an isolated Emby movie-parts fixture using only the Python standard library.

Example (use a short, existing MP4 whose actual duration matches --duration-ms):
    python Tools/Serve-EmbyMoviePartsFixture.py --media TestMedia/fixture.mp4 --duration-ms 30000

Listens on loopback port 18096; the device-test owner manages adb reverse and its
restoration. Login is fixture / fixture. Tokens and all user data exist only in
memory. IDs: movies (library), ordinary, main (CD1), cd2, cd3, pv, trailer. The
ordinary movie precedes the multipart movie in metadata lists. AdditionalParts
returns only CD2/CD3 with the real server's ExtraType=AdditionalPart; official
extras endpoints return PV and trailer separately. A 205-movie catalogue checks
API pagination behind one complete poster wall. Actor/category routes are metadata.
The original MP4 is streamed unchanged, with bounded reads and HTTP byte ranges.
Its real filename/path is never included in API data or request/error logs.
GET /Fixture/State provides authenticated, token-free playback reports for tests.
POST /Fixture/DelayPlaybackInfo {"itemId":"cd2","delayMs":12000} delays that
item's next PlaybackInfo response once (0 clears an unused delay, maximum 12000).
State includes pendingPlaybackInfoIds and scheduledPlaybackInfoDelayIds so tests
can observe the delay window without relying on a guessed wait.
This simulator verifies client behavior; it is not an actual Emby server test.
"""
import argparse
import copy
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import re
import secrets
import threading
import time
import urllib.parse
import uuid


USER_ID = "fixture-user"
SERVER_ID = "lsvrplayer-movie-parts-fixture"
FILLER_IDS = tuple("movie-"+str(index).zfill(3) for index in range(1, 204))
MOVIE_IDS = ("ordinary", "main")+FILLER_IDS
PLAYABLE_IDS = ("ordinary", "main", "cd2", "cd3", "pv", "trailer")+FILLER_IDS
JSON_LIMIT = 1024 * 1024


def utc_now():
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def byte_range(header, size):
    """Return an inclusive single range, or raise ValueError for a 416 response."""
    if header is None:
        return 0, size - 1, False
    match = re.fullmatch(r"bytes=(\d*)-(\d*)", header.strip())
    if not match or not any(match.groups()) or size <= 0:
        raise ValueError("Invalid byte range")
    first, last = match.groups()
    if not first:
        count = int(last)
        if count <= 0:
            raise ValueError("Invalid byte range")
        return max(0, size - count), size - 1, True
    start = int(first)
    end = min(int(last), size - 1) if last else size - 1
    if start >= size or end < start:
        raise ValueError("Unsatisfiable byte range")
    return start, end, True


class FixtureState:
    def __init__(self, media, duration_ms):
        self.media = Path(media)
        stat = self.media.stat()
        self.size = stat.st_size
        self.modified = datetime.fromtimestamp(stat.st_mtime, timezone.utc).isoformat().replace("+00:00", "Z")
        self.duration_ticks = duration_ms * 10000
        self.lock = threading.RLock()
        self.tokens = {}
        self.playing = {}
        self.reports = []
        self.user_data = {item_id: self.empty_user_data() for item_id in PLAYABLE_IDS}
        self.playback_info_delays = {}
        self.pending_playback_info = {}

    @staticmethod
    def empty_user_data():
        return {"PlaybackPositionTicks": 0, "PlayCount": 0, "PlayedPercentage": 0,
                "Played": False, "IsFavorite": False}

    def source(self, item_id):
        suffix = {"ordinary": "ordinary", "main": "cd1", "cd2": "cd2", "cd3": "cd3", "pv": "pv", "trailer": "trailer"}.get(item_id, item_id)
        return {"Id": "source-" + item_id, "Name": "Fixture " + suffix,
                "Path": "/fixture/fixture-" + suffix + ".mp4", "Protocol": "File", "Container": "mp4",
                "Size": self.size, "RunTimeTicks": self.duration_ticks, "DateModified": self.modified,
                "SupportsDirectPlay": True, "SupportsDirectStream": True, "SupportsTranscoding": False,
                "IsInfiniteStream": False, "RequiresOpening": False, "MediaStreams": []}

    def item(self, item_id):
        if item_id == "movies":
            return {"Id": "movies", "Name": "Fixture Movies", "Type": "CollectionFolder",
                    "CollectionType": "movies", "IsFolder": True, "ImageTags": {}}
        if item_id == "fixture-folder":
            return {"Id": item_id, "Name": "Fixture Files", "Type": "Folder",
                    "ParentId": "movies", "IsFolder": True, "ImageTags": {}}
        if item_id == "fixture-actor":
            return {"Id": item_id, "Name": "Fixture Actor", "Type": "Person", "IsFolder": False,
                    "Overview": "A synthetic actor with two related feature movies.", "ImageTags": {}}
        if item_id not in PLAYABLE_IDS:
            raise KeyError("Unknown fixture item")
        names = {"ordinary": "Fixture Ordinary Movie", "main": "Fixture Movie CD1", "cd2": "Fixture Movie CD2", "cd3": "Fixture Movie CD3",
                 "pv": "Fixture PV", "trailer": "Fixture Trailer"}
        names.update({identity: "Fixture Catalogue Movie "+identity[-3:] for identity in FILLER_IDS})
        item_type = "Movie" if item_id in MOVIE_IDS else "Trailer" if item_id == "trailer" else "Video"
        source = self.source(item_id)
        with self.lock:
            data = copy.deepcopy(self.user_data[item_id])
        sort_name = "01 Ordinary" if item_id == "ordinary" else "02 Multipart" if item_id == "main" else names[item_id]
        group = "main" if item_id == "main" or item_id in FILLER_IDS and int(item_id[-3:]) % 2 == 0 else "ordinary"
        genre = "Fixture Action" if group == "main" else "Fixture Drama"
        tag = "Fixture Adventure" if group == "main" else "Fixture Quiet"
        studio = {"Id": 9001 if group == "main" else 9002, "Name": "Fixture Studio "+group.title()}
        result = {"Id": item_id, "Name": names[item_id], "SortName": sort_name, "Type": item_type,
                  "IsFolder": False, "ParentId": "main" if item_id in ("cd2", "cd3", "pv", "trailer") else "fixture-folder",
                  "Path": source["Path"], "DateModified": self.modified, "Size": self.size,
                  "RunTimeTicks": self.duration_ticks, "MediaSources": [source], "UserData": data,
                  "Overview": "Isolated movie-parts playback fixture.", "ProductionYear": 2026,
                  "People": [{"Id": "fixture-actor", "Name": "Fixture Actor", "Type": "Actor", "Role": "Fixture"}] if item_id in ("ordinary", "main") else [],
                  "Genres": [genre], "Tags": [tag], "Studios": [studio], "ImageTags": {}, "BackdropImageTags": [],
                  "PartCount": 3 if item_id == "main" else 1,
                  "SpecialFeatureCount": 1 if item_id == "main" else 0,
                  "LocalTrailerCount": 1 if item_id == "main" else 0}
        if item_id in ("pv", "trailer"):
            result["ExtraType"] = "Clip" if item_id == "pv" else "Trailer"
        if item_id in ("cd2", "cd3"):
            result["ExtraType"] = "AdditionalPart"
        return result

    def library_items(self, query):
        parent = query.get("parentid", "")
        recursive = query.get("recursive", "false").lower() == "true"
        types = {value.lower() for value in query.get("includeitemtypes", "").split(",") if value}
        if recursive and parent in ("", "movies", "fixture-folder"):
            ids = list(MOVIE_IDS)
        elif parent == "movies":
            ids = ["fixture-folder"]
        elif parent == "fixture-folder":
            ids = list(MOVIE_IDS)
        elif not parent:
            ids = ["movies"]
        else:
            ids = []
        items = [self.item(item_id) for item_id in ids]
        if types:
            items = [item for item in items if item["Type"].lower() in types]
        if query.get("personids"):
            people = set(query["personids"].split(","))
            items = [item for item in items if any(person["Id"] in people for person in item.get("People", []))]
        for field, key in (("genres", "Genres"), ("tags", "Tags")):
            if query.get(field):
                values = set(query[field].split("|"))
                items = [item for item in items if values.intersection(item.get(key, []))]
        if query.get("studioids"):
            values = set(query["studioids"].split(","))
            items = [item for item in items if any(str(studio["Id"]) in values for studio in item.get("Studios", []))]
        elif query.get("studios"):
            values = set(query["studios"].split("|"))
            items = [item for item in items if any(studio["Name"] in values for studio in item.get("Studios", []))]
        search = query.get("searchterm", "").strip().casefold()
        if search:
            items = [item for item in items if search in item["Name"].casefold()]
        items.sort(key=lambda item: item.get("SortName", item["Name"]).casefold())
        if query.get("sortorder", "").lower() == "descending":
            items.reverse()
        start = max(0, int(query.get("startindex", 0)))
        limit = max(0, min(1000, int(query.get("limit", 30))))
        return {"Items": items[start:start + limit], "TotalRecordCount": len(items), "StartIndex": start}

    def report(self, auth_token, event, payload):
        item_id = payload.get("ItemId")
        if item_id not in PLAYABLE_IDS:
            raise ValueError("Unknown playback item")
        session_id = str(payload.get("PlaySessionId") or "fixture-playback")
        position = max(0, min(self.duration_ticks, int(payload.get("PositionTicks", 0))))
        with self.lock:
            data = self.user_data[item_id]
            if event == "start" and session_id not in self.playing:
                data["PlayCount"] = int(data.get("PlayCount", 0)) + 1
            data["LastPlayedDate"] = utc_now()
            data["PlaybackPositionTicks"] = position
            data["PlayedPercentage"] = 100 * position / self.duration_ticks
            record = {"event": event, "itemId": item_id, "positionTicks": position,
                      "paused": bool(payload.get("IsPaused", False)), "playSessionId": session_id}
            self.reports.append(record)
            del self.reports[:-1000]
            if event == "stop":
                self.playing.pop(session_id, None)
                if position >= self.duration_ticks * 0.95:
                    data["Played"] = True
                    data["PlaybackPositionTicks"] = 0
                    data["PlayedPercentage"] = 100
            else:
                self.playing[session_id] = {"authToken": auth_token, **record}

    def set_user_data(self, item_id, payload):
        allowed = {"PlaybackPositionTicks", "PlayCount", "PlayedPercentage", "Played", "IsFavorite",
                   "LastPlayedDate", "Rating", "Key", "ItemId", "UnplayedItemCount"}
        with self.lock:
            data = self.user_data[item_id]
            for key in allowed & payload.keys():
                if payload[key] is None:
                    data.pop(key, None)
                else:
                    data[key] = payload[key]
            return copy.deepcopy(data)

    def mark_played(self, item_id, played):
        with self.lock:
            data = self.user_data[item_id]
            data.update(Played=played, PlaybackPositionTicks=0, PlayedPercentage=100 if played else 0,
                        PlayCount=max(1, int(data.get("PlayCount", 0))) if played else 0)
            if played:
                data["LastPlayedDate"] = utc_now()
            else:
                data.pop("LastPlayedDate", None)
            return copy.deepcopy(data)

    def snapshot(self):
        with self.lock:
            return {"reports": copy.deepcopy(self.reports), "userData": copy.deepcopy(self.user_data),
                    "playing": [{key: value for key, value in entry.items() if key != "authToken"}
                                for entry in self.playing.values()], "activeLoginCount": len(self.tokens),
                    "pendingPlaybackInfoIds": sorted(self.pending_playback_info),
                    "scheduledPlaybackInfoDelayIds": sorted(self.playback_info_delays)}

    def schedule_playback_info_delay(self, payload):
        item_id = payload.get("itemId")
        delay_ms = int(payload.get("delayMs", 0))
        if item_id not in PLAYABLE_IDS or not 0 <= delay_ms <= 12000:
            raise ValueError("Invalid fixture delay")
        with self.lock:
            if delay_ms:
                self.playback_info_delays[item_id] = delay_ms
            else:
                self.playback_info_delays.pop(item_id, None)
        return {"itemId": item_id, "delayMs": delay_ms}

    def delay_playback_info(self, item_id):
        with self.lock:
            delay_ms = self.playback_info_delays.pop(item_id, 0)
            if not delay_ms:
                return
            self.pending_playback_info[item_id] = self.pending_playback_info.get(item_id, 0) + 1
        try:
            time.sleep(delay_ms / 1000)
        finally:
            with self.lock:
                remaining = self.pending_playback_info[item_id] - 1
                if remaining:
                    self.pending_playback_info[item_id] = remaining
                else:
                    self.pending_playback_info.pop(item_id)


class FixtureServer(ThreadingHTTPServer):
    daemon_threads = True

    def __init__(self, address, state):
        self.state = state
        super().__init__(address, FixtureHandler)

    def handle_error(self, request, client_address):
        # BaseServer would print tracebacks containing private local media paths.
        pass


class FixtureHandler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    server_version = "EmbyMoviePartsFixture/1.0"
    sys_version = ""

    def log_message(self, format, *args):
        pass

    def do_GET(self):
        self.dispatch("GET")

    def do_HEAD(self):
        self.dispatch("HEAD")

    def do_POST(self):
        self.dispatch("POST")

    def do_DELETE(self):
        self.dispatch("DELETE")

    def reply(self, status, value=None):
        body = b"" if value is None else json.dumps(value, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        if self.command != "HEAD" and body:
            self.wfile.write(body)

    def payload(self):
        size = int(self.headers.get("Content-Length", "0"))
        if not 0 <= size <= JSON_LIMIT:
            raise ValueError("Invalid request body size")
        if not size:
            return {}
        result = json.loads(self.rfile.read(size).decode("utf-8"))
        if not isinstance(result, dict):
            raise ValueError("Invalid request body")
        return result

    def dispatch(self, method):
        try:
            self.route(method)
        except (BrokenPipeError, ConnectionResetError):
            pass
        except (ValueError, TypeError, json.JSONDecodeError, UnicodeError):
            self.close_connection = True
            self.reply(400, {"Error": "Invalid fixture request"})
        except KeyError:
            self.reply(404, {"Error": "Fixture item not found"})
        except OSError:
            self.close_connection = True
            self.reply(500, {"Error": "Fixture media unavailable"})

    def route(self, method):
        parsed = urllib.parse.urlsplit(self.path)
        path = [urllib.parse.unquote(part) for part in parsed.path.strip("/").split("/")]
        query = {key.lower(): values[-1] for key, values in urllib.parse.parse_qs(parsed.query).items()}
        state = self.server.state
        body = self.payload() if method == "POST" else {}
        if path == ["System", "Info", "Public"] and method in ("GET", "HEAD"):
            return self.reply(200, {"Id": SERVER_ID, "ServerName": "Movie Parts Fixture", "Version": "4.10.1.0"})
        if path == ["Users", "AuthenticateByName"] and method == "POST":
            if body.get("Username") != "fixture" or body.get("Pw") != "fixture":
                return self.reply(401, {"Error": "Invalid fixture login"})
            token = secrets.token_urlsafe(32)
            with state.lock:
                state.tokens[token] = uuid.uuid4().hex
            return self.reply(200, {"AccessToken": token, "ServerId": SERVER_ID,
                                    "User": {"Id": USER_ID, "Name": "fixture"}})
        auth_token = self.headers.get("X-Emby-Token", "")
        with state.lock:
            authorized = auth_token in state.tokens
        if not authorized:
            return self.reply(401, {"Error": "Fixture login required"})
        if path == ["Sessions", "Logout"] and method == "POST":
            with state.lock:
                state.tokens.pop(auth_token, None)
                state.playing = {key: entry for key, entry in state.playing.items() if entry["authToken"] != auth_token}
            return self.reply(204)
        if path == ["Fixture", "State"] and method in ("GET", "HEAD"):
            return self.reply(200, state.snapshot())
        if path == ["Fixture", "DelayPlaybackInfo"] and method == "POST":
            return self.reply(200, state.schedule_playback_info_delay(body))
        events = {("Sessions", "Playing"): "start", ("Sessions", "Playing", "Progress"): "progress",
                  ("Sessions", "Playing", "Stopped"): "stop"}
        if tuple(path) in events and method == "POST":
            state.report(auth_token, events[tuple(path)], body)
            return self.reply(204)
        if len(path) == 3 and path[0] == "Items" and path[2] == "PlaybackInfo" and method in ("GET", "POST"):
            item_id = path[1]
            if item_id not in PLAYABLE_IDS:
                raise KeyError("Unknown fixture item")
            state.delay_playback_info(item_id)
            return self.reply(200, {"MediaSources": [state.source(item_id)], "PlaySessionId": uuid.uuid4().hex})
        if len(path) >= 3 and path[0] == "Videos" and method in ("GET", "HEAD"):
            item_id = path[1]
            if item_id not in PLAYABLE_IDS:
                raise KeyError("Unknown fixture item")
            if path[2] == "AdditionalParts" and len(path) == 3:
                parts = [state.item("cd2"), state.item("cd3")] if item_id == "main" else []
                return self.reply(200, {"Items": parts, "TotalRecordCount": len(parts)})
            if path[2] in ("stream", "stream.mp4", "originalStream", "originalStream.mp4") and len(path) == 3:
                return self.stream_media()
        if len(path) >= 3 and path[:2] == ["Users", USER_ID]:
            rest = path[2:]
            if rest == ["Views"] and method in ("GET", "HEAD"):
                return self.reply(200, {"Items": [state.item("movies")], "TotalRecordCount": 1})
            if rest == ["Items", "Latest"] and method in ("GET", "HEAD"):
                return self.reply(200, [state.item("ordinary"), state.item("main")])
            if rest == ["Items", "Resume"] and method in ("GET", "HEAD"):
                resume = [state.item(item_id) for item_id in PLAYABLE_IDS]
                resume = [item for item in resume if 0 < item["UserData"].get("PlaybackPositionTicks", 0) < state.duration_ticks
                          and not item["UserData"].get("Played", False)]
                return self.reply(200, {"Items": resume, "TotalRecordCount": len(resume)})
            if rest == ["Items"] and method in ("GET", "HEAD"):
                return self.reply(200, state.library_items(query))
            if len(rest) == 2 and rest[0] == "Items" and method in ("GET", "HEAD"):
                return self.reply(200, state.item(rest[1]))
            if len(rest) == 3 and rest[0] == "Items" and rest[1] in PLAYABLE_IDS:
                item_id, endpoint = rest[1:]
                if endpoint == "SpecialFeatures" and method in ("GET", "HEAD"):
                    return self.reply(200, [state.item("pv")] if item_id == "main" else [])
                if endpoint == "LocalTrailers" and method in ("GET", "HEAD"):
                    return self.reply(200, [state.item("trailer")] if item_id == "main" else [])
                if endpoint == "UserData" and method in ("GET", "HEAD", "POST"):
                    data = state.set_user_data(item_id, body) if method == "POST" else state.item(item_id)["UserData"]
                    return self.reply(200, data)
            if len(rest) == 2 and rest[0] == "PlayedItems" and rest[1] in PLAYABLE_IDS and method in ("POST", "DELETE"):
                return self.reply(200, state.mark_played(rest[1], method == "POST"))
        self.reply(404, {"Error": "Fixture route not found"})

    def stream_media(self):
        state = self.server.state
        try:
            start, end, partial = byte_range(self.headers.get("Range"), state.size)
        except ValueError:
            self.send_response(416)
            self.send_header("Content-Range", "bytes */" + str(state.size))
            self.send_header("Content-Length", "0")
            self.end_headers()
            return
        with state.media.open("rb") as media:
            self.send_response(206 if partial else 200)
            self.send_header("Content-Type", "video/mp4")
            self.send_header("Accept-Ranges", "bytes")
            self.send_header("Content-Length", str(end - start + 1))
            if partial:
                self.send_header("Content-Range", "bytes " + str(start) + "-" + str(end) + "/" + str(state.size))
            self.end_headers()
            if self.command == "HEAD":
                return
            media.seek(start)
            remaining = end - start + 1
            while remaining:
                block = media.read(min(64 * 1024, remaining))
                if not block:
                    self.close_connection = True
                    break
                self.wfile.write(block)
                remaining -= len(block)


def main():
    parser = argparse.ArgumentParser(description="Serve a local Emby CD1/CD2/CD3 and extras fixture")
    parser.add_argument("--media", required=True, help="Existing short MP4; never copied or modified")
    parser.add_argument("--duration-ms", type=int, default=12000, help="Actual MP4 duration in milliseconds (default: 12000)")
    parser.add_argument("--port", type=int, default=18096, help="Loopback listen port (default: 18096)")
    args = parser.parse_args()
    if not 1 <= args.port <= 65535 or not 1 <= args.duration_ms <= 24 * 60 * 60 * 1000:
        parser.error("Port or duration is outside the supported range")
    try:
        media = Path(args.media).resolve(strict=True)
        if not media.is_file() or media.suffix.lower() != ".mp4" or media.stat().st_size <= 0:
            parser.error("An existing nonempty MP4 is required")
        state = FixtureState(media, args.duration_ms)
        server = FixtureServer(("127.0.0.1", args.port), state)
    except OSError:
        parser.error("Could not read the fixture or bind the loopback port")
    print("Listening on http://127.0.0.1:" + str(args.port) + " (media bytes: " + str(state.size) + ")", flush=True)
    try:
        server.serve_forever(poll_interval=0.25)
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
