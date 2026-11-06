#!/usr/bin/env python3
"""Prepare the Microsoft Store submission of District AI for Windows (Store ID 9P9F0NNFJ3CP).

Path: the Microsoft Store submission API (manage.devcenter.microsoft.com v1.0), the same
endpoints the msstore CLI's packaged-app commands call (StorePackagedAPI.cs, msstore-cli
v0.4.3). The CLI itself is not used because on Linux `msstore reconfigure` stores the
client secret in the libsecret keyring; this script keeps it in memory only.

What a live run does, in order:
  1. GET the application; refuse if a submission is already pending.
  2. POST a new submission (Microsoft copies the last published one).
  3. Edit it: each existing package PendingDelete, the new .msixupload PendingUpload;
     en-us "What's new" (releaseNotes) from docs/store-listing.md; a fr-ca listing with
     the fr-CA "What's new" from the same file plus any fr-CA text and screenshots given.
     When the copied submission already has fr-ca (every release after 3.0.0), only its
     releaseNotes are replaced, and any fr-CA listing input is refused.
     --version and --ref are required (there is no default release).
  4. PUT the edited submission.
  5. Upload one ZIP (packages at the root, fr-CA screenshots under fr-ca/) to the
     submission's SAS fileUploadUrl as a block blob.
  6. Only with --submit: POST .../commit and read the status back. Without it the
     submission stays a draft (status PendingCommit) for review in Partner Center.

--dry-run makes read-only GETs only (the application and its last published submission,
which is what step 2 would copy), applies the same edits and prints every request it
would send. No secret is printed; the SAS fileUploadUrl is redacted.

Microsoft: "If you use Partner Center to change a submission that you originally created
by using the API, you will no longer be able to change or commit that submission by using
the API." (learn.microsoft.com/en-us/windows/uwp/monetize/manage-app-submissions)
"""
import argparse
import base64
import contextlib
import copy
import hashlib
import http.client
import io
import json
import os
import re
import struct
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import storeapi  # noqa: E402

REPO = "distronode-corporation/district-windows"
IDENTITY_NAME = "DistronodeCorporation.42101E4C5A5B6"
PUBLISHER = "CN=88F0B47E-0C40-4DCA-A217-DF66120AACBE"
NBSP = chr(0xA0)
BLOCK = 4 * 1024 * 1024  # valid block size under every Blob service version

# Statuses after a successful commit (the submission API's status values).
COMMITTED = {"PreProcessing", "Certification", "Release", "PendingPublication", "Publishing", "Published"}
# Terminal failures before or at certification: the submission stays pending until it is
# deleted, so this run deletes the draft it made and fails.
FAILED = {"CommitFailed", "PreProcessingFailed", "CertificationFailed", "ReleaseFailed"}
# The API's other terminal statuses. PublishFailed comes after the build passed
# certification (publishing can be retried in Partner Center), and Canceled is no longer
# pending: neither is deleted, and the run fails naming it.
KEPT_TERMINAL = {"PublishFailed", "Canceled"}
LIMITS = {"description": 10000, "releaseNotes": 1500, "shortDescription": 1000, "title": 256}
# What a call can raise when its answer never arrives whole: URLError and timeouts are
# OSError, a truncated body is http.client.HTTPException, a non-JSON 2xx body is ValueError.
TRANSPORT = (OSError, http.client.HTTPException, ValueError)


def vtuple(v):
    return tuple(int(x) for x in v.split("."))


def live_versions(sub):
    return [p["version"] for p in sub.get("applicationPackages") or []
            if p.get("fileStatus") == "Uploaded" and p.get("version")]


def refuse_not_newer(published, expect_version):
    """Only a version strictly above everything the Store serves: a re-run after the
    release went live, or a dispatch of an older release, would re-certify the same build
    or roll new installs back (the Store accepts packages in any order)."""
    live = live_versions(published)
    if not live:
        die("the published submission names no package version; cannot tell a re-run from a release")
    top = max(live, key=vtuple)
    if vtuple(top) >= vtuple(expect_version):
        die(f"the Store already serves {top}; {expect_version} would re-submit it or roll it back. Nothing was created.")


def die(msg):
    print("REFUSED: " + msg, file=sys.stderr)
    sys.exit(2)


# ---------------------------------------------------------------- listing text

def load_listing_md(args):
    if args.listing_md:
        with open(args.listing_md, encoding="utf-8") as f:
            return f.read(), "file " + args.listing_md
    out = subprocess.run(
        ["gh", "api", f"repos/{REPO}/contents/docs/store-listing.md?ref={args.ref}",
         "-H", "Accept: application/vnd.github.raw"],
        check=True, capture_output=True, text=True)
    return out.stdout, f"{REPO}@{args.ref}:docs/store-listing.md"


def whats_new(md, version):
    """The en-US and fr-CA ```text blocks under '## What's new in this version: <version>'."""
    head = f"## What's new in this version: {version}"
    # Anchored to the whole heading line: 3.1.1 never takes the 3.1.10 section.
    m = re.search(r"^" + re.escape(head) + r"[ \t]*$", md, re.M)
    if not m:
        die(f"docs/store-listing.md has no section '{head}'")
    sect = md[m.end():]
    sect = re.split(r"\n## ", sect, maxsplit=1)[0]
    out = {}
    for lang in ("en-US", "fr-CA"):
        m = re.search(r"^" + re.escape(lang) + r":\s*\n+```text\n(.*?)\n```", sect, re.S | re.M)
        if not m:
            die(f"no {lang} ```text block in '{head}'")
        out[lang] = m.group(1)
    return out


