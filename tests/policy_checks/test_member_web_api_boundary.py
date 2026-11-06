from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from tools.policy_checks.check_member_web_api_boundary import inspect_source_tree


VALID_APPOINTMENT = """
async function read(access, request=fetch) {
 const token = await access.getAccessToken();
 if (!token) throw new Error("signed out");
 return request('/api/v1/me/appointments', {
  cache: 'no-store', credentials: 'omit', redirect: 'error',
  headers: { Authorization: 'Bearer ' + token }
 });
}
"""
VALID_TARANOM = """
async function api(path) {
 const token=await window.taranomSession?.getAccessToken();
 if(!token) throw new Error("signed out");
 return fetch(path,{cache:'no-store',credentials:'omit',redirect:'error',
  headers:{'Authorization':'Bearer '+token}});
}
const endpoint='/api/v1/me/profile';
"""


class MemberWebApiBoundaryTests(unittest.TestCase):
    def check(self, files: dict[str, str]) -> list[str]:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "src"
            root.mkdir()
            for name, content in files.items():
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")
            return inspect_source_tree(root)

    def test_host_authenticated_appointment_client_is_allowed(self):
        self.assertEqual([], self.check({"appointment-client.ts": VALID_APPOINTMENT}))

    def test_host_authenticated_taranom_client_is_allowed(self):
        self.assertEqual([], self.check({"taranom-api.ts": VALID_TARANOM}))

    def test_only_exact_api_client_paths_are_allowlisted(self):
        errors = self.check({"screens/profile.tsx": "fetch('/api/v1/me/profile')"})
        self.assertTrue(any("outside approved client" in item for item in errors))

    def test_bearer_header_outside_allowlist_is_denied(self):
        errors = self.check({"screens/profile.tsx": "const auth = 'Bearer token'"})
        self.assertTrue(any("outside approved client" in item for item in errors))

    def test_api_client_without_host_identity_is_denied(self):
        errors = self.check({"appointment-client.ts": "fetch('/api/v1/me/appointments')"})
        self.assertTrue(any("obtain identity from its host" in item for item in errors))

    def test_api_client_without_no_store_or_no_redirect_is_denied(self):
        content = VALID_APPOINTMENT.replace(
            "cache: 'no-store', credentials: 'omit', redirect: 'error',",
            "cache: 'default', credentials: 'include', redirect: 'follow',"
        )
        errors = self.check({"appointment-client.ts": content})
        self.assertTrue(any("cache boundary" in item for item in errors))
        self.assertTrue(any("credentials boundary" in item for item in errors))
        self.assertTrue(any("redirect boundary" in item for item in errors))

    def test_browser_persisted_or_fake_state_is_denied_everywhere(self):
        errors = self.check({
            "appointment-client.ts": VALID_APPOINTMENT,
            "profile.ts": "localStorage.setItem('token','x')"
        })
        self.assertTrue(any("persisted/fake member state" in item for item in errors))

    def test_absolute_external_fetch_is_denied(self):
        errors = self.check({
            "appointment-client.ts": VALID_APPOINTMENT + "\nfetch('https://outside.invalid/api');"
        })
        self.assertTrue(any("absolute external fetch" in item for item in errors))

    def test_fixture_directory_is_denied(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "src"
            (root / "fixtures").mkdir(parents=True)
            self.assertTrue(any("synthetic/auth adapter" in item for item in inspect_source_tree(root)))


if __name__ == "__main__":
    unittest.main()
