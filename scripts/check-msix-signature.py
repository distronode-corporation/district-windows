#!/usr/bin/env python3
"""Fail a signed MSIX whose signature will stop installing too soon.

    python scripts/check-msix-signature.py <package.msix> [--cer <cert.cer>]
        [--min-days 365]                                # judge a package
    python scripts/check-msix-signature.py --self-test  # prove the reading works

District AI 3.4.0's sideload test package was signed with a certificate that
was valid for one day and carried no timestamp, so from the next day Windows
refused to install it (0x800B0101, "a required certificate is not within its
validity period"). This check reads the package's own signature and refuses it
unless both of these hold:

- the signing certificate is valid for at least --min-days more days (365 by
  default), so the package installs for a year even without a timestamp;
- the signature carries an RFC 3161 timestamp (signtool's /tr), made while the
  certificate was valid. Windows then keeps accepting the signature after the
  certificate expires, because it checks the certificate against the time the
  timestamp proves, not against today.

With --cer, the certificate a release publishes beside the package must be
the one that signed it, byte for byte, or a tester would trust the wrong one.

An MSIX keeps its signature in AppxSignature.p7x: the four bytes "PKCX", then
a DER-encoded PKCS #7 (CMS) SignedData. The reader below is a small DER walker
using only the standard library, since the release runs it on a Windows runner
with nothing installed. It reads, and never verifies, the signature: Windows
does that at install, and the release's other steps (the signer's subject, the
certification kit's install) already depend on it.
"""

from __future__ import annotations

import argparse
import datetime as dt
import sys
import tempfile
import zipfile
from dataclasses import dataclass
from pathlib import Path

OID_SIGNED_DATA = "1.2.840.113549.1.7.2"
OID_TST_INFO = "1.2.840.113549.1.9.16.1.4"
# szOID_RFC3161_counterSign: where signtool /tr puts the timestamp token.
OID_RFC3161_COUNTERSIGN = "1.3.6.1.4.1.311.3.3.1"
# PKCS #9 counterSignature: the legacy Authenticode timestamp (signtool /t).
OID_LEGACY_COUNTERSIGN = "1.2.840.113549.1.9.6"

DEFAULT_MIN_DAYS = 365


class SignatureError(Exception):
    pass


# --- DER --------------------------------------------------------------------


@dataclass
class Node:
    tag: int  # the whole identifier octet: class, constructed bit and number
    body: bytes
    raw: bytes  # the encoding, header included

    @property
    def constructed(self) -> bool:
        return bool(self.tag & 0x20)

    def children(self) -> list["Node"]:
        if not self.constructed:
            raise SignatureError(f"tag 0x{self.tag:02x} is not constructed")
        return parse_all(self.body)


def parse_one(data: bytes, at: int = 0) -> tuple[Node, int]:
    if at + 2 > len(data):
        raise SignatureError("truncated DER")
    tag = data[at]
    if tag & 0x1F == 0x1F:
        raise SignatureError("multi-byte DER tags are not used here")
    length = data[at + 1]
    head = 2
    if length & 0x80:
        count = length & 0x7F
        if count == 0 or count > 4 or at + 2 + count > len(data):
            raise SignatureError("unsupported DER length")
        length = int.from_bytes(data[at + 2 : at + 2 + count], "big")
        head += count
    end = at + head + length
    if end > len(data):
        raise SignatureError("DER length runs past the data")
    return Node(tag, data[at + head : end], data[at:end]), end


def parse_all(data: bytes) -> list[Node]:
    nodes, at = [], 0
    while at < len(data):
        node, at = parse_one(data, at)
        nodes.append(node)
    return nodes