def load_rows(path, en):
    """fr-CA listing fields from a reviewed jsonl (rows windows.store.*), `fr` taken verbatim.

    Each row's `en` must equal the live en-us field it translates (releaseNotes excepted:
    the live en-us one is the previous version's), so a stale translation is refused.
    """
    raw = {}
    with open(path, encoding="utf-8") as f:
        for line in f:
            o = json.loads(line)
            if o.get("id", "").startswith("windows.store."):
                if o.get("state") != "reviewed":
                    die(f"{o['id']} is in state {o.get('state')}, not reviewed")
                raw[o["id"][len("windows.store."):]] = o
    out = {}
    for k in ("title", "description", "shortDescription", "releaseNotes", "devStudio", "copyrightAndTrademarkInfo"):
        if k in raw:
            if k != "releaseNotes" and raw[k]["en"] != en.get(k):
                die(f"windows.store.{k}: its en no longer equals the live en-us {k}")
            out[k] = raw[k]["fr"]
    for k in ("features", "keywords"):
        n = sorted(int(x.split(".")[1]) for x in raw if x.startswith(k + "."))
        if n:
            if n != list(range(1, len(n) + 1)):
                die(f"windows.store.{k}.N rows are not 1..{len(n)}: {n}")
            if [raw[f"{k}.{i}"]["en"] for i in n] != en.get(k):
                die(f"windows.store.{k}.N: their en no longer equals the live en-us {k}")
            out[k] = [raw[f"{k}.{i}"]["fr"] for i in n]
    print(f"== fr-CA rows from {path}: {', '.join(sorted(out))}")
    return out


def read_text(path):
    with open(path, encoding="utf-8") as f:
        return f.read().rstrip("\n")


def read_lines(path):
    with open(path, encoding="utf-8") as f:
        return [ln.rstrip("\n") for ln in f if ln.strip()]


# ---------------------------------------------------------------- files

def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def check_sums(pkg, sums_path):
    want = None
    for line in open(sums_path, encoding="utf-8"):
        parts = line.split()
        if len(parts) == 2 and parts[1].lstrip("*") == os.path.basename(pkg):
            want = parts[0]
    if want is None:
        die(f"{os.path.basename(pkg)} is not listed in {sums_path}")
    got = sha256(pkg)
    if got != want:
        die(f"{pkg} sha256 {got} does not match SHA256SUMS {want}")
    return got


def manifest_facts(pkg):
    """Identity and resource languages of the .msix inside a .msixupload (or of a .msix)."""
    with zipfile.ZipFile(pkg) as outer:
        names = outer.namelist()
        if "AppxManifest.xml" in names:
            xml = outer.read("AppxManifest.xml")
        else:
            inner = [n for n in names if n.lower().endswith(".msix")]
            if len(inner) != 1:
                die(f"{pkg}: expected one .msix inside, found {inner}")
            with zipfile.ZipFile(io.BytesIO(outer.read(inner[0]))) as z:
                xml = z.read("AppxManifest.xml")
    root = ET.fromstring(xml)
    ident = next(e for e in root.iter() if e.tag.endswith("}Identity"))
    langs = [e.get("Language") for e in root.iter() if e.tag.endswith("}Resource") and e.get("Language")]
    return {"Name": ident.get("Name"), "Publisher": ident.get("Publisher"),
            "Version": ident.get("Version"), "Architecture": ident.get("ProcessorArchitecture"), "Languages": langs}


def png_size(path):
    with open(path, "rb") as f:
        head = f.read(24)
    if head[:8] != b"\x89PNG\r\n\x1a\n":
        die(f"{path} is not a PNG")
    return struct.unpack(">II", head[16:24])


def screenshots(dirpath):
    files = sorted(os.path.join(dirpath, n) for n in os.listdir(dirpath) if n.lower().endswith(".png"))
    if not files:
        die(f"no .png in {dirpath}")
    if len(files) > 10:
        die(f"{len(files)} screenshots in {dirpath}; desktop allows 10")
    out = []
    for p in files:
        w, h = png_size(p)
        landscape = w >= 1366 and h >= 768
        portrait = h >= 1366 and w >= 768
        if not (landscape or portrait):
            die(f"{p} is {w}x{h}; desktop screenshots must be 1366x768 or larger")
        if os.path.getsize(p) > 50 * 1024 * 1024:
            die(f"{p} is over 50 MB")
        out.append((p, w, h))
    return out


# ---------------------------------------------------------------- the edit

