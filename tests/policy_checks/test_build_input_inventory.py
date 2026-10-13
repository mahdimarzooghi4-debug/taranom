"""TRN-TECH-120: offline adversarial build-input declaration verification."""
import base64,json,pathlib,sys,tempfile,unittest
sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[2]/"tools"/"policy_checks"))
import check_build_input_inventory as inv

def put(root,rel,content):
    p=root/rel;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(content)
def prepare(root):
    put(root,"global.json",json.dumps({"sdk":{"version":"10.0.401","rollForward":"disable"}}))
    proof="sha512-"+base64.b64encode(bytes(range(64))).decode()
    for app in inv.APPS:
        base="apps/"+app
        pkg={"name":"@taranom/"+app,"version":"0.1.0",
             "dependencies":{"react":"19.2.0"},
             "devDependencies":{"typescript":"5.9.3"}}
        lock={"lockfileVersion":3,"packages":{"":dict(pkg),"node_modules/react":{
            "version":"19.2.0","resolved":"https://registry.npmjs.org/react/-/react-19.2.0.tgz",
            "integrity":proof}}}
        put(root,base+"/package.json",json.dumps(pkg))
        put(root,base+"/package-lock.json",json.dumps(lock))
    put(root,"src/A/A.csproj",'<Project><PackageReference Include="Npgsql" Version="10.0.3" /></Project>')
    put(root,".github/workflows/taranom-full-regression-reusable.yml",
        "steps:\n  - uses: actions/checkout@"+"a"*40+"\nservices:\n    image: postgres:18.6\n")

class BuildInventoryTests(unittest.TestCase):
    def setUp(self):
        t=tempfile.TemporaryDirectory();self.addCleanup(t.cleanup)
        self.root=pathlib.Path(t.name);prepare(self.root)
    def check(self):return inv.inspect(self.root)
    def denied(self,code):
        v=self.check();self.assertEqual("SOURCE_UNVERIFIABLE",v["status"],v)
        self.assertIn(code,v["errors"]);self.assertIsNone(v["sourceFingerprint"])
        self.assertFalse(v["nationalReleaseAllowed"])
    def test_declared_only_never_actual_attestation(self):
        a=self.check();self.assertEqual("DECLARED_BUILD_INPUTS_CHECKED_NOT_ATTESTED",a["status"],a)
        self.assertEqual(5,a["npmApplications"]);self.assertEqual(5,a["npmLockedPackages"])
        self.assertEqual(1,a["nugetPackageDeclarations"])
        self.assertEqual(1,a["pinnedGithubActions"])
        self.assertEqual(1,a["mutableContainerTags"])
        self.assertEqual(a["sourceFingerprint"],self.check()["sourceFingerprint"])
        for k in ("nugetRestoreBytesLocked","containerDigestPinned",
                  "dependencyVulnerabilityReviewCompleted","supplyChainAttested",
                  "productionReady","nationalReleaseAllowed"):
            self.assertFalse(a[k],k)
    def test_mutation_changes_source_fingerprint_without_approval(self):
        old=self.check()["sourceFingerprint"]
        put(self.root,"src/A/A.csproj",'<Project><PackageReference Include="Npgsql" Version="10.0.4" /></Project>')
        new=self.check()
        self.assertNotEqual(old,new["sourceFingerprint"])
        self.assertFalse(new["nationalReleaseAllowed"])
    def test_duplicate_json_and_missing_source_fail_closed(self):
        (self.root/"global.json").unlink();self.denied("SOURCE_READ_OR_PARSE_FAILED")
        put(self.root,"global.json",'{"sdk":{"version":"10.0.401","version":"10.0.402","rollForward":"disable"}}')
        self.denied("DUPLICATE_JSON_KEY")
    def test_unpinned_sdk_and_nuget_fail_closed(self):
        put(self.root,"global.json",'{"sdk":{"version":"10.0.401","rollForward":"latestFeature"}}')
        self.denied("UNPINNED_DOTNET_SDK")
        prepare(self.root)
        put(self.root,"src/A/A.csproj",'<Project><PackageReference Include="Npgsql" Version="10.*" /></Project>')
        self.denied("FLOATING_NUGET_PACKAGE_VERSION")
    def test_npm_stale_lock_and_external_source_denied(self):
        p=self.root/"apps/ops-web/package-lock.json"
        x=json.loads(p.read_text());x["packages"][""]["dependencies"]["react"]="19.1.0"
        p.write_text(json.dumps(x));self.denied("STALE_NPM_DIRECT_DEPENDENCY")
        prepare(self.root);x=json.loads(p.read_text())
        x["packages"]["node_modules/react"]["resolved"]="http://malicious.invalid/tgz"
        p.write_text(json.dumps(x));self.denied("UNPINNED_NPM_PACKAGE_SOURCE")
        prepare(self.root);x=json.loads(p.read_text())
        x["packages"]["node_modules/react"]["integrity"]="sha512-aaaa"
        p.write_text(json.dumps(x));self.denied("INVALID_NPM_INTEGRITY")
    def test_symlinked_lock_and_unpinned_ci_action_denied(self):
        p=self.root/"apps/member-web/package-lock.json"
        original=p.read_text();p.unlink()
        other=self.root/"fake.json";other.write_text(original);p.symlink_to(other)
        self.denied("SYMLINKED_SOURCE")
        p.unlink();p.write_text(original)
        put(self.root,".github/workflows/taranom-full-regression-reusable.yml",
            "steps:\n  - uses: actions/checkout@v4\nservices:\n    image: postgres:18.6\n")
        self.denied("UNPINNED_GITHUB_ACTION")
    def test_tag_only_db_image_remains_a_nonproduction_blocker(self):
        a=self.check()
        self.assertEqual(1,a["mutableContainerTags"])
        self.assertFalse(a["containerDigestPinned"])
        self.assertFalse(a["supplyChainAttested"])
if __name__=="__main__":unittest.main()
