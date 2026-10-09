import unittest
from unittest.mock import patch

from scripts.validate_cross_platform_release import (
    PLATFORMS,
    project_version,
    validate_matrix,
    validate_peer_ci,
)


def ready_matrix():
    return {
        "schemaVersion": 1,
        "product": "SpectraGrab",
        "productVersion": "0.3.0",
        "sharedFeatures": {
            feature_id: {platform: "implemented" for platform in PLATFORMS}
            for feature_id in (
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
            )
        },
        "platformLimitations": {},
    }


class ValidateMatrixTests(unittest.TestCase):
    def test_accepts_matching_version_and_verified_feature_states(self):
        self.assertEqual(validate_matrix(ready_matrix(), "0.3.0", "fixture"), [])

    def test_rejects_version_mismatch(self):
        errors = validate_matrix(ready_matrix(), "0.4.0", "fixture")

        self.assertTrue(any("productVersion" in error for error in errors))

    def test_rejects_non_object_matrix(self):
        self.assertTrue(validate_matrix([], "0.3.0", "fixture"))

    def test_rejects_missing_platform_state(self):
        matrix = ready_matrix()
        del matrix["sharedFeatures"]["direct-media"]["ios"]

        errors = validate_matrix(matrix, "0.3.0", "fixture")

        self.assertTrue(any("exactly these platform states" in error for error in errors))

    def test_rejects_unverified_state_and_requires_limitation_reason(self):
        matrix = ready_matrix()
        matrix["sharedFeatures"]["direct-media"]["ios"] = "platform-limited"

        errors = validate_matrix(matrix, "0.3.0", "fixture")

        self.assertTrue(any("documented platform limitation" in error for error in errors))

    def test_accepts_limited_feature_with_documented_reason(self):
        matrix = ready_matrix()
        matrix["sharedFeatures"]["direct-media"]["ios"] = "platform-limited"
        matrix["platformLimitations"]["direct-media"] = "iOS background transfer limits"

        self.assertEqual(validate_matrix(matrix, "0.3.0", "fixture"), [])

    def test_rejects_unverified_feature(self):
        matrix = ready_matrix()
        matrix["sharedFeatures"]["direct-media"]["android"] = "unverified"

        errors = validate_matrix(matrix, "0.3.0", "fixture")

        self.assertTrue(any("release requires implemented" in error for error in errors))

    def test_checks_platform_project_version(self):
        project = "<Project><PropertyGroup><ApplicationDisplayVersion>0.3.0</ApplicationDisplayVersion></PropertyGroup></Project>"

        self.assertEqual(
            project_version(project, "0.3.0", "fixture", "ApplicationDisplayVersion"),
            [],
        )
        self.assertTrue(
            project_version(project, "0.4.0", "fixture", "ApplicationDisplayVersion")
        )

    @patch(
        "scripts.validate_cross_platform_release.read_json_url",
        side_effect=[
            {"sha": "current"},
            {
                "workflow_runs": [
                    {
                        "head_sha": "current",
                        "id": 17,
                        "status": "completed",
                        "conclusion": "success",
                    }
                ]
            },
            {"artifacts": [{"name": "release-current", "expired": False}]},
        ],
    )
    def test_requires_successful_ci_for_current_main_commit(self, _read_json):
        self.assertEqual(
            validate_peer_ci("owner/repo", "ci.yml", "release-{sha}"), []
        )

    @patch(
        "scripts.validate_cross_platform_release.read_json_url",
        side_effect=[
            {"sha": "current"},
            {
                "workflow_runs": [
                    {
                        "head_sha": "stale",
                        "id": 17,
                        "status": "completed",
                        "conclusion": "failure",
                    }
                ]
            },
            {"artifacts": [{"name": "release-stale", "expired": False}]},
        ],
    )
    def test_rejects_stale_and_failed_ci(self, _read_json):
        errors = validate_peer_ci("owner/repo", "ci.yml", "release-{sha}")

        self.assertEqual(len(errors), 2)
        self.assertTrue(any("latest main commit" in error for error in errors))
        self.assertTrue(any("completed/failure" in error for error in errors))

    @patch(
        "scripts.validate_cross_platform_release.read_json_url",
        side_effect=[
            {"sha": "current"},
            {
                "workflow_runs": [
                    {
                        "head_sha": "current",
                        "id": 17,
                        "status": "completed",
                        "conclusion": "success",
                    }
                ]
            },
            {"artifacts": []},
        ],
    )
    def test_rejects_successful_ci_without_required_release_artifact(self, _read_json):
        errors = validate_peer_ci("owner/repo", "ci.yml", "release-{sha}")

        self.assertTrue(any("missing unexpired artifact release-current" in error for error in errors))

    def test_rejects_different_shared_feature_ids(self):
        matrix = ready_matrix()
        matrix["sharedFeatures"] = {
            feature_id: {platform: "implemented" for platform in PLATFORMS}
            for feature_id in ("direct-media", "unrecognized")
        }

        errors = validate_matrix(matrix, "0.3.0", "fixture", {"direct-media", "hls"})

        self.assertTrue(any("missing shared feature IDs: hls" in error for error in errors))
        self.assertTrue(
            any(
                "unknown shared feature IDs: unrecognized" in error
                for error in errors
            )
        )


if __name__ == "__main__":
    unittest.main()
