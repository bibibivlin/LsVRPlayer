"""Verify a signed Android release and write public, reproducible release records."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ANDROID = "{http://schemas.android.com/apk/res/android}"
PACKAGE = "com.linxyi.LsVRPlayer"


def project_version(root):
    text = (root / "ProjectSettings/ProjectSettings.asset").read_text(encoding="utf-8")
    version = re.search(r"^  bundleVersion: (\S+)$", text, re.M).group(1)
    code = int(re.search(r"^  AndroidBundleVersionCode: (\d+)$", text, re.M).group(1))
    if not re.fullmatch(r"\d+\.\d+\.\d+", version) or code < 1:
        raise ValueError("Release version must be major.minor.patch with a positive version code")
    return version, code


def verify_manifest(text, version, code):
    manifest = ET.fromstring(text)
    app = manifest.find("application")
    sdk = manifest.find("uses-sdk")
    if manifest.get("package") != PACKAGE:
        raise ValueError("Unexpected application package")
    if manifest.get(ANDROID + "versionName") != version or manifest.get(ANDROID + "versionCode") != str(code):
        raise ValueError("APK version does not match the project release version")
    if app is None or sdk is None:
        raise ValueError("Missing Android application or SDK declaration")
    for flag in ("debuggable", "testOnly"):
        if app.get(ANDROID + flag, "false").lower() not in ("false", "0"):
            raise ValueError("Release APK must not enable " + flag)
    if sdk.get(ANDROID + "minSdkVersion") != "29" or sdk.get(ANDROID + "targetSdkVersion") != "35":
        raise ValueError("Unexpected Android SDK configuration")


def verify_signer(text, expected):
    fingerprints = re.findall(r"^Signer #\d+ certificate SHA-256 digest:\s*([0-9a-fA-F]+)\s*$", text, re.M)
    expected = expected.replace(":", "").strip().lower()
    if not re.fullmatch(r"[0-9a-f]{64}", expected):
        raise ValueError("Invalid expected signing certificate fingerprint")
    if len(fingerprints) != 1 or fingerprints[0].lower() != expected:
        raise ValueError("APK signing certificate does not match the selected release key")
    return expected


def test_summary(path):
    report = ET.parse(path).getroot()
    summary = {key: int(report.get(key, "0")) for key in ("passed", "failed", "skipped", "inconclusive")}
    if report.get("result") != "Passed" or summary["passed"] == 0 or summary["failed"] or summary["inconclusive"]:
        raise ValueError("Unity EditMode tests did not pass")
    return summary


def run_tool(args):
    result = subprocess.run([str(arg) for arg in args], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if result.returncode:
        # Tool diagnostics may contain machine paths; preserve raw logs locally only.
        raise ValueError(Path(args[0]).name + " failed with exit code " + str(result.returncode))
    return result.stdout.decode("utf-8", errors="strict")


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apk", required=True, type=Path)
    parser.add_argument("--apkanalyzer", required=True, type=Path)
    parser.add_argument("--apksigner", required=True, type=Path)
    parser.add_argument("--expected-cert-sha256", required=True)
    parser.add_argument("--tests", required=True, type=Path)
    parser.add_argument("--commit", required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    version, code = project_version(root)
    if not re.fullmatch(r"[0-9a-f]{40}", args.commit):
        raise ValueError("Release source commit must be a complete Git SHA")
    verify_manifest(run_tool([args.apkanalyzer, "manifest", "print", args.apk]), version, code)
    signer = verify_signer(run_tool([args.apksigner, "verify", "--verbose", "--print-certs", args.apk]), args.expected_cert_sha256)
    with zipfile.ZipFile(args.apk) as apk:
        libraries = {name for name in apk.namelist() if name.startswith("lib/") and name.endswith(".so")}
        abis = sorted({name.split("/")[1] for name in libraries})
        if abis != ["arm64-v8a"]:
            raise ValueError("Release APK must contain ARM64 native libraries only")
        for required in ("libvrplayerrender.so", "libil2cpp.so", "libunity.so"):
            if "lib/arm64-v8a/" + required not in libraries:
                raise ValueError("Missing required ARM64 library: " + required)
    unity = (root / "ProjectSettings/ProjectVersion.txt").read_text().splitlines()[0].split(": ", 1)[1]
    record = {
        "version": version, "versionCode": code, "package": PACKAGE, "sourceCommit": args.commit,
        "unityVersion": unity, "nativeAbis": abis, "minSdk": 29, "targetSdk": 35,
        "debuggable": False, "testOnly": False, "signingCertificateSha256": signer,
        "apk": args.apk.name, "apkBytes": args.apk.stat().st_size, "apkSha256": digest(args.apk),
        "editModeTests": test_summary(args.tests), "verifiedAtUtc": datetime.now(timezone.utc).isoformat(),
    }
    manifest = args.apk.parent / "release-manifest.json"
    manifest.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    checksums = f"{record['apkSha256']}  {args.apk.name}\n{digest(manifest)}  {manifest.name}\n"
    (args.apk.parent / "SHA256SUMS.txt").write_text(checksums, encoding="utf-8")
    print(json.dumps(record, indent=2))


if __name__ == "__main__":
    main()
