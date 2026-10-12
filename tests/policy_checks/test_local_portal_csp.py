"""TRN-TECH-089: only ephemeral already-compiled ASP.NET bundles get self CSP."""
import os
from pathlib import Path
import sys
import unittest
from unittest.mock import patch
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/"tools/policy_checks"))
import authorize_local_portal_csp as csp

def sample(prefix):
    return ('<html><head><meta http-equiv="Content-Security-Policy" content='
        '"default-src \'none\'; base-uri \'none\'; form-action \'none\'; '
        'script-src \'self\'; connect-src \'none\'" /></head><body>'
        '<script type="module" src="'+prefix+'/assets/index-123.js"></script></body></html>')
class Tests(unittest.TestCase):
    def test_two_exact_local_build_bases_only(self):
        for prefix in ("/member-portal","/center-portal"):
            updated=csp.authorize(sample(prefix),prefix)
            self.assertEqual(updated.count("connect-src 'self'"),1)
            self.assertNotIn("connect-src 'none'",updated)
            self.assertIn("base-uri 'none'",updated)
            self.assertIn("form-action 'none'",updated)
    def test_wrong_base_malformed_and_repeated_csp_denied(self):
        for bad,prefix in (
            (sample("/member-portal"),"/center-portal"),
            (sample("/center-portal").replace("connect-src 'none'","connect-src *"),"/center-portal"),
            (sample("/member-portal").replace("base-uri 'none'","base-uri *"),"/member-portal"),
            (sample("/member-portal").replace("</head>",
                '<meta http-equiv="Content-Security-Policy" content="connect-src \'none\'"/></head>'),"/member-portal")):
            with self.assertRaises(csp.AdmissionError):csp.authorize(bad,prefix)
    def test_refuses_outside_github_actions_without_touching_any_file(self):
        with patch.dict(os.environ,{},clear=True):
            with self.assertRaises(csp.AdmissionError):
                csp.main(["--audience","member"])
if __name__=="__main__":unittest.main()
