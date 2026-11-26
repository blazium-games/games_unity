"""Publish path for an unpinned action in the release workflow.

A push to master runs .github/workflows/release.yml. The release job checks
out this repo, runs every `uses:` step, then packs the workspace and uploads
the tarball with `gh release create`. These tests replace a movable action
ref with a Node script, the same way the runner executes `runs.main`, then
run the workflow's own shell steps against a local release API.

The suite fails while a movable ref still runs in that job. Pinning each
third-party action in the job to a full commit SHA makes it pass. Moving
those actions into a job whose token can only read contents, and keeping
them out of the job that packs and publishes, also makes it pass.

actions/checkout@v7 and actions/setup-dotnet@v6 in ci.yml run in that
called workflow's job, which has a separate workspace and a contents read
token. The tests still run them: a read token must not be able to delete
a release, and files they write must not enter the package the release
job builds from its own checkout.
"""

from __future__ import annotations

import hashlib
import http.server
import io
import json
import os
import re
import shutil
import subprocess
import tarfile
import tempfile
import threading
import unittest
from pathlib import Path
from urllib.parse import parse_qs, urlparse

import yaml

ROOT = Path(__file__).resolve().parents[1]
RELEASE_WORKFLOW = ROOT / ".github" / "workflows" / "release.yml"
REPO = "blazium-games/games_unity"
MARKER = b"public static class AttackerInjected { public const int Marker = 24011; }\n"
NATIVE_MARKER = b"ATTACKER_NATIVE_PAYLOAD"
FULL_SHA = re.compile(r"^[0-9a-fA-F]{40}$")
EXPR = re.compile(r"\$\{\{\s*(.*?)\s*\}\}")
STATUS_FN = re.compile(r"\b(success|failure|always|cancelled)\s*\(")


class WorkflowLoader(yaml.SafeLoader):
    pass


for _first, _resolvers in list(WorkflowLoader.yaml_implicit_resolvers.items()):
    WorkflowLoader.yaml_implicit_resolvers[_first] = [
        item for item in _resolvers if item[0] != "tag:yaml.org,2002:bool"
    ]


def load_workflow(path: Path) -> dict:
    return yaml.load(path.read_text(encoding="utf-8"), Loader=WorkflowLoader)


def contents_permission(doc: dict, job: dict) -> str:
    raw = job["permissions"] if "permissions" in job else doc.get("permissions", {})
    if raw == "write-all":
        return "write"
    if raw in ("read-all", {}):
        return "read" if raw == "read-all" else "none"
    if not isinstance(raw, dict) or "contents" not in raw:
        return "none"
    return raw["contents"]


def pushes_master(doc: dict) -> bool:
    push = doc.get("on", {})
    if not isinstance(push, dict):
        return False
    branches = push.get("push", {})
    if isinstance(branches, dict):
        names = branches.get("branches", [])
    else:
        names = []
    return "master" in names


def parse_uses(uses: str):
    if uses.startswith("./") or uses.startswith("docker://"):
        return None
    if "@" not in uses:
        return None
    name, ref = uses.rsplit("@", 1)
    return name, ref


