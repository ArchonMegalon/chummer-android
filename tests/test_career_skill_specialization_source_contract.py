from pathlib import Path
import unittest


REPO = Path(__file__).resolve().parents[1]


class CareerSkillSpecializationSourceContractTests(unittest.TestCase):
    def test_phone_page_binds_typed_selection_quote_digests_and_confirmation(self) -> None:
        page = (
            REPO / "src/Chummer.Android/Native/CareerSkillSpecializationPage.cs"
        ).read_text(encoding="utf-8")
        for automation_id in (
            "career-skill-specialization-page",
            "career-skill-specialization-skill-picker",
            "career-skill-specialization-option-picker",
            "career-skill-specialization-custom-name",
            "career-skill-specialization-identity",
            "career-skill-specialization-rating",
            "career-skill-specialization-selection-origin",
            "career-skill-specialization-quote",
            "career-skill-specialization-group-consequence",
            "career-skill-specialization-blocker",
            "career-skill-specialization-review",
            "career-skill-specialization-rook",
        ):
            self.assertIn(f'"{automation_id}"', page)
        self.assertIn("CharacterCareerSkillKind.Active", page)
        self.assertIn("CharacterCareerSkillKind.Knowledge", page)
        self.assertIn("CharacterCareerSkillSpecializationOptionKind.Custom", page)
        self.assertIn("CharacterCareerSkillSpecializationRules.IsCoherent", page)
        self.assertIn("PrepareCareerSkillSpecializationQuoteAsync", page)
        self.assertIn("quote.CharacterRevision", page)
        self.assertIn("quote.SourceRevision", page)
        self.assertIn("quote.RuleDigest", page)
        self.assertIn("quote.LogicalRevision", page)
        self.assertIn("Confirmed: true", page)
        self.assertIn("SpecializationId: Guid.NewGuid()", page)
        self.assertIn("ExpenseId: Guid.NewGuid()", page)
        self.assertIn('"Buy specialization"', page)
        self.assertIn('"Cancel"', page)
        self.assertNotIn("tablet", page.lower())

    def test_rook_navigation_is_a_question_path_not_confirmation(self) -> None:
        page = (
            REPO / "src/Chummer.Android/Native/CareerSkillSpecializationPage.cs"
        ).read_text(encoding="utf-8")
        self.assertGreaterEqual(page.count("new RookConversationPage(Coordinator)"), 2)
        self.assertIn("without confirming this purchase", page)
        rook_section = page.split('automationId: "career-skill-specialization-rook-entry"', 1)[0]
        self.assertNotIn("ApplyCareerSkillSpecializationAsync", rook_section.split("Ask Rook")[-1])


if __name__ == "__main__":
    unittest.main()