def edit_submission(sub, args, notes, pkgs, fr_shots):
    sub = copy.deepcopy(sub)
    for p in sub.get("applicationPackages", []):
        if p.get("fileStatus") == "Uploaded":
            p["fileStatus"] = "PendingDelete"
    for path in pkgs:
        sub["applicationPackages"].append({
            "fileName": os.path.basename(path), "fileStatus": "PendingUpload",
            "minimumDirectXVersion": "None", "minimumSystemRam": "None"})

    listings = sub["listings"]
    en_key = next((k for k in listings if k.lower() == "en-us"), None)
    if en_key is None:
        die("the base submission has no en-us listing")
    en = listings[en_key]["baseListing"]
    en["releaseNotes"] = notes["en-US"]

    rows = load_rows(args.fr_ca_rows, en) if args.fr_ca_rows else {}
    fr = {
        "title": rows.get("title", "District AI"),  # never translated
        "description": rows.get("description") or (read_text(args.fr_ca_description) if args.fr_ca_description else ""),
        "releaseNotes": rows.get("releaseNotes", notes["fr-CA"]),
        "features": rows.get("features") or (read_lines(args.fr_ca_features) if args.fr_ca_features else []),
        "shortDescription": rows.get("shortDescription") or (read_text(args.fr_ca_short_description) if args.fr_ca_short_description else ""),
        "keywords": rows.get("keywords") or (read_lines(args.fr_ca_keywords) if args.fr_ca_keywords else []),
        "copyrightAndTrademarkInfo": rows.get("copyrightAndTrademarkInfo", en.get("copyrightAndTrademarkInfo", "")),
        "devStudio": rows.get("devStudio", en.get("devStudio", "")),
        # Empty on the live en-us listing; carried as they are:
        "licenseTerms": en.get("licenseTerms", ""),
        "privacyPolicy": en.get("privacyPolicy", ""),
        "supportContact": en.get("supportContact", ""),
        "websiteUrl": en.get("websiteUrl", ""),
        "recommendedHardware": [],
        "minimumHardware": [],
        "images": [{"fileName": "fr-ca/" + os.path.basename(p), "fileStatus": "PendingUpload",
                    "imageType": "Screenshot", "description": ""} for p, _, _ in fr_shots],
    }
    existing_fr = next((k for k in listings if k.lower() == "fr-ca"), None)
    if existing_fr:
        # 3.0.1 and later: the published submission already carries the reviewed fr-ca
        # listing and its 7 screenshots. Only its "What's new" changes, taken from the
        # tagged docs/store-listing.md. Any fr-CA listing input here would replace live,
        # reviewed text (S02 rows would even ship the 3.0.0 notes), so it is refused.
        given = [n for n in ("fr_ca_rows", "fr_ca_description", "fr_ca_features",
                             "fr_ca_short_description", "fr_ca_keywords", "fr_ca_screenshots")
                 if getattr(args, n)]
        if given or fr_shots:
            die(f"the base submission already has a {existing_fr} listing; only its releaseNotes are updated, "
                f"so drop {', '.join('--' + g.replace('_', '-') for g in given) or 'the fr-CA screenshots'}")
        listings[existing_fr]["baseListing"]["releaseNotes"] = notes["fr-CA"]
        return sub
    listings["fr-ca"] = {"baseListing": fr, "platformOverrides": {}}
    return sub


def check_listing(lang, b, problems, warnings):
    for k, lim in LIMITS.items():
        if len(b.get(k) or "") > lim:
            problems.append(f"{lang} {k} is {len(b[k])} chars, limit {lim}")
    feats = b.get("features") or []
    if len(feats) > 20:
        problems.append(f"{lang} has {len(feats)} features, limit 20")
    for f in feats:
        if len(f) > 200:
            problems.append(f"{lang} feature over 200 chars: {f[:40]}...")
    kws = b.get("keywords") or []
    if len(kws) > 7 or any(len(k) > 40 for k in kws) or sum(len(k.split()) for k in kws) > 21:
        problems.append(f"{lang} keywords break 7 terms / 40 chars each / 21 words: {kws}")
    if not b.get("description"):
        problems.append(f"{lang} description is empty (required by Partner Center)")
    if not b.get("images"):
        problems.append(f"{lang} has no screenshot (one is required per listing language)")
    if not b.get("shortDescription"):
        warnings.append(f"{lang} shortDescription is empty (optional; the Store then shows the description's first paragraph)")
    if not b.get("features"):
        warnings.append(f"{lang} features are empty (optional)")
    if not b.get("keywords"):
        warnings.append(f"{lang} keywords (search terms) are empty (optional)")


# ---------------------------------------------------------------- upload

def build_zip(pkgs, fr_shots, dest):
    with zipfile.ZipFile(dest, "w", zipfile.ZIP_STORED) as z:
        for p in pkgs:
            z.write(p, os.path.basename(p))
        for p, _, _ in fr_shots:
            z.write(p, "fr-ca/" + os.path.basename(p))


def upload_block_blob(sas_url, path):
    ids = []
    with open(path, "rb") as f:
        n = 0
        while True:
            chunk = f.read(BLOCK)
            if not chunk:
                break
            bid = base64.b64encode(f"{n:08d}".encode()).decode()
            req = urllib.request.Request(
                sas_url + "&comp=block&blockid=" + urllib.parse.quote(bid, safe=""), data=chunk, method="PUT")
            urllib.request.urlopen(req, timeout=300).read()
            ids.append(bid)
            n += 1
    body = "<?xml version=\"1.0\" encoding=\"utf-8\"?><BlockList>" + "".join(
        f"<Latest>{b}</Latest>" for b in ids) + "</BlockList>"
    req = urllib.request.Request(sas_url + "&comp=blocklist", data=body.encode(), method="PUT")
    req.add_header("Content-Type", "application/xml")
    urllib.request.urlopen(req, timeout=300).read()
    return len(ids)