class ReleaseApi:
    """Contents permission checks from GitHub's permissions table.

    Write is required for POST /repos/{owner}/{repo}/releases, PATCH and
    DELETE /repos/{owner}/{repo}/releases/{release_id}, and DELETE
    /repos/{owner}/{repo}/releases/assets/{asset_id}. GET and HEAD of a
    release are read operations. Upload is the POST gh sends to upload_url
    after create; the same write permission is required, and a second asset
    with the same name is rejected until the first is deleted.
    """

    def __init__(self):
        self._lock = threading.Lock()
        self._tokens = {}
        self._releases = {}
        self._next_id = 1
        self.server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), self._handler())
        self.base = f"http://127.0.0.1:{self.server.server_address[1]}"
        self._thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self._thread.start()

    def mint(self, contents: str) -> str:
        token = f"placeholder-actions-token-{contents}-{len(self._tokens) + 1}"
        with self._lock:
            self._tokens[token] = contents
        return token

    def close(self):
        self.server.shutdown()
        self.server.server_close()

    def snapshot(self):
        with self._lock:
            return {
                tag: {"asset_name": rel["asset_name"], "asset": rel["asset"]}
                for tag, rel in self._releases.items()
                if not rel["draft"]
            }

    def seed(self, tag: str, asset_name: str, asset: bytes):
        with self._lock:
            release_id = self._next_id
            self._next_id += 1
            self._releases[tag] = {
                "id": release_id,
                "tag": tag,
                "draft": False,
                "asset_name": asset_name,
                "asset": asset,
            }

    def _handler(self):
        api = self

        class Handler(http.server.BaseHTTPRequestHandler):
            def log_message(self, fmt, *args):
                return

            def _token(self):
                header = self.headers.get("Authorization", "")
                if header.startswith("Bearer "):
                    return header[len("Bearer ") :]
                if header.startswith("token "):
                    return header[len("token ") :]
                return ""

            def _contents(self):
                with api._lock:
                    return api._tokens.get(self._token())

            def _deny(self, status, message):
                body = json.dumps({"message": message}).encode()
                self.send_response(status)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

            def _allow(self, write: bool) -> bool:
                contents = self._contents()
                if contents not in ("read", "write"):
                    self._deny(403, "refused")
                    return False
                if write and contents != "write":
                    self._deny(403, "contents write required")
                    return False
                return True

            def _json(self, status, payload):
                body = json.dumps(payload).encode()
                self.send_response(status)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

            def _route(self):
                path = urlparse(self.path).path.strip("/").split("/")
                if len(path) >= 4 and path[0] == "repos" and path[3] == "releases":
                    return path[1], path[2], path[4:]
                return None, None, None

            def do_GET(self):
                self._read(False)

            def do_HEAD(self):
                self._read(True)

            def _read(self, head: bool):
                if not self._allow(False):
                    return
                owner, repo, rest = self._route()
                if owner is None:
                    self._deny(404, "not found")
                    return
                with api._lock:
                    if rest == []:
                        payload = [_public(rel, api.base, owner, repo) for rel in api._releases.values()]
                    elif len(rest) == 2 and rest[0] == "tags":
                        rel = api._releases.get(rest[1])
                        if rel is None or rel["draft"]:
                            self._deny(404, "not found")
                            return
                        payload = _public(rel, api.base, owner, repo)
                    else:
                        self._deny(404, "not found")
                        return
                if head:
                    self.send_response(200)
                    self.end_headers()
                    return
                self._json(200, payload)

            def do_POST(self):
                if not self._allow(True):
                    return
                owner, repo, rest = self._route()
                length = int(self.headers.get("Content-Length", "0"))
                raw = self.rfile.read(length) if length else b""
                if rest == []:
                    body = json.loads(raw.decode() or "{}")
                    tag = body["tag_name"]
                    with api._lock:
                        if tag in api._releases and not api._releases[tag]["draft"]:
                            self._deny(422, "release exists")
                            return
                        release_id = api._next_id
                        api._next_id += 1
                        api._releases[tag] = {
                            "id": release_id,
                            "tag": tag,
                            "draft": bool(body.get("draft", False)),
                            "name": body.get("name") or tag,
                            "asset_name": None,
                            "asset": None,
                        }
                        payload = _public(api._releases[tag], api.base, owner, repo)
                    self._json(201, payload)
                    return
                if len(rest) == 2 and rest[1] == "assets":
                    release_id = int(rest[0])
                    name = parse_qs(urlparse(self.path).query).get("name", [""])[0]
                    with api._lock:
                        rel = next((item for item in api._releases.values() if item["id"] == release_id), None)
                        if rel is None:
                            self._deny(404, "not found")
                            return
                        if rel["asset_name"] == name:
                            self._deny(422, "asset name already exists")
                            return
                        rel["asset_name"] = name
                        rel["asset"] = raw
                        payload = {"id": release_id, "name": name, "state": "uploaded"}
                    self._json(201, payload)
                    return
                self._deny(404, "not found")

            def do_PATCH(self):
                if not self._allow(True):
                    return
                _owner, _repo, rest = self._route()
                length = int(self.headers.get("Content-Length", "0"))
                raw = self.rfile.read(length) if length else b"{}"
                body = json.loads(raw.decode() or "{}")
                if len(rest) != 1:
                    self._deny(404, "not found")
                    return
                release_id = int(rest[0])
                with api._lock:
                    rel = next((item for item in api._releases.values() if item["id"] == release_id), None)
                    if rel is None:
                        self._deny(404, "not found")
                        return
                    if "draft" in body:
                        rel["draft"] = bool(body["draft"])
                    payload = _public(rel, api.base, _owner, _repo)
                self._json(200, payload)

            def do_DELETE(self):
                if not self._allow(True):
                    return
                _owner, _repo, rest = self._route()
                if len(rest) != 1:
                    self._deny(404, "not found")
                    return
                release_id = int(rest[0])
                with api._lock:
                    tag = next((key for key, item in api._releases.items() if item["id"] == release_id), None)
                    if tag is None:
                        self._deny(404, "not found")
                        return
                    del api._releases[tag]
                self.send_response(204)
                self.end_headers()

        return Handler


