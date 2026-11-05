"""Minimal Partner Center (devcenter v1.0) client for District AI for Windows.

Credentials come from Secret Manager straight into memory: the project and the
secret names are the environment's (GSM_PROJECT, MSSTORE_TENANT_SECRET,
MSSTORE_CLIENT_ID_SECRET, MSSTORE_CLIENT_SECRET_SECRET; store.yml sets them),
read with GSM_ACCESS_TOKEN in Actions and through `gcloud` elsewhere. Nothing
here prints, logs or writes a secret value. The submission's SAS fileUploadUrl is a credential too: it is redacted
before anything is shown or saved.
"""
import base64
import json
import os
import subprocess
import urllib.error
import urllib.parse
import urllib.request

DEVCENTER = "https://manage.devcenter.microsoft.com"
APP_ID = "9P9F0NNFJ3CP"  # Store ID of "District AI" (docs/district_windows.md)
REDACT_KEYS = {"fileUploadUrl"}


def env(name):
    value = os.environ.get(name)
    if not value:
        raise SystemExit(f"REFUSED: {name} is not set (store.yml sets it)")
    return value


def gsm(name):
    project = env("GSM_PROJECT")
    tok = os.environ.get("GSM_ACCESS_TOKEN")
    if tok:
        req = urllib.request.Request(
            f"https://secretmanager.googleapis.com/v1/projects/{project}/secrets/{name}/versions/latest:access")
        req.add_header("Authorization", "Bearer " + tok)
        with urllib.request.urlopen(req, timeout=60) as r:
            return base64.b64decode(json.load(r)["payload"]["data"]).decode().strip()
    out = subprocess.run(
        ["gcloud", "secrets", "versions", "access", "latest", f"--secret={name}", f"--project={project}"],
        check=True, capture_output=True, text=True,
    )
    return out.stdout.strip()


def token():
    tenant = gsm(env("MSSTORE_TENANT_SECRET"))
    body = urllib.parse.urlencode({
        "grant_type": "client_credentials",
        "client_id": gsm(env("MSSTORE_CLIENT_ID_SECRET")),
        "client_secret": gsm(env("MSSTORE_CLIENT_SECRET_SECRET")),
        "scope": f"{DEVCENTER}/.default",
    }).encode()
    req = urllib.request.Request(
        f"https://login.microsoftonline.com/{urllib.parse.quote(tenant)}/oauth2/v2.0/token", data=body, method="POST")
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.load(r)["access_token"]


class DevCenter:
    def __init__(self):
        self._tok = token()

    def call(self, method, path, body=None):
        data = None if body is None else json.dumps(body).encode()
        req = urllib.request.Request(DEVCENTER + path, data=data, method=method)
        req.add_header("Authorization", "Bearer " + self._tok)
        if data is not None:
            req.add_header("Content-Type", "application/json")
        try:
            with urllib.request.urlopen(req, timeout=120) as r:
                raw = r.read()
                return r.status, (json.loads(raw) if raw else None)
        except urllib.error.HTTPError as e:
            raw = e.read()
            try:
                return e.code, json.loads(raw)
            except ValueError:
                return e.code, {"raw": raw.decode(errors="replace")[:2000]}


def redact(obj):
    if isinstance(obj, dict):
        return {k: ("<redacted SAS url>" if k in REDACT_KEYS and v else redact(v)) for k, v in obj.items()}
    if isinstance(obj, list):
        return [redact(v) for v in obj]
    return obj