def summary(body):
    """What this run changes, for the public log: packages, each listing's notes (by
    length and hash) and images. Never the whole resource, which carries the
    certification notes and signed URLs."""
    def notes(l):
        t = (l.get("baseListing") or {}).get("releaseNotes") or ""
        return {"releaseNotes": f"{len(t)} chars, sha256 {hashlib.sha256(t.encode()).hexdigest()[:16]}",
                "images": [(i.get("fileName"), i.get("fileStatus")) for i in (l.get("baseListing") or {}).get("images", [])]}
    return {"applicationPackages": [{k: p.get(k) for k in ("fileName", "fileStatus", "version")}
                                    for p in body.get("applicationPackages", [])],
            "listings": {lang: notes(l) for lang, l in body.get("listings", {}).items()},
            "targetPublishMode": body.get("targetPublishMode")}


def mask(value):
    """Ask the Actions runner to mask a value in every later log line."""
    if value and os.environ.get("GITHUB_ACTIONS") == "true":
        print(f"::add-mask::{value}")


def discard(dc, put_path, sub_id):
    """DELETE the submission this run created, after a failure before its commit."""
    st, resp = dc.call("DELETE", put_path)
    print(f"== DELETE {put_path} -> {st} (the submission this run created, so no draft is left pending)")
    if st not in (200, 204):
        print(f"   could not delete {sub_id}: {json.dumps(storeapi.redact(resp))[:300]}; delete it in Partner Center",
              file=sys.stderr)


