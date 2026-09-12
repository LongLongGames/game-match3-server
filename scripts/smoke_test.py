#!/usr/bin/env python3
"""
game-match3-server API smoke (happy + bad path).
Needs:
  - MP up (login for JWT), default http://localhost:11080
  - match3 compose up, gateway default http://localhost:13180 (profile G=1)

  python scripts/smoke_test.py
  set MP_URL=http://localhost:11080
  set GT_URL=http://localhost:13180
  set GAME_ID=match3
"""
from __future__ import annotations

import json
import os
import sys
import time
import urllib.error
import urllib.request
import uuid

MP_URL = os.environ.get("MP_URL", "http://localhost:11080").rstrip("/")
GT_URL = os.environ.get("GT_URL", "http://localhost:13180").rstrip("/")
GAME_ID = os.environ.get("GAME_ID", "match3")

PASS = 0
FAIL = 0
RESULTS: list[dict] = []


def req(method: str, url: str, body: dict | None = None, headers: dict | None = None):
    data = None
    hdr = dict(headers or {})
    if body is not None:
        data = json.dumps(body).encode()
        hdr.setdefault("Content-Type", "application/json")
    r = urllib.request.Request(url, data=data, headers=hdr, method=method)
    try:
        with urllib.request.urlopen(r, timeout=25) as resp:
            return resp.status, resp.read().decode()
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()
    except Exception as e:
        return 0, str(e)


def expect(
    name: str,
    method: str,
    url: str,
    expected: int,
    body=None,
    headers=None,
    allow_empty=False,
    allow_non_json=False,
):
    global PASS, FAIL
    code, raw = req(method, url, body, headers)
    ok = code == expected
    note = ""
    if ok and not allow_empty and expected not in (204,) and raw:
        if not allow_non_json:
            try:
                json.loads(raw)
            except json.JSONDecodeError:
                ok = False
                note = f" non-JSON (AOT risk?) body={raw[:120]}"
    # 500 on expected 4xx often means anonymous-type AOT crash
    if expected in (400, 401, 404) and code == 500:
        note += " [possible AOT anonymous error serialization]"
    if ok:
        print(f"  PASS  {name}  ({code})")
        PASS += 1
        RESULTS.append({"name": name, "ok": True, "code": code})
    else:
        print(f"  FAIL  {name}  expected={expected} got={code}{note} body={raw[:280]}")
        FAIL += 1
        RESULTS.append({"name": name, "ok": False, "code": code, "body": raw[:500]})
    return code, raw


