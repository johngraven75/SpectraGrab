import argparse
import json
import re
import sys
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
FEATURE_PARITY_PATH = ROOT / "FEATURE_PARITY.json"
WINDOWS_PROJECT_PATH = ROOT / "SpectraGrab.csproj"
PLATFORMS = ("windows", "android", "ios")
PEERS = (
    {
        "repository": "johngraven75/SpectraGrab-Android",
        "project": "SpectraGrab.Android.csproj",
        "workflow": "android-ci-release.yml",
        "artifact": "SpectraGrab-Android-production-{sha}",
    },
    {
        "repository": "johngraven75/SpectraGrab-iOS",
        "project": "SpectraGrab.iOS.csproj",
        "workflow": "ios-ci.yml",
        "artifact": "SpectraGrab-iOS-Release-Archive-{sha}",
    },
)
READY_STATES = {"implemented", "platform-limited"}
REQUIRED_FEATURE_IDS = frozenset(
    {
        "url-inspection",
        "extractor-compatibility",
        "generic-provider-fallback",
        "direct-media",
        "hls",
        "dash",
        "playlists",
        "bounded-deep-crawl",
        "queue-pause-cancel-resume",
        "retries",
        "format-quality-selection",
        "captcha-human-handoff",
        "authorized-session-handling",
        "ffmpeg-merge-transcode",
        "provider-profiles",
    }
)


def validate_matrix(matrix, version, source, expected_feature_ids=None):
    errors = []
    if not isinstance(matrix, dict):
        return [f"{source}: parity matrix must be a JSON object"]
    if matrix.get("schemaVersion") != 1 or matrix.get("product") != "SpectraGrab":
        errors.append(f"{source}: unsupported or missing SpectraGrab parity schema")
    if matrix.get("productVersion") != version:
        errors.append(
            f"{source}: productVersion must be {version!r}, got {matrix.get('productVersion')!r}"
        )

    features = matrix.get("sharedFeatures")
    if not isinstance(features, dict) or not features:
        errors.append(f"{source}: sharedFeatures must be a non-empty object")
        return errors
    required_ids = expected_feature_ids or REQUIRED_FEATURE_IDS
    if set(features) != required_ids:
        missing = sorted(required_ids - set(features))
        extra = sorted(set(features) - required_ids)
        if missing:
            errors.append(f"{source}: missing shared feature IDs: {', '.join(missing)}")
        if extra:
            errors.append(f"{source}: unknown shared feature IDs: {', '.join(extra)}")

    limitations = matrix.get("platformLimitations", {})
    if not isinstance(limitations, dict):
        errors.append(f"{source}: platformLimitations must be an object")
        limitations = {}

    for feature_id, platform_states in features.items():
        if not isinstance(platform_states, dict):
            errors.append(f"{source}: {feature_id} must define a state for each platform")
            continue
        if set(platform_states) != set(PLATFORMS):
            errors.append(
                f"{source}: {feature_id} must define exactly these platform states: "
                f"{', '.join(PLATFORMS)}"
            )
            continue
        for platform, state in platform_states.items():
            if not isinstance(state, str) or state not in READY_STATES:
                errors.append(
                    f"{source}: {feature_id}/{platform} is {state!r}; "
                    "release requires implemented or an approved platform limitation"
                )
            reason = limitations.get(feature_id)
            if state == "platform-limited" and (
                not isinstance(reason, str) or not reason.strip()
            ):
                errors.append(
                    f"{source}: {feature_id}/{platform} needs a documented platform limitation"
                )
    return errors


def read_json_url(url):
    request = urllib.request.Request(
        url,
        headers={"Accept": "application/json", "User-Agent": "SpectraGrab-release-gate"},
    )
    with urllib.request.urlopen(request, timeout=20) as response:
        return json.load(response)


def read_text_url(url):
    request = urllib.request.Request(
        url, headers={"User-Agent": "SpectraGrab-release-gate"}
    )
    with urllib.request.urlopen(request, timeout=20) as response:
        return response.read().decode("utf-8")


def project_version(project_xml, version, source, version_element):
    try:
        root = ET.fromstring(project_xml)
    except ET.ParseError as error:
        return [f"{source}: invalid project XML: {error}"]

    actual = root.findtext(f".//{version_element}")
    if actual != version:
        return [
            f"{source}: {version_element} must be {version!r}, got {actual!r}"
        ]
    return []