# ---------------------------------------------------------------- main

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--msixupload", action="append", default=[], help="path of DistrictAI_<v>_x64.msixupload from the release (repeatable)")
    ap.add_argument("--sha256sums", help="the release's SHA256SUMS; each package must match it")
    ap.add_argument("--version", required=True, help="the release, X.Y.Z: the What's new section and the package version")
    ap.add_argument("--ref", required=True, help="the full commit the release was tagged on (docs/store-listing.md is read there)")
    ap.add_argument("--listing-md", help="read docs/store-listing.md from this file instead of GitHub")
    ap.add_argument("--fr-ca-rows", help="reviewed jsonl with windows.store.* rows (S02 final.jsonl); `fr` verbatim, overrides the doc's fr-CA What's new")
    ap.add_argument("--fr-ca-description", help="UTF-8 file: the fr-CA description (required for a complete fr-CA listing)")
    ap.add_argument("--fr-ca-features", help="UTF-8 file, one product feature per line")
    ap.add_argument("--fr-ca-short-description", help="UTF-8 file: the fr-CA short description")
    ap.add_argument("--fr-ca-keywords", help="UTF-8 file, one search term per line")
    ap.add_argument("--fr-ca-screenshots", help="directory of fr-CA desktop screenshots (.png, sorted by name)")
    ap.add_argument("--dry-run", action="store_true", help="read-only GETs; print what would be sent")
    ap.add_argument("--submit", action="store_true", help="commit the submission (sends it to certification)")
    args = ap.parse_args()
    if args.dry_run and args.submit:
        die("--dry-run and --submit together")
    if not re.fullmatch(r"\d+\.\d+\.\d+", args.version):
        die(f"--version {args.version!r} is not X.Y.Z")
    if not re.fullmatch(r"[0-9a-f]{40}", args.ref):
        die("--ref must be a full 40-character commit")

    md, md_src = load_listing_md(args)
    notes = whats_new(md, args.version)
    expect_version = args.version + ".0"

    print(f"== listing text source: {md_src}")
    for lang in ("en-US", "fr-CA"):
        t = notes[lang]
        print(f"   {lang} What's new: {len(t)} chars, {t.count(NBSP)} U+00A0, sha256 {hashlib.sha256(t.encode()).hexdigest()[:16]}")

    if not args.msixupload:
        if not args.dry_run:
            die("--msixupload is required")
        print("== packages: none given (dry run); the release run's DistrictAI_%s_x64.msixupload goes here" % args.version)
        pkgs = [f"DistrictAI_{args.version}_x64.msixupload"]
        have_files = False
    else:
        pkgs = args.msixupload
        have_files = True
        for p in pkgs:
            if not os.path.isfile(p):
                die(f"{p} does not exist")
            facts = manifest_facts(p)
            digest = check_sums(p, args.sha256sums) if args.sha256sums else sha256(p)
            print(f"== package {p}: {os.path.getsize(p)} bytes, sha256 {digest}"
                  f"{' (matches SHA256SUMS)' if args.sha256sums else ' (no SHA256SUMS given)'}")
            print(f"   manifest: {json.dumps(facts)}")
            if facts["Name"] != IDENTITY_NAME or facts["Publisher"] != PUBLISHER:
                die(f"{p} identity is not the Store identity {IDENTITY_NAME} / {PUBLISHER}")
            if facts["Version"] != expect_version:
                die(f"{p} version {facts['Version']} is not {expect_version}")
            if not any(lang.lower() == "fr-ca" for lang in facts["Languages"]):
                die(f"{p} declares no fr-CA resource")
            if (facts["Architecture"] or "").lower() != "x64":
                die(f"{p} is {facts['Architecture']!r}, not x64")

    fr_shots = screenshots(args.fr_ca_screenshots) if args.fr_ca_screenshots else []
    for p, w, h in fr_shots:
        print(f"== fr-CA screenshot {p}: {w}x{h}")

    dc = storeapi.DevCenter()
    app_path = f"/v1.0/my/applications/{storeapi.APP_ID}"
    st, app = dc.call("GET", app_path)
    print(f"== GET {app_path} -> {st}")
    if st != 200:
        die(json.dumps(app)[:500])
    pend = app.get("pendingApplicationSubmission")
    last = app.get("lastPublishedApplicationSubmission")
    print(f"   lastPublishedApplicationSubmission {json.dumps(last)}; pendingApplicationSubmission {json.dumps(pend)}")
    if pend:
        pid = pend.get("id")
        try:
            pst, pstat = dc.call("GET", f"{app_path}/submissions/{pid}/status")
        except TRANSPORT:
            pst, pstat = None, {}
        status = (pstat or {}).get("status") if pst == 200 else None
        if status == "PendingCommit" or status in FAILED:
            advice = "it never went through: finish it or delete it in Partner Center, then re-run"
        else:
            advice = "do NOT delete it: it may be going through (or its status is unreadable); wait for it to finish"
        die(f"submission {pid} is already pending (status {status!r}); {advice}")

    # The listing is checked on the last published submission (what POST would copy)
    # BEFORE anything is created: a submit that would refuse later must not leave a
    # pending draft behind, which blocks every later run until someone deletes it.
    st, published = dc.call("GET", f"{app_path}/submissions/{last['id']}")
    print(f"== GET {app_path}/submissions/{last['id']} -> {st} (the submission POST copies)")
    if st != 200:
        die(json.dumps(storeapi.redact(published))[:500])
    # A re-run after the release went live, or a dispatch of an older release, must not
    # submit again: the Store accepts any version ("you can submit these packages in any
    # order"), so it would re-certify the same build or roll new installs back.
    refuse_not_newer(published, expect_version)
    pre = edit_submission(published, args, notes, pkgs, fr_shots)
    pre_problems = []
    for lang, l in pre["listings"].items():
        check_listing(lang, l["baseListing"], pre_problems, [])
    if pre_problems and (args.dry_run or args.submit):
        for p in pre_problems:
            print("   INCOMPLETE: " + p)
        die("the listing would be incomplete; nothing was created")

    if args.dry_run:
        base = published
        sub_id = "<new submission id from POST>"
        print(f"== WOULD POST {app_path}/submissions (no body)")
    else:
        base = create_submission(dc, app_path, published)
        sub_id = base["id"]
        print(f"   new submission id {sub_id} (status {base.get('status')})")

    # From POST on, the submission exists. Anything that stops a live run before the
    # commit deletes it again (only this one, by the id POST returned), so a failed
    # run never leaves a pending draft that blocks every later run.
    put_path = f"{app_path}/submissions/{sub_id}"
    try:
        sas = base.get("fileUploadUrl")
        mask(sas)
        body = edit_submission(base, args, notes, pkgs, fr_shots)
        body.pop("fileUploadUrl", None)
        for k in ("id", "status", "statusDetails"):
            body.pop(k, None)

        problems, warnings = [], []
        for lang, l in body["listings"].items():
            check_listing(lang, l["baseListing"], problems, warnings)

        printable = summary(body)
        print(f"== {'WOULD ' if args.dry_run else ''}PUT {put_path}  Content-Type: application/json")
        print(json.dumps(printable, indent=2, ensure_ascii=False))

        zip_entries = [os.path.basename(p) for p in pkgs] + ["fr-ca/" + os.path.basename(p) for p, _, _ in fr_shots]
        print(f"== {'WOULD ' if args.dry_run else ''}upload one ZIP to the submission's fileUploadUrl "
              f"(SAS, redacted) as a block blob, {BLOCK // (1024 * 1024)} MiB blocks; entries: {json.dumps(zip_entries)}")
        if args.submit:
            print(f"== WILL POST {put_path}/commit (sends it to certification; publish mode {body.get('targetPublishMode')})")
        else:
            print(f"== no commit: the submission stays a draft (PendingCommit). Pass --submit to commit.")

        print("== listing check")
        for w in warnings:
            print("   warning: " + w)
        for p in problems:
            print("   INCOMPLETE: " + p)
        if not problems:
            print("   complete")

        if args.dry_run:
            print("== dry run: nothing was created, uploaded or committed")
            return
        if args.submit and problems:
            die("the listing is incomplete; not committing")

        st, resp = dc.call("PUT", put_path, body)
        print(f"== PUT {put_path} -> {st}")
        if st != 200:
            die(json.dumps(storeapi.redact(resp))[:2000])

        if have_files:
            work = os.environ.get("RUNNER_TEMP") or os.environ.get("TMPDIR") or "."
            zpath = os.path.join(work, f"msstore-{sub_id}.zip")
            build_zip(pkgs, fr_shots, zpath)
            try:
                n = upload_block_blob(sas, zpath)
            finally:
                os.remove(zpath)
            print(f"== uploaded {zpath} in {n} blocks")

    except BaseException:
        # Before the commit, deleting this run's draft is always safe, a runner cancel
        # (KeyboardInterrupt) included.
        if not args.dry_run:
            discard(dc, put_path, sub_id)
        raise

    if not args.submit:
        print(f"== DRAFT submission {sub_id} left uncommitted. Review it in Partner Center, then commit with the API"
              " (POST .../commit) or submit it there (Partner Center edits lock the API out of it).")
        return

    commit_and_wait(dc, put_path, sub_id)