def main():
    global PASS, FAIL
    print(f"=== match3 smoke GT={GT_URL} MP={MP_URL} game_id={GAME_ID} ===")
    suffix = uuid.uuid4().hex[:8]
    username = f"m3_{suffix}"
    password = "test1234"
    device_id = f"m3-device-{suffix}"

    print("--- gateway health ---")
    expect("GET /health", "GET", f"{GT_URL}/health", 200)

    print("--- MP login (JWT) ---")
    code, raw = expect(
        "MP login -> 200",
        "POST",
        f"{MP_URL}/api/v1/auth/login",
        200,
        {
            "provider": "official",
            "app_id": "test_app",
            "device_id": device_id,
            "auth_payload": {"username": username, "password": password},
        },
    )
    token = ""
    if code == 200:
        try:
            token = json.loads(raw).get("access_token") or ""
        except json.JSONDecodeError:
            pass
    if not token:
        print("  FAIL  no MP token; match3 auth tests skipped")
        FAIL += 1
        _write_report()
        sys.exit(1)

    auth = {"Authorization": f"Bearer {token}"}
    bad_auth = {"Authorization": "Bearer invalid-token"}

    print("--- user bad path ---")
    expect(
        "profile no auth -> 401",
        "GET",
        f"{GT_URL}/api/v1/user/profile?game_id={GAME_ID}",
        401,
        allow_empty=True,
    )
    expect(
        "profile bad token -> 401",
        "GET",
        f"{GT_URL}/api/v1/user/profile?game_id={GAME_ID}",
        401,
        headers=bad_auth,
        allow_empty=True,
    )
    # missing game_id on PUT — may 400 or AOT 500 if anonymous error
    expect(
        "profile put missing game_id -> 400",
        "PUT",
        f"{GT_URL}/api/v1/user/profile",
        400,
        {"nickname": "x"},
        headers=auth,
        allow_non_json=True,  # if 500, still FAIL on code
    )
    expect(
        "state no auth -> 401",
        "GET",
        f"{GT_URL}/api/v1/user/state?game_id={GAME_ID}&map_id=1",
        401,
        allow_empty=True,
    )
    expect(
        "level clear no auth -> 401",
        "POST",
        f"{GT_URL}/api/v1/user/level/clear",
        401,
        {"game_id": GAME_ID, "map_id": 1, "level_id": 1, "stars": 3, "steps": 10, "score": 100},
        allow_empty=True,
    )
    expect(
        "level clear bad stars -> 400",
        "POST",
        f"{GT_URL}/api/v1/user/level/clear",
        400,
        {"game_id": GAME_ID, "map_id": 1, "level_id": 1, "stars": 9, "steps": 10, "score": 100},
        headers=auth,
        allow_non_json=True,
    )

    print("--- user happy ---")
    expect(
        "get profile -> 200",
        "GET",
        f"{GT_URL}/api/v1/user/profile?game_id={GAME_ID}",
        200,
        headers=auth,
    )
    expect(
        "put profile -> 200",
        "PUT",
        f"{GT_URL}/api/v1/user/profile",
        200,
        {"game_id": GAME_ID, "nickname": f"Smoke_{suffix}"},
        headers=auth,
    )
    expect(
        "get state -> 200",
        "GET",
        f"{GT_URL}/api/v1/user/state?game_id={GAME_ID}&map_id=1",
        200,
        headers=auth,
    )
    # refill energy if endpoint exists (dev cheat)
    code_e, _ = req(
        "POST",
        f"{GT_URL}/api/v1/user/energy/cheat-refill",
        {"game_id": GAME_ID},
        auth,
    )
    if code_e in (200, 204):
        print(f"  PASS  energy cheat-refill  ({code_e})")
        PASS += 1
    elif code_e == 404:
        print("  SKIP  energy cheat-refill not found (ok)")
    else:
        # non-fatal for smoke if clear still works
        print(f"  INFO  energy cheat-refill got {code_e}")

    code_c, raw_c = expect(
        "level clear map1-lv1 -> 200",
        "POST",
        f"{GT_URL}/api/v1/user/level/clear",
        200,
        {
            "game_id": GAME_ID,
            "map_id": 1,
            "level_id": 1,
            "stars": 3,
            "steps": 12,
            "score": 3500,
        },
        headers=auth,
    )
    if code_c != 200:
        print("  INFO  clear failed — check config Level.bytes / energy / previous level rules")

    print("--- leaderboard ---")
    expect(
        "score no auth -> 401",
        "POST",
        f"{GT_URL}/api/v1/leaderboard/score",
        401,
        {"game_id": GAME_ID, "board_id": "default", "score": 100, "nickname": "smoke"},
        allow_empty=True,
    )
    expect(
        "score missing game_id -> 400",
        "POST",
        f"{GT_URL}/api/v1/leaderboard/score",
        400,
        {"score": 1, "nickname": "x"},
        headers=auth,
        allow_non_json=True,
    )
    expect(
        "submit score -> 200",
        "POST",
        f"{GT_URL}/api/v1/leaderboard/score",
        200,
        {"game_id": GAME_ID, "board_id": "default", "score": 100 + int(time.time()) % 1000, "nickname": f"smoke_{suffix}"},
        headers=auth,
    )
    expect(
        "top -> 200",
        "GET",
        f"{GT_URL}/api/v1/leaderboard/top?game_id={GAME_ID}&limit=5",
        200,
    )
    expect(
        "top missing game_id -> 400",
        "GET",
        f"{GT_URL}/api/v1/leaderboard/top",
        400,
        allow_non_json=True,
    )
    expect(
        "me rank -> 200",
        "GET",
        f"{GT_URL}/api/v1/leaderboard/me?game_id={GAME_ID}",
        200,
        headers=auth,
    )

    print("--- game core ---")
    expect(
        "game status no auth -> 401",
        "GET",
        f"{GT_URL}/api/v1/game/status",
        401,
        allow_empty=True,
    )
    expect(
        "game status with token -> 200",
        "GET",
        f"{GT_URL}/api/v1/game/status",
        200,
        headers=auth,
    )
    expect(
        "version-check missing params -> 400",
        "GET",
        f"{GT_URL}/api/v1/game/version-check",
        400,
        allow_non_json=True,
    )
    # 404 if no row in client_version_config — still valid API behavior
    code_v, raw_v = req(
        "GET",
        f"{GT_URL}/api/v1/game/version-check?game_id={GAME_ID}&channel=official&platform=android&region=cn&client_version_code=10000",
    )
    if code_v in (200, 404):
        print(f"  PASS  version-check -> {code_v}")
        PASS += 1
        RESULTS.append({"name": "version-check", "ok": True, "code": code_v})
    else:
        print(f"  FAIL  version-check expected 200/404 got={code_v} body={raw_v[:200]}")
        FAIL += 1

    print(f"=== summary: PASS={PASS} FAIL={FAIL} ===")
    _write_report()
    sys.exit(1 if FAIL else 0)


def _write_report():
    out_dir = os.path.join(os.path.dirname(__file__), "..", "test-results")
    os.makedirs(out_dir, exist_ok=True)
    report = {
        "suite": "game-match3-server smoke",
        "gtUrl": GT_URL,
        "mpUrl": MP_URL,
        "gameId": GAME_ID,
        "timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "pass": PASS,
        "fail": FAIL,
        "results": RESULTS,
        "python": sys.version.split()[0],
        "platform": sys.platform,
    }
    jp = os.path.join(out_dir, "smoke-report.json")
    with open(jp, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2, ensure_ascii=False)
    with open(os.path.join(out_dir, "smoke-junit.xml"), "w", encoding="utf-8") as f:
        f.write(
            f"""<?xml version="1.0" encoding="UTF-8"?>
<testsuite name="Match3.Smoke" tests="{PASS + FAIL}" failures="{FAIL}" timestamp="{report['timestamp']}">
  <testcase classname="Match3.Smoke" name="all">
    {"" if FAIL == 0 else '<failure message="one or more checks failed"/>'}
  </testcase>
</testsuite>
"""
        )
    print(f"Wrote {jp}")


if __name__ == "__main__":
    main()
