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
# Terminal failures the submission API documents: the submission stays pending until deleted.
FAILED = {"CommitFailed", "PreProcessingFailed", "CertificationFailed", "ReleaseFailed"}
LIMITS = {"description": 10000, "releaseNotes": 1500, "shortDescription": 1000, "title": 256}


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
    if head not in md:
        die(f"docs/store-listing.md has no section '{head}'")
    sect = md.split(head, 1)[1]
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
            "Version": ident.get("Version"), "Languages": langs}


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
        die(f"submission {pend.get('id')} is already pending; finish or delete it in Partner Center first")

    # The listing is checked on the last published submission (what POST would copy)
    # BEFORE anything is created: a submit that would refuse later must not leave a
    # pending draft behind, which blocks every later run until someone deletes it.
    st, published = dc.call("GET", f"{app_path}/submissions/{last['id']}")
    print(f"== GET {app_path}/submissions/{last['id']} -> {st} (the submission POST copies)")
    if st != 200:
        die(json.dumps(storeapi.redact(published))[:500])
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
        base = create_submission(dc, app_path)
        sub_id = base["id"]
        print(f"   new submission id {sub_id} (status {base.get('status')})")

    # From POST on, the submission exists. Anything that stops a live run before the
    # commit deletes it again (only this one, by the id POST returned), so a failed
    # run never leaves a pending draft that blocks every later run.
    put_path = f"{app_path}/submissions/{sub_id}"
    try:
        sas = base.get("fileUploadUrl")
        body = edit_submission(base, args, notes, pkgs, fr_shots)
        body.pop("fileUploadUrl", None)
        for k in ("id", "status", "statusDetails"):
            body.pop(k, None)

        problems, warnings = [], []
        for lang, l in body["listings"].items():
            check_listing(lang, l["baseListing"], problems, warnings)

        printable = storeapi.redact(body)
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

    except (Exception, SystemExit):
        if not args.dry_run:
            discard(dc, put_path, sub_id)
        raise

    if not args.submit:
        print(f"== DRAFT submission {sub_id} left uncommitted. Review it in Partner Center, then commit with the API"
              " (POST .../commit) or submit it there (Partner Center edits lock the API out of it).")
        return

    commit_and_wait(dc, put_path, sub_id)


def create_submission(dc, app_path):
    """POST a new submission and return it.

    When the answer never arrives (a network error or timeout) or is a 5xx, the
    submission may exist without this run knowing its id. No submission was pending
    when the run began (checked above, and store.yml runs one at a time), so a
    pending one now is this run's: it is deleted by the id the application reports,
    and the run fails."""
    try:
        st, base = dc.call("POST", f"{app_path}/submissions")
    except (OSError, urllib.error.URLError) as e:
        st, base = None, {"error": type(e).__name__}
    print(f"== POST {app_path}/submissions -> {st}")
    if st in (200, 201) and (base or {}).get("id"):
        return base
    if st is None or st >= 500 or st in (200, 201):
        try:
            rst, app = dc.call("GET", app_path)
        except (OSError, urllib.error.URLError):
            rst, app = None, {}
        pend = (app or {}).get("pendingApplicationSubmission") if rst == 200 else None
        if pend and pend.get("id"):
            discard(dc, f"{app_path}/submissions/{pend['id']}", pend["id"])
            die(f"creating the submission failed ({st}); the submission it left ({pend['id']}) was deleted")
        if rst != 200:
            die(f"creating the submission failed ({st}) and the application could not be re-read; check Partner "
                "Center for a pending submission before any re-run")
    die(f"creating the submission failed ({st}): {json.dumps(storeapi.redact(base))[:1000]}")


def commit_and_wait(dc, put_path, sub_id, polls=30, interval=20):
    """POST the commit and poll its status.

    A refused commit, or a terminal failure (FAILED), deletes the draft this run made:
    left pending, it blocks every later run until someone deletes it in Partner Center.
    A status still in progress, an unknown one or a poll that times out keeps the
    submission, because it may still go through; deleting it, or starting another,
    could make a second live submission."""
    st, resp = dc.call("POST", f"{put_path}/commit")
    print(f"== POST {put_path}/commit -> {st} {json.dumps(storeapi.redact(resp))[:300]}")
    if st not in (200, 202):
        discard(dc, put_path, sub_id)
        die(f"commit refused (HTTP {st})")
    stat = {}
    for _ in range(polls):
        time.sleep(interval)
        st, stat = dc.call("GET", f"{put_path}/status")
        status = (stat or {}).get("status")
        print(f"   status {st} {status}")
        if st == 200 and status != "CommitStarted":
            break
    status = (stat or {}).get("status")
    print(json.dumps(storeapi.redact(stat), indent=2)[:3000])
    if status in COMMITTED:
        print(f"== submission {sub_id} committed: {status}")
        return
    if status in FAILED:
        errors = ((stat or {}).get("statusDetails") or {}).get("errors") or []
        detail = "; ".join(f"{e.get('code')}: {e.get('details')}" for e in errors) or "no error details"
        discard(dc, put_path, sub_id)
        die(f"submission {sub_id} ended {status} ({detail}); the draft was deleted, so a re-run starts clean")
    if status == "CommitStarted":
        die(f"submission {sub_id} still CommitStarted after {polls * interval // 60} minutes; read its status "
            "in Partner Center (kept: it may still go through)")
    die(f"submission {sub_id} ended {status!r}, not a known committed or failed status; kept for Partner Center")