def create_submission(dc, app_path, published=None):
    """POST a new submission and return it.

    When the answer never arrives whole (transport error, truncated or non-JSON body) or
    is a 5xx, the submission may exist without this run knowing its id. A pending one is
    deleted only when it is provably an untouched copy that was never committed: status
    PendingCommit, and the packages and listings of the published submission it copies.
    Anything else is kept and the run fails, naming it."""
    try:
        st, base = dc.call("POST", f"{app_path}/submissions")
    except TRANSPORT as e:
        st, base = None, {"error": type(e).__name__}
    print(f"== POST {app_path}/submissions -> {st}")
    if st is not None and 200 <= st < 300 and (base or {}).get("id"):
        return base
    if st is None or st >= 500 or st == 408 or 200 <= st < 300:
        try:
            rst, app = dc.call("GET", app_path)
        except TRANSPORT:
            rst, app = None, {}
        if rst != 200:
            die(f"creating the submission failed ({st}) and the application could not be re-read; check Partner "
                "Center for a pending submission before any re-run")
        pend = (app or {}).get("pendingApplicationSubmission")
        if pend and pend.get("id"):
            pid = pend["id"]
            try:
                gst, sub = dc.call("GET", f"{app_path}/submissions/{pid}")
            except TRANSPORT:
                gst, sub = None, {}
            untouched = (gst == 200 and (sub or {}).get("status") == "PendingCommit" and published is not None
                         and sub.get("applicationPackages") == published.get("applicationPackages")
                         and sub.get("listings") == published.get("listings"))
            if untouched:
                discard(dc, f"{app_path}/submissions/{pid}", pid)
                die(f"creating the submission failed ({st}); the untouched draft it left ({pid}) was deleted")
            die(f"creating the submission failed ({st}); submission {pid} is pending (status "
                f"{(sub or {}).get('status')!r}) and is not provably this run's untouched draft: kept, check Partner Center")
    die(f"creating the submission failed ({st}): {json.dumps(storeapi.redact(base))[:1000]}")


def read_status(dc, put_path):
    try:
        st, stat = dc.call("GET", f"{put_path}/status")
    except TRANSPORT as e:
        print(f"   status read failed: {type(e).__name__}")
        return None, None
    status = (stat or {}).get("status") if st == 200 else None
    print(f"   status {st} {status}")
    return status, (stat if st == 200 else None)


def commit_and_wait(dc, put_path, sub_id, polls=30, interval=20):
    """POST the commit and poll its status.

    Deleted: only a submission that reads PendingCommit after a definite refusal (a 4xx
    other than 408), or that ends in a documented terminal failure (FAILED). An unknown
    commit outcome (transport error, 408, 5xx) is never a reason to delete: the status
    decides. Kept, and the run fails naming it: anything still in progress, unreadable or
    unknown, and a PendingCommit draft whose commit outcome was unknown."""
    try:
        st, resp = dc.call("POST", f"{put_path}/commit")
    except TRANSPORT as e:
        st, resp = None, {"error": type(e).__name__}
    print(f"== POST {put_path}/commit -> {st} {json.dumps(storeapi.redact(resp))[:300]}")
    refused = st is not None and 400 <= st < 500 and st != 408
    accepted = st is not None and 200 <= st < 300
    if refused:
        status, _ = read_status(dc, put_path)
        if status == "PendingCommit":
            discard(dc, put_path, sub_id)
            die(f"commit refused (HTTP {st}); the draft read PendingCommit and was deleted, so a re-run starts clean")
        die(f"commit refused (HTTP {st}), but submission {sub_id} reads {status!r}; kept, check Partner Center")
    if not accepted:
        print(f"   the commit's outcome is unknown (HTTP {st}); the status decides, nothing is deleted on it")
    status, stat, reads = None, None, 0
    for _ in range(polls):
        time.sleep(interval)
        s, body = read_status(dc, put_path)
        if s is None:
            continue
        status, stat, reads = s, body, reads + 1
        if s not in ("CommitStarted", "PendingCommit"):
            break
    print(json.dumps(storeapi.redact(stat), indent=2)[:3000])
    if status in COMMITTED:
        print(f"== submission {sub_id} committed: {status}")
        return
    if status in FAILED:
        errors = ((stat or {}).get("statusDetails") or {}).get("errors") or []
        detail = "; ".join(f"{e.get('code')}: {e.get('details')}" for e in errors) or "no error details"
        discard(dc, put_path, sub_id)
        die(f"submission {sub_id} ended {status} ({detail}); the draft was deleted, so a re-run starts clean")
    if status in KEPT_TERMINAL:
        die(f"submission {sub_id} ended {status}; kept (not deleted): it passed certification or is no longer "
            "pending. Read it in Partner Center")
    if status is None:
        die(f"submission {sub_id}: no status read succeeded in {polls} tries (commit HTTP {st}); kept: read it in "
            "Partner Center before any re-run, and do not delete it unless it reads PendingCommit")
    if status in ("CommitStarted", "PendingCommit"):
        die(f"submission {sub_id} still {status} after {polls * interval // 60} minutes (commit HTTP {st}); kept: "
            "read its status in Partner Center (it may still go through)")
    die(f"submission {sub_id} ended {status!r}, not a known committed or failed status; kept for Partner Center")


