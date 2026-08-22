import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET
REPO=Path(__file__).resolve().parents[1]; DRIVER=REPO/"tests/run_api36_gear_sleaze_swap_e2e.py"
class DriverTests(unittest.TestCase):
    def test_driver_is_exact_phone_api36_arm64_digest_restart_bound(self):
        source=DRIVER.read_text(); module=ast.parse(source)
        assignment=next(n for n in module.body if isinstance(n,ast.Assign) and any(isinstance(t,ast.Name) and t.id=="CONTROLS" for t in n.targets))
        self.assertEqual(("CharacterCreate.cboGearSleaze","CharacterCareer.cboGearSleaze"),tuple(ast.literal_eval(assignment.value)))
        for marker in ('"profile":"phone"','api != "36"','"arm64-v8a" not in abi.split(",")','shared.PACKAGE','device.shell("am", "force-stop"','"dataProcessingNotificationOnly"','"activeHomeStatePreserved"','"gearMatrixSwapRulesSha256"','"attackSharedDriverSha256"'): self.assertIn(marker,source)
    def test_fixtures_pin_raw_bonus_state_and_unique_identity(self):
        for name,created in (("creation-gear-sleaze-swap-e2e.chum5","False"),("career-gear-sleaze-swap-e2e.chum5","True")):
            root=ET.parse(REPO/"tests/fixtures"/name).getroot(); self.assertEqual(created,root.findtext("created"))
            target=root.find("./gears/gear/children/gear"); self.assertEqual("True",target.findtext("canswapattributes")); self.assertTrue(target.findtext("modsleaze")); self.assertEqual("True",target.findtext("homenode"))
            ids=[x.findtext("guid") for x in root.findall(".//gear")]; self.assertEqual(len(ids),len(set(ids)))
if __name__=="__main__": unittest.main()