# ---------------------------------------------------------------- self-test

def self_test():
    """commit_and_wait against a fake Partner Center: which statuses delete the draft."""
    global time
    real_time = time

    class NoSleep:
        @staticmethod
        def sleep(_):
            pass
    time = NoSleep
    failures = []

    def run(commit_code, statuses):
        calls = []

        class DC:
            def call(self, method, path, body=None):
                calls.append(method)
                if method == "POST":
                    return commit_code, {}
                if method == "DELETE":
                    return 204, None
                return statuses.pop(0) if len(statuses) > 1 else statuses[0]
        code = 0
        out = io.StringIO()
        try:
            with contextlib.redirect_stdout(out), contextlib.redirect_stderr(out):
                commit_and_wait(DC(), "/s/2", "2", polls=3, interval=0)
        except SystemExit as e:
            code = e.code
        return code, calls, out.getvalue()

    failed = (200, {"status": "CommitFailed", "statusDetails": {"errors": [{"code": "InvalidState", "details": "x"}]}})
    cases = [
        ("CommitFailed deletes the draft and fails", (200, [failed]), 2, True, "InvalidState: x"),
        ("CertificationFailed deletes the draft and fails",
         (200, [(200, {"status": "CertificationFailed"})]), 2, True, "no error details"),
        ("a refused commit deletes the draft", (409, [(200, {})]), 2, True, "HTTP 409"),
        ("a poll that times out keeps the submission",
         (200, [(200, {"status": "CommitStarted"})]), 2, False, "kept"),
        ("an unknown status keeps the submission", (200, [(200, {"status": "Mystery"})]), 2, False, "kept"),
        ("a status read that keeps failing keeps the submission", (200, [(503, {"raw": "x"})]), 2, False, "kept"),
        ("Certification is committed", (200, [(200, {"status": "CommitStarted"}), (200, {"status": "Certification"})]),
         0, False, "committed: Certification"),
    ]
    def create(post, app):
        calls = []

        class DC:
            def call(self, method, path, body=None):
                calls.append(method)
                if method == "POST":
                    if isinstance(post, Exception):
                        raise post
                    return post
                if method == "DELETE":
                    return 204, None
                return app
        code, out = 0, io.StringIO()
        try:
            with contextlib.redirect_stdout(out), contextlib.redirect_stderr(out):
                create_submission(DC(), "/a")
        except SystemExit as e:
            code = e.code
        return code, calls, out.getvalue()

    pending = (200, {"pendingApplicationSubmission": {"id": "9"}})
    create_cases = [
        ("a POST that times out deletes the submission it left", (TimeoutError("t"), pending), 2, True, "(9) was deleted"),
        ("a POST answered 503 deletes the submission it left", ((503, {}), pending), 2, True, "(9) was deleted"),
        ("a POST that left nothing deletes nothing", ((503, {}), (200, {"pendingApplicationSubmission": None})),
         2, False, "failed (503)"),
        ("an unreadable application after a failed POST deletes nothing", (OSError("x"), (500, {})), 2, False,
         "check Partner Center"),
        ("a POST refused with 4xx deletes nothing", ((409, {"code": "InvalidState"}), pending), 2, False, "409"),
        ("a created submission is returned", ((201, {"id": "5"}), pending), 0, False, "-> 201"),
    ]
    for name, (post, app), want_code, want_delete, want_text in create_cases:
        got_code, calls, text = create(post, app)
        deleted = "DELETE" in calls
        ok = got_code == want_code and deleted == want_delete and want_text in text
        if not ok:
            failures.append(f"{name}: exit {got_code}, DELETE {deleted}")
        print(("ok   " if ok else "FAIL ") + name)
    cases_total = len(create_cases)
    for name, (code, statuses), want_code, want_delete, want_text in cases:
        got_code, calls, text = run(code, list(statuses))
        deleted = "DELETE" in calls
        if got_code != want_code or deleted != want_delete or want_text not in text:
            failures.append(f"{name}: exit {got_code}, DELETE {deleted}")
        print(("ok   " if not failures or not failures[-1].startswith(name) else "FAIL ") + name)
    time = real_time
    if failures:
        print("\n".join(failures), file=sys.stderr)
        sys.exit(1)
    print(f"{len(cases) + cases_total} cases passed")


if __name__ == "__main__":
    if sys.argv[1:] == ["--self-test"]:
        self_test()
    else:
        main()