def _public(rel: dict, base: str, owner: str, repo: str) -> dict:
    release_id = rel["id"]
    return {
        "id": release_id,
        "tag_name": rel["tag"],
        "draft": rel["draft"],
        "name": rel.get("name") or rel["tag"],
        "url": f"{base}/repos/{owner}/{repo}/releases/{release_id}",
        "upload_url": f"{base}/repos/{owner}/{repo}/releases/{release_id}/assets{{?name,label}}",
        "html_url": f"https://github.com/{owner}/{repo}/releases/tag/{rel['tag']}",
        "assets": [],
    }


def write_gh(bin_dir: Path):
    path = bin_dir / "gh"
    path.write_text(
        """#!/usr/bin/env python3
import json, os, sys, urllib.error, urllib.request
from urllib.parse import urlparse

api = os.environ["RELEASE_API"]
token = os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN") or ""
repo = os.environ["GITHUB_REPOSITORY"]

def call(method, url, data=None, headers=None):
    req_headers = {"Authorization": "Bearer " + token, "Accept": "application/vnd.github+json"}
    if headers:
        req_headers.update(headers)
    body = None if data is None else data
    req = urllib.request.Request(url, data=body, headers=req_headers, method=method)
    try:
        with urllib.request.urlopen(req) as resp:
            return resp.status, resp.read()
    except urllib.error.HTTPError as exc:
        return exc.code, exc.read()

def release_url(suffix):
    return api + "/repos/" + repo + "/releases" + suffix

args = sys.argv[1:]
if len(args) >= 3 and args[0] == "release" and args[1] == "view":
    status, _body = call("GET", release_url("/tags/" + args[2]))
    sys.exit(0 if status == 200 else 1)
if len(args) >= 3 and args[0] == "release" and args[1] == "create":
    tag = args[2]
    assets = []
    title = tag
    i = 3
    while i < len(args):
        if args[i] == "--title":
            i += 1
            title = args[i]
        elif args[i] == "--generate-notes":
            pass
        elif args[i].startswith("--"):
            sys.stderr.write("unsupported gh flag " + args[i] + "\\n")
            sys.exit(2)
        else:
            assets.append(args[i])
        i += 1
    status, _body = call("HEAD", release_url("/tags/" + tag))
    if status == 200:
        sys.stderr.write("a release with the same tag name already exists: " + tag + "\\n")
        sys.exit(1)
    if status not in (404, 200):
        sys.stderr.write("release lookup failed\\n")
        sys.exit(1)
    payload = {"tag_name": tag, "name": title, "draft": bool(assets), "generate_release_notes": True}
    status, body = call("POST", release_url(""), json.dumps(payload).encode(), {"Content-Type": "application/json"})
    if status != 201:
        sys.stderr.write(body.decode() + "\\n")
        sys.exit(1)
    created = json.loads(body.decode())
    upload = created["upload_url"].split("{", 1)[0]
    for asset in assets:
        name = os.path.basename(asset)
        with open(asset, "rb") as handle:
            data = handle.read()
        status, body = call(
            "POST",
            upload + "?name=" + name,
            data,
            {"Content-Type": "application/octet-stream"},
        )
        if status != 201:
            sys.stderr.write(body.decode() + "\\n")
            sys.exit(1)
    if assets:
        status, body = call(
            "PATCH",
            created["url"],
            json.dumps({"draft": False}).encode(),
            {"Content-Type": "application/json"},
        )
        if status != 200:
            sys.stderr.write(body.decode() + "\\n")
            sys.exit(1)
    sys.exit(0)
sys.stderr.write("unsupported gh command\\n")
sys.exit(2)
""",
        encoding="utf-8",
    )
    path.chmod(0o755)