def oid(node: Node) -> str:
    if node.tag != 0x06 or not node.body:
        raise SignatureError("expected an OBJECT IDENTIFIER")
    first = node.body[0]
    parts = [min(first // 40, 2), first - 40 * min(first // 40, 2)]
    value = 0
    for byte in node.body[1:]:
        value = (value << 7) | (byte & 0x7F)
        if not byte & 0x80:
            parts.append(value)
            value = 0
    return ".".join(str(p) for p in parts)


def time_of(node: Node) -> dt.datetime:
    text = node.body.decode("ascii")
    if node.tag == 0x17:  # UTCTime, YYMMDDHHMMSSZ
        year = int(text[:2])
        text = f"{1900 + year if year >= 50 else 2000 + year}{text[2:]}"
    elif node.tag != 0x18:  # GeneralizedTime, YYYYMMDDHHMMSS[.fff]Z
        raise SignatureError(f"tag 0x{node.tag:02x} is not a time")
    if not text.endswith("Z"):
        raise SignatureError(f"time {text!r} is not in UTC")
    return dt.datetime.strptime(text[:14], "%Y%m%d%H%M%S").replace(tzinfo=dt.timezone.utc)


# --- PKCS #7 ----------------------------------------------------------------


@dataclass
class Certificate:
    raw: bytes
    serial: bytes
    issuer: bytes
    subject: bytes
    not_before: dt.datetime
    not_after: dt.datetime


@dataclass
class Signature:
    signer: Certificate
    timestamp_kind: str  # "rfc3161", "legacy" or ""
    timestamp_time: dt.datetime | None


def read_certificate(node: Node) -> Certificate:
    tbs = node.children()[0].children()
    if tbs and tbs[0].tag == 0xA0:  # [0] EXPLICIT version
        tbs = tbs[1:]
    serial, _alg, issuer, validity, subject = tbs[:5]
    not_before, not_after = (time_of(t) for t in validity.children())
    return Certificate(node.raw, serial.body, issuer.raw, subject.raw, not_before, not_after)


def signed_data(content_info: bytes) -> list[Node]:
    outer = parse_one(content_info)[0].children()
    if len(outer) != 2 or oid(outer[0]) != OID_SIGNED_DATA or outer[1].tag != 0xA0:
        raise SignatureError("not a PKCS #7 SignedData")
    return outer[1].children()[0].children()


def tst_time(token: bytes) -> dt.datetime:
    """The genTime of an RFC 3161 timestamp token (a SignedData over TSTInfo)."""
    fields = signed_data(token)
    encap = fields[2].children()
    if oid(encap[0]) != OID_TST_INFO or len(encap) < 2:
        raise SignatureError("the timestamp token holds no TSTInfo")
    octets = encap[1].children()[0]
    tst = parse_one(octets.body)[0].children()
    # version, policy, messageImprint, serialNumber, genTime, ...
    return time_of(tst[4])


def read_signature(p7: bytes) -> Signature:
    fields = signed_data(p7)
    certificates = [read_certificate(c) for f in fields if f.tag == 0xA0 for c in f.children()]
    signer_infos = fields[-1]
    if signer_infos.tag != 0x31:
        raise SignatureError("SignedData has no signerInfos")
    infos = signer_infos.children()
    if len(infos) != 1:
        raise SignatureError(f"expected one signer, found {len(infos)}")
    info = infos[0].children()
    sid = info[1].children()  # IssuerAndSerialNumber
    if len(sid) != 2:
        raise SignatureError("the signer is not named by issuer and serial number")
    matches = [c for c in certificates if c.serial == sid[1].body and c.issuer == sid[0].raw]
    if len(matches) != 1:
        raise SignatureError("the signature does not carry its signer's certificate")

    kind, when = "", None
    for attrs in (n for n in info if n.tag == 0xA1):  # [1] IMPLICIT unsignedAttrs
        for attr in attrs.children():
            attr_type, values = attr.children()
            if oid(attr_type) == OID_RFC3161_COUNTERSIGN:
                kind, when = "rfc3161", tst_time(values.children()[0].raw)
            elif oid(attr_type) == OID_LEGACY_COUNTERSIGN and not kind:
                kind = "legacy"
    return Signature(matches[0], kind, when)


def p7x_of(package: Path) -> bytes:
    with zipfile.ZipFile(package) as z:
        try:
            data = z.read("AppxSignature.p7x")
        except KeyError:
            raise SignatureError(f"{package.name} is not signed (no AppxSignature.p7x)") from None
    if data[:4] != b"PKCX":
        raise SignatureError("AppxSignature.p7x does not start with PKCX")
    return data[4:]


# --- the verdict --------------------------------------------------------------


def judge(sig: Signature, now: dt.datetime, min_days: int, cer: bytes | None) -> list[str]:
    problems = []
    cert = sig.signer
    left = cert.not_after - now
    if left < dt.timedelta(days=min_days):
        problems.append(
            f"the signing certificate expires {cert.not_after:%Y-%m-%d %H:%M}Z, "
            f"{left.days} days from now; a release needs at least {min_days}"
        )
    if sig.timestamp_kind != "rfc3161":
        found = "a legacy Authenticode timestamp" if sig.timestamp_kind else "no timestamp"
        problems.append(
            f"the signature has {found}, not an RFC 3161 one: "
            "it stops installing the day the certificate expires"
        )
    elif not (cert.not_before <= sig.timestamp_time <= cert.not_after):
        problems.append(
            f"the timestamp ({sig.timestamp_time:%Y-%m-%d %H:%M}Z) is outside the "
            "certificate's validity, so it vouches for nothing"
        )
    if cer is not None and cer != cert.raw:
        problems.append("the published certificate is not the one that signed the package")
    return problems


def check(package: Path, cer_path: Path | None, min_days: int, now: dt.datetime) -> int:
    try:
        sig = read_signature(p7x_of(package))
    except (SignatureError, ValueError, IndexError, zipfile.BadZipFile) as e:
        print(f"::error::{package.name}: cannot read its signature: {e}")
        return 1
    cert = sig.signer
    print(f"{package.name}: signed by a certificate valid "
          f"{cert.not_before:%Y-%m-%d %H:%M}Z to {cert.not_after:%Y-%m-%d %H:%M}Z")
    if sig.timestamp_time:
        print(f"{package.name}: RFC 3161 timestamp {sig.timestamp_time:%Y-%m-%d %H:%M:%S}Z")
    cer = cer_path.read_bytes() if cer_path else None
    problems = judge(sig, now, min_days, cer)
    for p in problems:
        print(f"::error::{package.name}: {p}")
    if not problems:
        print(f"{package.name}: the signature keeps installing (certificate good for "
              f"{(cert.not_after - now).days} more days, and timestamped)")
    return 1 if problems else 0


# --- self-test ----------------------------------------------------------------


def _der(tag: int, body: bytes) -> bytes:
    n = len(body)
    if n < 0x80:
        return bytes([tag, n]) + body
    size = n.to_bytes((n.bit_length() + 7) // 8, "big")
    return bytes([tag, 0x80 | len(size)]) + size + body


def _seq(*parts: bytes) -> bytes:
    return _der(0x30, b"".join(parts))


def _set(*parts: bytes) -> bytes:
    return _der(0x31, b"".join(parts))


def _oid(dotted: str) -> bytes:
    nums = [int(p) for p in dotted.split(".")]
    body = bytes([40 * nums[0] + nums[1]])
    for n in nums[2:]:
        chunk = [n & 0x7F]
        n >>= 7
        while n:
            chunk.append(0x80 | (n & 0x7F))
            n >>= 7
        body += bytes(reversed(chunk))
    return _der(0x06, body)


def _time(when: dt.datetime, generalized: bool = False) -> bytes:
    if generalized:
        return _der(0x18, when.strftime("%Y%m%d%H%M%SZ").encode())
    return _der(0x17, when.strftime("%y%m%d%H%M%SZ").encode())


def _name(cn: str) -> bytes:
    return _seq(_set(_seq(_oid("2.5.4.3"), _der(0x0C, cn.encode()))))


def _cert(serial: int, cn: str, not_before: dt.datetime, not_after: dt.datetime) -> bytes:
    alg = _seq(_oid("1.2.840.113549.1.1.11"), b"\x05\x00")
    tbs = _seq(
        _der(0xA0, _der(0x02, b"\x02")),
        _der(0x02, serial.to_bytes(2, "big")),
        alg,
        _name(cn),
        _seq(_time(not_before), _time(not_after, generalized=not_after.year >= 2050)),
        _name(cn),
        _seq(alg, _der(0x03, b"\x00")),  # a stand-in public key
    )
    return _seq(tbs, alg, _der(0x03, b"\x00sig"))


def _signed_data(certs: list[bytes], signer_serial: int, signer_cn: str,
                 unsigned: bytes, econtent: bytes, econtent_type: str) -> bytes:
    alg = _seq(_oid("2.16.840.1.101.3.4.2.1"))
    info_parts = [
        _der(0x02, b"\x01"),
        _seq(_name(signer_cn), _der(0x02, signer_serial.to_bytes(2, "big"))),
        alg,
        _seq(_oid("1.2.840.113549.1.1.1")),
        _der(0x04, b"signature"),
    ]
    if unsigned:
        info_parts.append(_der(0xA1, unsigned))
    body = _seq(
        _der(0x02, b"\x01"),
        _set(alg),
        _seq(_oid(econtent_type), _der(0xA0, _der(0x04, econtent))),
        _der(0xA0, b"".join(certs)),
        _set(_seq(*info_parts)),
    )
    return _seq(_oid(OID_SIGNED_DATA), _der(0xA0, body))


def _token(gen_time: dt.datetime) -> bytes:
    tst = _seq(
        _der(0x02, b"\x01"),
        _oid("2.16.840.1.114412.7.1"),
        _seq(_seq(_oid("2.16.840.1.101.3.4.2.1")), _der(0x04, b"\x00" * 32)),
        _der(0x02, b"\x05"),
        _time(gen_time, generalized=True),
    )
    tsa = _cert(9, "Test TSA", gen_time - dt.timedelta(days=900), gen_time + dt.timedelta(days=900))
    return _signed_data([tsa], 9, "Test TSA", b"", tst, OID_TST_INFO)


def _p7(not_before: dt.datetime, not_after: dt.datetime, stamp: str,
        stamp_time: dt.datetime | None = None, extra_cert: bool = False) -> tuple[bytes, bytes]:
    cert = _cert(7, "CN=Test", not_before, not_after)
    certs = [cert]
    if extra_cert:
        certs.insert(0, _cert(8, "CN=Other", not_before, not_after))
    unsigned = b""
    if stamp == "rfc3161":
        unsigned = _seq(_oid(OID_RFC3161_COUNTERSIGN), _set(_token(stamp_time or not_before)))
    elif stamp == "legacy":
        unsigned = _seq(_oid(OID_LEGACY_COUNTERSIGN), _set(_seq(_der(0x02, b"\x01"))))
    return _signed_data(certs, 7, "CN=Test", unsigned, b"appx digests", "1.3.6.1.4.1.311.2.1.4"), cert


def self_test() -> int:
    now = dt.datetime(2026, 11, 20, 12, 0, tzinfo=dt.timezone.utc)
    day = dt.timedelta(days=1)
    two_years = (now - day, now + 730 * day)
    cases = [
        # (name, p7 arguments, expected problem fragments; empty means it passes)
        ("two years, RFC 3161", dict(not_before=two_years[0], not_after=two_years[1], stamp="rfc3161", stamp_time=now), []),
        ("signer not first", dict(not_before=two_years[0], not_after=two_years[1], stamp="rfc3161", stamp_time=now, extra_cert=True), []),
        ("3.4.0: one day, no timestamp", dict(not_before=now - 5 * day, not_after=now - 4 * day, stamp=""),
         ["days from now", "no timestamp"]),
        ("two years, no timestamp", dict(not_before=two_years[0], not_after=two_years[1], stamp=""), ["no timestamp"]),
        ("two years, legacy timestamp", dict(not_before=two_years[0], not_after=two_years[1], stamp="legacy"), ["legacy Authenticode"]),
        ("one day, RFC 3161", dict(not_before=now - day, not_after=now + day, stamp="rfc3161", stamp_time=now), ["days from now"]),
        ("364 days", dict(not_before=now, not_after=now + 364 * day, stamp="rfc3161", stamp_time=now), ["days from now"]),
        ("timestamp after expiry", dict(not_before=two_years[0], not_after=two_years[1], stamp="rfc3161", stamp_time=now + 800 * day),
         ["outside the certificate's validity"]),
    ]
    failures = 0
    for name, kwargs, expected in cases:
        p7, _ = _p7(**kwargs)
        problems = judge(read_signature(p7), now, DEFAULT_MIN_DAYS, None)
        ok = len(problems) == len(expected) and all(any(e in p for p in problems) for e in expected)
        print(f"{'ok  ' if ok else 'FAIL'} {name}: {problems or 'passes'}")
        failures += not ok

    # The published certificate must be the signer's own.
    p7, cert = _p7(*two_years, stamp="rfc3161", stamp_time=now)
    sig = read_signature(p7)
    ok = not judge(sig, now, DEFAULT_MIN_DAYS, cert) and judge(sig, now, DEFAULT_MIN_DAYS, cert + b"x")
    print(f"{'ok  ' if ok else 'FAIL'} the published certificate is compared byte for byte")
    failures += not ok

    # Through a package on disk: a signed one, an unsigned one and a bad header.
    with tempfile.TemporaryDirectory() as tmp:
        def package(name: str, p7x: bytes | None) -> Path:
            path = Path(tmp) / name
            with zipfile.ZipFile(path, "w") as z:
                z.writestr("AppxManifest.xml", "<Package/>")
                if p7x is not None:
                    z.writestr("AppxSignature.p7x", p7x)
            return path

        real_now = dt.datetime.now(dt.timezone.utc)
        good, _ = _p7(real_now - day, real_now + 730 * day, "rfc3161", real_now)
        for name, p7x, want in (("good.msix", b"PKCX" + good, 0), ("unsigned.msix", None, 1),
                                ("bad.msix", b"XXXX" + good, 1)):
            got = check(package(name, p7x), None, DEFAULT_MIN_DAYS, real_now)
            ok = got == want
            print(f"{'ok  ' if ok else 'FAIL'} {name} exits {got}")
            failures += not ok

    print("self-test:", "FAILED" if failures else "passed")
    return 1 if failures else 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("package", nargs="?", type=Path)
    parser.add_argument("--cer", type=Path, help="the certificate published beside the package")
    parser.add_argument("--min-days", type=int, default=DEFAULT_MIN_DAYS)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        return self_test()
    if not args.package:
        parser.error("a package, or --self-test")
    return check(args.package, args.cer, args.min_days, dt.datetime.now(dt.timezone.utc))


if __name__ == "__main__":
    sys.exit(main())