# ---------------------------------------------------------------- self-test

class _FakePC:
    """An in-process Partner Center for the self-test: answers by (method, path suffix)."""

    def __init__(self, post=(201, None), commit=(202, {}), statuses=((200, {"status": "PreProcessing"}),),
                 app=None, sub=None, put=(200, {})):
        self.post, self.commit, self.statuses = post, commit, list(statuses)
        self.app, self.sub, self.put = app, sub, put
        self.calls = []

    @staticmethod
    def _answer(x):
        if isinstance(x, BaseException):
            raise x
        return x

    def call(self, method, path, body=None):
        self.calls.append((method, path))
        if method == "DELETE":
            return 204, None
        if method == "PUT":
            return self._answer(self.put)
        if method == "POST" and path.endswith("/commit"):
            return self._answer(self.commit)
        if method == "POST":
            return self._answer(self.post)
        if path.endswith("/status"):
            return self._answer(self.statuses.pop(0) if len(self.statuses) > 1 else self.statuses[0])
        if "/submissions/" in path:
            return self._answer(self.sub)
        return self._answer(self.app)

    def deleted(self):
        return any(m == "DELETE" for m, _ in self.calls)


def _quiet(fn):
    code, out = 0, io.StringIO()
    try:
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(out):
            fn()
    except SystemExit as e:
        code = e.code
    return code, out.getvalue()


def _main_case(tmp, fake, upload):
    """main() --submit against a fake, with a real package, SHA256SUMS and listing."""
    manifest = ('<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">'
                f'<Identity Name="{IDENTITY_NAME}" Publisher="{PUBLISHER}" Version="3.3.0.0" '
                'ProcessorArchitecture="x64"/><Resources><Resource Language="EN-US"/>'
                '<Resource Language="FR-CA"/></Resources></Package>')
    inner = io.BytesIO()
    with zipfile.ZipFile(inner, "w") as z:
        z.writestr("AppxManifest.xml", manifest)
    pkg = os.path.join(tmp, "DistrictAI_3.3.0_x64.msixupload")
    with zipfile.ZipFile(pkg, "w") as z:
        z.writestr("DistrictAI_3.3.0.0_x64.msix", inner.getvalue())
    with open(os.path.join(tmp, "SHA256SUMS"), "w") as f:
        f.write(f"{sha256(pkg)}  {os.path.basename(pkg)}\n")
    with open(os.path.join(tmp, "listing.md"), "w") as f:
        f.write("## What's new in this version: 3.3.0\n\nen-US:\n\n```text\nFixes.\n```\n\n"
                "fr-CA:\n\n```text\nCorrectifs.\n```\n")
    real_dc, real_upload, real_argv = storeapi.DevCenter, globals()["upload_block_blob"], sys.argv
    storeapi.DevCenter = lambda: fake
    globals()["upload_block_blob"] = upload
    sys.argv = ["p", "--version", "3.3.0", "--ref", "0" * 40, "--listing-md", os.path.join(tmp, "listing.md"),
                "--msixupload", pkg, "--sha256sums", os.path.join(tmp, "SHA256SUMS"), "--submit"]
    os.environ["RUNNER_TEMP"] = tmp
    try:
        try:
            return _quiet(main)
        except KeyboardInterrupt:
            return "interrupted", ""
    finally:
        storeapi.DevCenter, sys.argv = real_dc, real_argv
        globals()["upload_block_blob"] = real_upload


