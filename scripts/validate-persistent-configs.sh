#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
python_command="python3"
if ! command -v "$python_command" >/dev/null 2>&1; then
  python_command="python"
fi

"$python_command" - "$repo_root" <<'PY'
import json
import pathlib
import sys
import urllib.parse

root = pathlib.Path(sys.argv[1])
expected = {
    "providers": {"huggingface", "theporndb", "stashdb", "extractor-metadata", "extractor-thumbnail"},
    "plugins": {"emby", "jellyfin", "plex", "localai", "quickconnect"},
}

def validate_node(node, path):
    if isinstance(node, dict):
        for key, value in node.items():
            child = f"{path}.{key}"
            lowered = key.lower()
            sensitive = any(lowered == name or lowered.endswith(name) for name in ("apikey", "token", "password", "secret", "credential"))
            reference = "environmentvariable" in lowered or "securestoragekey" in lowered
            if sensitive and not reference and isinstance(value, str) and value.strip():
                raise ValueError(f"{child} contains a plaintext credential")
            if (lowered == "endpoint" or lowered.endswith("url")) and isinstance(value, str) and value.strip():
                parsed = urllib.parse.urlparse(value)
                if parsed.scheme not in {"http", "https"} or not parsed.netloc:
                    raise ValueError(f"{child} is not an absolute HTTP/HTTPS URL")
            validate_node(value, child)
    elif isinstance(node, list):
        for index, value in enumerate(node):
            validate_node(value, f"{path}[{index}]")

for kind, expected_ids in expected.items():
    directory = root / "ConfigDefaults" / kind
    actual_ids = {path.stem for path in directory.glob("*.json")}
    if actual_ids != expected_ids:
        raise SystemExit(f"{kind} config set mismatch: expected {sorted(expected_ids)}, found {sorted(actual_ids)}")
    for config_path in sorted(directory.glob("*.json")):
        with config_path.open(encoding="utf-8-sig") as stream:
            config = json.load(stream)
        if config.get("id") != config_path.stem:
            raise ValueError(f"{config_path} id does not match its filename")
        if config.get("schemaVersion", 0) < 2 or config.get("version", 0) < 2:
            raise ValueError(f"{config_path} has an outdated schema or config version")
        if not isinstance(config.get("enabled"), bool) or not str(config.get("name", "")).strip():
            raise ValueError(f"{config_path} is missing enabled/name")
        if not isinstance(config.get("settings"), dict):
            raise ValueError(f"{config_path} settings must be an object")
        validate_node(config["settings"], f"{kind}.{config_path.stem}.settings")

print("Verified 5 provider configs and 5 persistent add-in configs.")
PY