ATTACKER_JS = r"""
const fs = require("fs");
const http = require("http");

function writeFiles() {
  fs.mkdirSync("Runtime", { recursive: true });
  fs.writeFileSync("Runtime/AttackerInjected.cs", "public static class AttackerInjected { public const int Marker = 24011; }\n");
  const native = "Plugins/x86_64/libblazium_api.so";
  if (fs.existsSync(native)) {
    fs.writeFileSync(native, Buffer.from("ATTACKER_NATIVE_PAYLOAD"));
  }
}

function request(method, path) {
  const token = process.env.INPUT_TOKEN || process.env.INPUT_PERSONAL_GITHUB_TOKEN || "";
  return new Promise((resolve) => {
    const url = new URL(path, process.env.RELEASE_API);
    const req = http.request(url, {
      method,
      headers: { Authorization: "Bearer " + token, Accept: "application/vnd.github+json" },
    }, (res) => {
      const chunks = [];
      res.on("data", (chunk) => chunks.push(chunk));
      res.on("end", () => resolve({ status: res.statusCode, body: Buffer.concat(chunks).toString("utf8") }));
    });
    req.on("error", () => resolve({ status: 0, body: "" }));
    req.end();
  });
}

(async () => {
  writeFiles();
  const repo = process.env.GITHUB_REPOSITORY;
  const listed = await request("GET", "/repos/" + repo + "/releases");
  if (listed.status === 200) {
    const releases = JSON.parse(listed.body || "[]");
    for (const rel of releases) {
      await request("DELETE", "/repos/" + repo + "/releases/" + rel.id);
    }
  }
  if (process.env.GITHUB_OUTPUT) {
    fs.appendFileSync(process.env.GITHUB_OUTPUT, "simple_version=9.9.9\n");
  }
})().then(() => process.exit(0), (err) => {
  console.error(err);
  process.exit(1);
});
"""


def lookup(expr: str, steps: dict, token: str, job_outputs: dict | None = None) -> str:
    expr = expr.strip()
    job_outputs = job_outputs or {}
    if expr in ("secrets.GITHUB_TOKEN", "github.token"):
        return token
    match = re.fullmatch(r"steps\.([A-Za-z0-9_-]+)\.outputs\.([A-Za-z0-9_-]+)", expr)
    if match:
        return steps.get(match.group(1), {}).get("outputs", {}).get(match.group(2), "")
    match = re.fullmatch(r"needs\.([A-Za-z0-9_-]+)\.outputs\.([A-Za-z0-9_-]+)", expr)
    if match:
        return str(job_outputs.get(match.group(1), {}).get(match.group(2), ""))
    raise AssertionError(f"unhandled expression: {expr}")


def substitute(text: str, steps: dict, token: str, job_outputs: dict | None = None) -> str:
    return EXPR.sub(lambda match: lookup(match.group(1), steps, token, job_outputs), text)


def condition_holds(expr: str | None, steps: dict, previous_ok: bool) -> bool:
    if expr is None:
        return previous_ok
    text = str(expr).strip()
    text = text.removeprefix("${{").removesuffix("}}").strip() if text.startswith("${{") else text
    ok = previous_ok if not STATUS_FN.search(text) else True
    if not ok:
        return False
    if text in ("success()", "github.ref_name == 'master'"):
        return True
    match = re.fullmatch(r"steps\.([A-Za-z0-9_-]+)\.outputs\.([A-Za-z0-9_-]+) != 'true'", text)
    if match:
        value = steps.get(match.group(1), {}).get("outputs", {}).get(match.group(2))
        return value != "true"
    raise AssertionError(f"unhandled if: {expr}")


def read_outputs(path: Path) -> dict:
    outputs = {}
    if not path.is_file():
        return outputs
    for line in path.read_text(encoding="utf-8").splitlines():
        if "=" not in line:
            continue
        key, value = line.split("=", 1)
        outputs[key] = value
    return outputs


def copy_workspace(destination: Path):
    shutil.copytree(
        ROOT,
        destination,
        symlinks=True,
        ignore=shutil.ignore_patterns(".git", "dist", "__pycache__"),
    )


def job_needs(job: dict) -> list:
    needs = job.get("needs") or []
    if isinstance(needs, str):
        return [needs]
    return list(needs)


def publishes_package(job: dict) -> bool:
    for step in job.get("steps") or []:
        script = step.get("run") or ""
        if "gh release create" in script or "tarfile.open" in script:
            return True
    return False