def validate_peer_ci(repository, workflow, expected_artifact):
    base_url = f"https://api.github.com/repos/{repository}"
    errors = []
    try:
        main = read_json_url(f"{base_url}/commits/main")
        runs = read_json_url(
            f"{base_url}/actions/workflows/{workflow}/runs?branch=main&per_page=1"
        )
    except (OSError, ValueError, urllib.error.URLError) as error:
        return [f"{repository}: unable to verify latest CI run: {error}"]

    if not isinstance(main, dict) or not isinstance(runs, dict):
        return [f"{repository}: unexpected GitHub API response for {workflow}"]
    latest_runs = runs.get("workflow_runs", [])
    if not latest_runs:
        return [f"{repository}: no main-branch run found for {workflow}"]

    latest = latest_runs[0]
    if latest.get("head_sha") != main.get("sha"):
        errors.append(
            f"{repository}: {workflow} has not validated the latest main commit "
            f"{main.get('sha')}"
        )
    if latest.get("status") != "completed" or latest.get("conclusion") != "success":
        errors.append(
            f"{repository}: latest {workflow} run is "
            f"{latest.get('status')}/{latest.get('conclusion')}"
        )
    if latest.get("id") is not None:
        try:
            artifacts = read_json_url(
                f"{base_url}/actions/runs/{latest['id']}/artifacts?per_page=100"
            )
        except (OSError, ValueError, urllib.error.URLError) as error:
            errors.append(f"{repository}: unable to verify release artifacts: {error}")
        else:
            if not isinstance(artifacts, dict):
                errors.append(f"{repository}: unexpected release artifact API response")
                return errors
            artifact_name = expected_artifact.format(sha=latest.get("head_sha", ""))
            matching_artifact = any(
                isinstance(artifact, dict)
                and artifact.get("name") == artifact_name
                and not artifact.get("expired")
                for artifact in artifacts.get("artifacts", [])
            )
            if not matching_artifact:
                errors.append(
                    f"{repository}: current CI run is missing unexpired artifact "
                    f"{artifact_name}"
                )
    return errors


def validate_release(version):
    errors = []
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        return ["release version must use MAJOR.MINOR.PATCH"]

    try:
        with FEATURE_PARITY_PATH.open(encoding="utf-8") as file:
            windows_matrix = json.load(file)
        errors.extend(validate_matrix(windows_matrix, version, "Windows matrix"))
    except (OSError, json.JSONDecodeError) as error:
        return [f"Windows matrix: unable to read valid JSON: {error}"]
    if not isinstance(windows_matrix, dict):
        return ["Windows matrix: parity matrix must be a JSON object"]

    try:
        windows_project = WINDOWS_PROJECT_PATH.read_text(encoding="utf-8")
        errors.extend(
            project_version(windows_project, version, "Windows project", "Version")
        )
    except OSError as error:
        errors.append(f"Windows project: unable to read project file: {error}")

    for peer in PEERS:
        repository = peer["repository"]
        raw_url = f"https://raw.githubusercontent.com/{repository}/main"
        try:
            matrix = json.loads(read_text_url(f"{raw_url}/FEATURE_PARITY.json"))
            errors.extend(
                validate_matrix(
                    matrix,
                    version,
                    f"{repository} matrix",
                    REQUIRED_FEATURE_IDS,
                )
            )
        except (OSError, ValueError, urllib.error.URLError) as error:
            errors.append(f"{repository}: unable to read parity matrix: {error}")

        try:
            project_xml = read_text_url(f"{raw_url}/{peer['project']}")
            errors.extend(
                project_version(
                    project_xml,
                    version,
                    f"{repository} project",
                    "ApplicationDisplayVersion",
                )
            )
        except (OSError, urllib.error.URLError) as error:
            errors.append(f"{repository}: unable to read project version: {error}")

        errors.extend(
            validate_peer_ci(repository, peer["workflow"], peer["artifact"])
        )
    return errors


def main():
    parser = argparse.ArgumentParser(
        description="Fail closed unless all three platforms satisfy release parity."
    )
    parser.add_argument("--version", required=True, help="Product MAJOR.MINOR.PATCH version")
    args = parser.parse_args()

    errors = validate_release(args.version)
    if errors:
        for error in errors:
            print(f"BLOCKED: {error}", file=sys.stderr)
        return 1
    print(f"Cross-platform release readiness verified for SpectraGrab {args.version}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