def self_test():
    """Which outcomes delete a draft and which keep it, against fakes (no network)."""
    global time
    import tempfile
    real_time = time

    class NoSleep:
        @staticmethod
        def sleep(_):
            pass
    time = NoSleep
    results = []

    def check(name, fake, fn, want_code, want_delete, want_text):
        code, text = _quiet(fn)
        ok = code == want_code and fake.deleted() == want_delete and want_text in text
        results.append(ok)
        print(("ok   " if ok else "FAIL ") + name + ("" if ok else f" (exit {code}, DELETE {fake.deleted()})"))

    def commit(name, want_code, want_delete, want_text, **kw):
        fake = _FakePC(**kw)
        check(name, fake, lambda: commit_and_wait(fake, "/s/2", "2", polls=3, interval=0),
              want_code, want_delete, want_text)

    ok200 = (200, {"status": "PreProcessing"})
    pending_commit = (200, {"status": "PendingCommit"})
    commit("CommitFailed deletes the draft and fails", 2, True, "InvalidState: x", statuses=[
        (200, {"status": "CommitFailed", "statusDetails": {"errors": [{"code": "InvalidState", "details": "x"}]}})])
    commit("CertificationFailed deletes the draft and fails", 2, True, "no error details",
           statuses=[(200, {"status": "CertificationFailed"})])
    commit("a refused commit deletes a draft that reads PendingCommit", 2, True, "was deleted",
           commit=(409, {}), statuses=[pending_commit])
    commit("a refused commit keeps a submission that reads anything else", 2, False, "kept",
           commit=(409, {}), statuses=[(200, {"status": "CommitStarted"})])
    commit("a commit answered 503 that went through is kept and committed", 0, False, "committed: PreProcessing",
           commit=(503, {}), statuses=[(200, {"status": "CommitStarted"}), ok200])
    commit("a commit answered 204 is accepted", 0, False, "committed", commit=(204, None), statuses=[ok200])
    commit("a commit that times out lets the status decide", 0, False, "committed",
           commit=TimeoutError("t"), statuses=[ok200])
    commit("an unknown commit outcome that never started is kept", 2, False, "kept",
           commit=(503, {}), statuses=[pending_commit])
    commit("one failed status read does not fail a commit", 0, False, "committed",
           statuses=[urllib.error.URLError("down"), ok200])
    commit("status reads that always fail keep the submission", 2, False, "no status read succeeded",
           statuses=[http.client.RemoteDisconnected("x")])
    commit("PublishFailed is kept", 2, False, "PublishFailed; kept", statuses=[(200, {"status": "PublishFailed"})])
    commit("an unknown status keeps the submission", 2, False, "kept", statuses=[(200, {"status": "Mystery"})])
    commit("a poll that times out keeps the submission", 2, False, "kept",
           statuses=[(200, {"status": "CommitStarted"})])

    published = {"applicationPackages": [{"fileName": "a", "fileStatus": "Uploaded", "version": "3.2.0.0"}],
                 "listings": {"en-us": {"baseListing": {"releaseNotes": "old"}}}}
    untouched = (200, {**copy.deepcopy(published), "id": "9", "status": "PendingCommit"})
    app_pending = (200, {"pendingApplicationSubmission": {"id": "9"}})

    def create(name, want_code, want_delete, want_text, **kw):
        fake = _FakePC(**kw)
        check(name, fake, lambda: create_submission(fake, "/a", published), want_code, want_delete, want_text)

    create("a POST that times out deletes the untouched draft it left", 2, True, "(9) was deleted",
           post=TimeoutError("t"), app=app_pending, sub=untouched)
    create("a truncated POST answer deletes the untouched draft it left", 2, True, "(9) was deleted",
           post=http.client.IncompleteRead(b"{"), app=app_pending, sub=untouched)
    create("a POST answered 503 keeps someone else's draft", 2, False, "not provably",
           post=(503, {}), app=app_pending,
           sub=(200, {"id": "9", "status": "PendingCommit", "applicationPackages": [], "listings": {}}))
    create("a POST answered 503 keeps a submission in flight", 2, False, "not provably",
           post=(503, {}), app=app_pending, sub=(200, {**copy.deepcopy(published), "status": "CommitStarted"}))
    create("a POST that left nothing deletes nothing", 2, False, "failed (503)",
           post=(503, {}), app=(200, {"pendingApplicationSubmission": None}))
    create("an unreadable application after a failed POST deletes nothing", 2, False, "check Partner Center",
           post=OSError("x"), app=(500, {}))
    create("a POST refused with 4xx deletes nothing", 2, False, "409", post=(409, {"code": "InvalidState"}),
           app=app_pending, sub=untouched)
    create("a created submission is returned", 0, False, "-> 201", post=(201, {"id": "5"}))

    for name, version, want_code in (("the live version is refused", "3.2.0.0", 2),
                                     ("an older version is refused", "3.1.0.0", 2),
                                     ("a newer version is allowed", "3.3.0.0", 0)):
        code, _ = _quiet(lambda v=version: refuse_not_newer(published, v))
        results.append(code == want_code)
        print(("ok   " if code == want_code else "FAIL ") + name)

    md = ("## What's new in this version: 3.1.10\n\nen-US:\n\n```text\nten\n```\n\nfr-CA:\n\n```text\ndix\n```\n")
    code, _ = _quiet(lambda: whats_new(md, "3.1.1"))
    results.append(code == 2)
    print(("ok   " if code == 2 else "FAIL ") + "3.1.1 never takes the 3.1.10 section")

    base = {**copy.deepcopy(published), "id": "2", "status": "PendingCommit", "fileUploadUrl": "https://sas?sig=x",
            "listings": {lang: {"baseListing": {"releaseNotes": "old", "description": "d", "images": [{"fileName": "1"}],
                                                "shortDescription": "s", "features": ["f"], "keywords": ["k"]}}
                         for lang in ("en-us", "fr-ca")}}
    with tempfile.TemporaryDirectory() as tmp:
        for name, upload, put, want in (
                ("a runner cancel during the upload deletes the draft", None, (200, {}), "interrupted"),
                ("a failed PUT deletes the draft", None, (500, {"code": "x"}), 2)):
            def boom(sas, path):
                raise KeyboardInterrupt
            fake = _FakePC(post=(201, copy.deepcopy(base)), put=put,
                           app=(200, {"pendingApplicationSubmission": None,
                                      "lastPublishedApplicationSubmission": {"id": "1"}}),
                           sub=(200, copy.deepcopy(base)))
            code, _ = _main_case(tmp, fake, boom if upload is None and want == "interrupted" else (lambda s, p: 1))
            ok = code == want and fake.deleted()
            results.append(ok)
            print(("ok   " if ok else "FAIL ") + name + ("" if ok else f" (exit {code}, DELETE {fake.deleted()})"))
    time = real_time
    if not all(results):
        print(f"{results.count(False)} of {len(results)} cases failed", file=sys.stderr)
        sys.exit(1)
    print(f"{len(results)} cases passed")


if __name__ == "__main__":
    if sys.argv[1:] == ["--self-test"]:
        self_test()
    else:
        main()