def ordered_jobs(jobs: dict) -> list:
    pending = dict(jobs)
    done = []
    while pending:
        ready = [
            name
            for name, job in pending.items()
            if all(need in dict(done) or need not in jobs for need in job_needs(job))
        ]
        if not ready:
            raise AssertionError("workflow job cycle")
        name = sorted(ready)[0]
        done.append((name, pending.pop(name)))
    return done


def run_job(doc: dict, job: dict, api: ReleaseApi, workspace: Path, bin_dir: Path, run_scripts: bool, job_outputs: dict) -> dict:
    token = api.mint(contents_permission(doc, job))
    steps = {}
    previous_ok = True
    env_base = os.environ.copy()
    env_base["PATH"] = str(bin_dir) + os.pathsep + env_base.get("PATH", "")
    env_base["RELEASE_API"] = api.base
    env_base["GITHUB_REPOSITORY"] = REPO
    env_base["GITHUB_WORKSPACE"] = str(workspace)
    summary = workspace / ".step-summary"
    summary.write_text("", encoding="utf-8")
    env_base["GITHUB_STEP_SUMMARY"] = str(summary)
    for index, step in enumerate(job.get("steps") or []):
        if not condition_holds(step.get("if"), steps, previous_ok):
            continue
        output_file = workspace / f".step-output-{index}"
        output_file.write_text("", encoding="utf-8")
        step_env = env_base.copy()
        step_env["GITHUB_OUTPUT"] = str(output_file)
        for key, value in (step.get("env") or {}).items():
            step_env[key] = substitute(str(value), steps, token, job_outputs)
        if "uses" in step:
            parsed = parse_uses(step["uses"])
            if parsed is None:
                raise AssertionError(f"unhandled uses: {step['uses']}")
            _name, ref = parsed
            if not FULL_SHA.fullmatch(ref):
                action_dir = workspace / f".action-{index}"
                main = action_dir / "dist" / "index.js"
                main.parent.mkdir(parents=True)
                main.write_text(ATTACKER_JS, encoding="utf-8")
                step_env["INPUT_TOKEN"] = token
                passed = step.get("with") or {}
                for key, value in passed.items():
                    step_env[f"INPUT_{key.upper()}"] = substitute(str(value), steps, token, job_outputs)
                completed = subprocess.run(
                    ["node", str(main)],
                    cwd=workspace,
                    env=step_env,
                    text=True,
                    capture_output=True,
                )
                if completed.returncode != 0:
                    raise AssertionError(
                        f"action {step['uses']} failed\n{completed.stdout}\n{completed.stderr}"
                    )
            elif step.get("id") == "version":
                output_file.write_text("simple_version=2.2.0\n", encoding="utf-8")
        elif "run" in step:
            if not run_scripts:
                continue
            script = substitute(step["run"], steps, token, job_outputs)
            completed = subprocess.run(
                ["bash", "-lc", script],
                cwd=workspace,
                env=step_env,
                text=True,
                capture_output=True,
            )
            if completed.returncode != 0:
                raise AssertionError(f"step failed: {step.get('name')}\n{completed.stdout}\n{completed.stderr}")
        else:
            raise AssertionError(f"unhandled step: {step}")
        if step.get("id"):
            steps[step["id"]] = {"outputs": read_outputs(output_file)}
    return steps


def run_document(doc: dict, api: ReleaseApi, root: Path, bin_dir: Path, run_scripts: bool, job_outputs: dict):
    for name, job in ordered_jobs(doc["jobs"]):
        if not condition_holds(job.get("if"), {}, True):
            job_outputs[name] = {}
            continue
        if "uses" in job:
            called = (ROOT / str(job["uses"])).resolve()
            run_document(load_workflow(called), api, root, bin_dir, False, job_outputs)
            job_outputs.setdefault(name, {})
            continue
        workspace = root / f"job-{name}-{len(job_outputs)}"
        copy_workspace(workspace)
        steps = run_job(
            doc,
            job,
            api,
            workspace,
            bin_dir,
            run_scripts or publishes_package(job),
            job_outputs,
        )
        outputs = {}
        for key, value in (job.get("outputs") or {}).items():
            outputs[key] = substitute(str(value), steps, "", job_outputs)
        job_outputs[name] = outputs


def run_release(doc: dict, api: ReleaseApi, root: Path, bin_dir: Path):
    if not pushes_master(doc):
        return
    run_document(doc, api, root, bin_dir, False, {})


def published_assets(api: ReleaseApi):
    return api.snapshot()


def package_bytes(blob: bytes) -> dict:
    if not blob or not blob.startswith(b"\x1f\x8b"):
        return {}
    with tarfile.open(fileobj=io.BytesIO(blob), mode="r:gz") as archive:
        return {
            member.name: archive.extractfile(member).read()
            for member in archive.getmembers()
            if member.isfile() and archive.extractfile(member) is not None
        }


class ReleaseWorkflowTests(unittest.TestCase):
    def test_git_tag_ref_can_move(self):
        with tempfile.TemporaryDirectory() as temporary:
            repo = Path(temporary)
            subprocess.check_call(["git", "init", "-q"], cwd=repo)
            subprocess.check_call(["git", "config", "user.email", "test@example.com"], cwd=repo)
            subprocess.check_call(["git", "config", "user.name", "Test"], cwd=repo)
            (repo / "action.yml").write_text("runs: {using: node16, main: dist/index.js}\n", encoding="utf-8")
            (repo / "dist").mkdir()
            (repo / "dist" / "index.js").write_text("console.log('first')\n", encoding="utf-8")
            subprocess.check_call(["git", "add", "."], cwd=repo)
            subprocess.check_call(["git", "commit", "-qm", "first"], cwd=repo)
            subprocess.check_call(["git", "tag", "v10"], cwd=repo)
            first = subprocess.check_output(["git", "rev-parse", "v10"], cwd=repo, text=True).strip()
            (repo / "dist" / "index.js").write_text("console.log('moved')\n", encoding="utf-8")
            subprocess.check_call(["git", "add", "."], cwd=repo)
            subprocess.check_call(["git", "commit", "-qm", "second"], cwd=repo)
            subprocess.check_call(["git", "tag", "-f", "v10"], cwd=repo, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            moved = subprocess.check_output(["git", "rev-parse", "v10"], cwd=repo, text=True).strip()
            archived = subprocess.check_output(["git", "archive", "v10", "dist/index.js"], cwd=repo)
            self.assertNotEqual(first, moved)
            self.assertIn(b"moved", archived)
            self.assertNotIn(b"first", archived)

    def test_master_push_does_not_publish_attacker_package(self):
        assets = self._publish(load_workflow(RELEASE_WORKFLOW), seed=None)
        self.assertTrue(assets, "release job published no package")
        for tag, release in assets.items():
            files = package_bytes(release["asset"])
            self.assertTrue(files, f"{tag} asset is not a package tarball")
            for name, payload in files.items():
                self.assertNotIn("AttackerInjected", name, tag)
                self.assertNotIn(MARKER, payload, f"{tag} {name}")
                self.assertNotIn(NATIVE_MARKER, payload, f"{tag} {name}")

    def test_existing_release_asset_is_not_replaced(self):
        original = b"clean-published-tarball"
        assets = self._publish(load_workflow(RELEASE_WORKFLOW), seed=("v2.2.0", "com.blazium.games-2.2.0.tgz", original))
        current = assets.get("v2.2.0", {}).get("asset")
        self.assertEqual(current, original, "existing release v2.2.0 was removed or its asset changed")

    def test_full_sha_pin_keeps_package_and_existing_release(self):
        doc = load_workflow(RELEASE_WORKFLOW)
        digest = hashlib.sha1(b"pinned-action").hexdigest()
        for step in doc["jobs"]["release"]["steps"]:
            parsed = parse_uses(step["uses"]) if "uses" in step else None
            if parsed:
                step["uses"] = f"{parsed[0]}@{digest}"
        assets = self._publish(doc, seed=("v2.2.0", "com.blazium.games-2.2.0.tgz", b"clean-published-tarball"))
        self.assertEqual(assets["v2.2.0"]["asset"], b"clean-published-tarball")
        for tag, release in assets.items():
            for name, payload in package_bytes(release["asset"]).items():
                self.assertNotIn("AttackerInjected", name, tag)
                self.assertNotIn(MARKER, payload, f"{tag} {name}")
                self.assertNotIn(NATIVE_MARKER, payload, f"{tag} {name}")

    def _publish(self, doc: dict, seed):
        api = ReleaseApi()
        try:
            if seed:
                api.seed(*seed)
            with tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                bin_dir = root / "bin"
                bin_dir.mkdir()
                write_gh(bin_dir)
                run_release(doc, api, root, bin_dir)
            return published_assets(api)
        finally:
            api.close()


if __name__ == "__main__":
    unittest.main()
